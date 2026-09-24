#Requires -Version 5.1
<#
.SYNOPSIS
    TraceLens 배포물을 로컬에서 만든다(.NET tool 패키지와 Windows portable ZIP).

.DESCRIPTION
    소스와 Node.js가 없는 환경에서도 실행할 수 있는 배포물을 만든다.
    클라이언트를 먼저 프로덕션 빌드해서 wwwroot에 넣고, CLI를 publish/pack 한다.

    이 스크립트는 만들기만 한다. 게시(nuget push 등)와 공개 홍보는 하지 않는다.
    산출물마다 SHA256 체크섬과 버전을 artifacts/manifest.txt에 남긴다.

.EXAMPLE
    .\pack.ps1

.EXAMPLE
    .\pack.ps1 -Configuration Release -Version 0.7.0

.EXAMPLE
    .\pack.ps1 -Runtimes @('win-x64','linux-x64','osx-arm64')
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    # 패키지 버전. 비우면 프로젝트 기본값을 쓴다.
    [string]$Version,
    # ZIP을 만들 런타임 식별자(RID). 여러 개를 주면 각각 만든다.
    # 만드는 것과 "지원한다"는 다르다 — 사람이 그 OS에서 확인하기 전까지 README 지원 표는 바꾸지 않는다.
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string[]]$Runtimes = @('win-x64'),
    # 클라이언트 빌드를 건너뛴다(이미 wwwroot가 최신인 경우).
    [switch]$SkipClient,
    # .NET tool 패키지(nupkg)를 만들지 않는다.
    [switch]$SkipTool,
    # portable ZIP을 만들지 않는다.
    [switch]$SkipZip
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = $PSScriptRoot
$clientPath = Join-Path $repositoryRoot 'client'
$cliProject = Join-Path $repositoryRoot 'src\TraceLens.Cli\TraceLens.Cli.csproj'
$artifacts = Join-Path $repositoryRoot 'artifacts'

function Invoke-Step {
    param([string]$Title, [scriptblock]$Action)

    Write-Host "==> $Title" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Title 이(가) 실패했다 (종료 코드 $LASTEXITCODE)."
    }
}

function Assert-Command {
    param([string]$Name, [string]$Hint)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name 을(를) 찾을 수 없다. $Hint"
    }
}

Assert-Command 'dotnet' '.NET 10 이상 SDK를 설치해야 한다.'
if (-not $SkipClient) {
    Assert-Command 'npm.cmd' 'Node.js 20 이상을 설치해야 한다. (-SkipClient로 건너뛸 수 있다)'
}

if (Test-Path $artifacts) {
    Remove-Item $artifacts -Recurse -Force
}
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null

# 클라이언트를 먼저 빌드해야 wwwroot가 배포물에 들어간다.
if (-not $SkipClient) {
    if (-not (Test-Path (Join-Path $clientPath 'node_modules'))) {
        Push-Location $clientPath
        try { Invoke-Step '클라이언트 의존성 설치' { npm.cmd install } } finally { Pop-Location }
    }

    Push-Location $clientPath
    try { Invoke-Step '클라이언트 빌드' { npm.cmd run build } } finally { Pop-Location }
}

$wwwroot = Join-Path $repositoryRoot 'src\TraceLens.Server\wwwroot\index.html'
if (-not (Test-Path $wwwroot)) {
    throw "웹 빌드 결과를 찾을 수 없다: $wwwroot (-SkipClient 없이 다시 실행한다.)"
}

# -SkipClient는 "이미 최신"이라는 사람의 주장이다. 파일이 있다는 것만으로는 확인이 안 된다.
# 클라이언트 소스보다 wwwroot가 오래됐으면 옛 UI가 배포물에 들어가므로 여기서 멈춘다.
if ($SkipClient) {
    $builtAt = (Get-Item $wwwroot).LastWriteTimeUtc
    $newestSource = Get-ChildItem (Join-Path $clientPath 'src') -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    $clientConfigs = @('package.json', 'vite.config.ts', 'index.html') |
        ForEach-Object { Join-Path $clientPath $_ } | Where-Object { Test-Path $_ } | ForEach-Object { Get-Item $_ }
    $newest = @($newestSource) + @($clientConfigs) | Where-Object { $_ } |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($newest -and $newest.LastWriteTimeUtc -gt $builtAt) {
        throw ("wwwroot가 클라이언트 소스보다 오래됐다. " +
            "wwwroot: $($builtAt.ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss')), " +
            "$($newest.Name): $($newest.LastWriteTimeUtc.ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss')). " +
            '-SkipClient 없이 다시 실행한다.')
    }

    Write-Host "==> wwwroot 확인: $($builtAt.ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss'))에 빌드됨 (클라이언트 소스보다 최신)" -ForegroundColor Green
}

$versionArgs = @()
if ($Version) { $versionArgs = @("-p:Version=$Version") }

$builtRuntimes = @()
if (-not $SkipZip) {
    foreach ($runtime in $Runtimes) {
        $publishDirectory = Join-Path $artifacts "publish\$runtime"
        Invoke-Step "$runtime portable 배포" {
            dotnet publish $cliProject -c $Configuration -r $runtime --self-contained false `
                -o $publishDirectory --nologo @versionArgs
        }

        # publish 결과에 MCP 실행에 필요한 파일이 들어갔는지 확인한다. 없으면 배포물이 반쪽이다.
        # 실행 파일 이름은 RID마다 다르다(Windows만 .exe).
        $hostExecutable = if ($runtime.StartsWith('win-')) { 'tracelens.exe' } else { 'tracelens' }
        $required = @($hostExecutable, 'tracelens.dll', 'tracelens.deps.json', 'tracelens.runtimeconfig.json',
            'TraceLens.Analyzer.dll', 'TraceLens.Context.dll', 'TraceLens.Mcp.dll', 'TraceLens.Server.dll')
        foreach ($name in $required) {
            if (-not (Test-Path (Join-Path $publishDirectory $name))) {
                throw "$runtime 배포물에 $name 이(가) 없다. 패키지 구성을 확인해야 한다."
            }
        }

        foreach ($directory in @('wwwroot', 'samples', 'docs')) {
            if (-not (Test-Path (Join-Path $publishDirectory $directory))) {
                throw "$runtime 배포물에 $directory 폴더가 없다."
            }
        }

        # 설명서와 설정 스키마가 빠지면 `/manual/ko`와 `tracelens init`이 조용히 실패한다.
        $docFiles = @('manual.html', 'manual_kr.html', 'tracelens.config.schema.json')
        foreach ($name in $docFiles) {
            if (-not (Test-Path (Join-Path $publishDirectory "docs\$name"))) {
                throw "$runtime 배포물의 docs에 $name 이(가) 없다. 설명서 없이 배포하면 안 된다."
            }
        }

        $zipPath = Join-Path $artifacts "tracelens-$runtime.zip"
        Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath -Force
        $builtRuntimes += $runtime
        Write-Host "==> portable ZIP: $zipPath" -ForegroundColor Green
    }
}

if (-not $SkipTool) {
    Invoke-Step '.NET tool 패키지 생성' {
        dotnet pack $cliProject -c $Configuration -o $artifacts --nologo @versionArgs
    }
}

# 체크섬·버전·검증 기록을 남긴다. 게시 전에 사람이 확인할 수 있어야 한다.
$manifestPath = Join-Path $artifacts 'manifest.txt'
$lines = @(
    "TraceLens 배포물",
    "만든 시각: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')",
    "구성: $Configuration",
    "버전: $(if ($Version) { $Version } else { '(프로젝트 기본값)' })",
    "dotnet: $(dotnet --version)",
    "만든 RID: $(if ($builtRuntimes.Count -gt 0) { $builtRuntimes -join ', ' } else { '(ZIP 없음)' })",
    "",
    "== 파일 (SHA256) =="
)
Get-ChildItem $artifacts -File | Where-Object { $_.Name -ne 'manifest.txt' } | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines += "{0}  {1}  {2:N0} bytes" -f $hash, $_.Name, $_.Length
}
$lines += ""
$lines += "== 수동 검증 절차 =="
$lines += '1. 다른 폴더에 ZIP을 풀고 tracelens doctor --target <대상> 을 실행한다.'
$lines += '2. tracelens analyze <대상> 으로 분석하고 .tracelens/graph.json 이 생기는지 확인한다.'
$lines += '3. tracelens serve --target <대상> 으로 UI를 열어 소스 보기까지 확인한다.'
$lines += '4. 생성한 MCP 설정으로 AI 도구에서 get_analysis_status 를 호출해 본다.'
$lines += '5. 공백·한글이 든 경로(예: C:\테스트 폴더\My App)에 풀어 doctor · analyze · serve · 소스 보기를 확인한다.'
$lines += '6. 위 결과를 이 파일 아래에 날짜와 함께 적는다. 확인하지 않은 운영체제는 지원 표에 넣지 않는다.'
$lines += ''
$lines += "== 만든 것과 지원하는 것은 다르다 =="
$lines += 'ZIP을 만들었다는 것은 그 RID로 publish가 성공했다는 뜻일 뿐이다.'
$lines += '해당 OS에서 doctor · analyze · serve · 소스 보기를 사람이 확인하기 전까지 README 지원 표는 바꾸지 않는다.'
$lines += 'Windows에서 만든 linux/osx ZIP은 Compress-Archive가 실행 권한을 보존하지 않는다.'
$lines += '푼 뒤 `chmod +x tracelens` 가 필요하다.'
$lines | Set-Content -Path $manifestPath -Encoding utf8

Write-Host "==> 완료. 산출물: $artifacts" -ForegroundColor Green
Get-Content $manifestPath | Write-Host
