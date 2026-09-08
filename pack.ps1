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
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    # 패키지 버전. 비우면 프로젝트 기본값을 쓴다.
    [string]$Version,
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
$publishDirectory = Join-Path $artifacts 'publish\win-x64'

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

Assert-Command 'dotnet' '.NET 8 이상 SDK를 설치해야 한다.'
if (-not $SkipClient) {
    Assert-Command 'npm' 'Node.js 20 이상을 설치해야 한다. (-SkipClient로 건너뛸 수 있다)'
}

if (Test-Path $artifacts) {
    Remove-Item $artifacts -Recurse -Force
}
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null

# 클라이언트를 먼저 빌드해야 wwwroot가 배포물에 들어간다.
if (-not $SkipClient) {
    if (-not (Test-Path (Join-Path $clientPath 'node_modules'))) {
        Push-Location $clientPath
        try { Invoke-Step '클라이언트 의존성 설치' { npm install } } finally { Pop-Location }
    }

    Push-Location $clientPath
    try { Invoke-Step '클라이언트 빌드' { npm run build } } finally { Pop-Location }
}

$wwwroot = Join-Path $repositoryRoot 'src\TraceLens.Server\wwwroot\index.html'
if (-not (Test-Path $wwwroot)) {
    throw "웹 빌드 결과를 찾을 수 없다: $wwwroot (-SkipClient 없이 다시 실행한다.)"
}

$versionArgs = @()
if ($Version) { $versionArgs = @("-p:Version=$Version") }

if (-not $SkipZip) {
    Invoke-Step 'Windows x64 portable 배포' {
        dotnet publish $cliProject -c $Configuration -r win-x64 --self-contained false `
            -o $publishDirectory --nologo @versionArgs
    }

    # publish 결과에 MCP 실행에 필요한 파일이 들어갔는지 확인한다. 없으면 배포물이 반쪽이다.
    $required = @('tracelens.exe', 'tracelens.dll', 'tracelens.deps.json', 'tracelens.runtimeconfig.json',
        'TraceLens.Analyzer.dll', 'TraceLens.Context.dll', 'TraceLens.Mcp.dll', 'TraceLens.Server.dll')
    foreach ($name in $required) {
        if (-not (Test-Path (Join-Path $publishDirectory $name))) {
            throw "배포물에 $name 이(가) 없다. 패키지 구성을 확인해야 한다."
        }
    }

    foreach ($directory in @('wwwroot', 'samples')) {
        if (-not (Test-Path (Join-Path $publishDirectory $directory))) {
            throw "배포물에 $directory 폴더가 없다."
        }
    }

    $zipPath = Join-Path $artifacts 'tracelens-win-x64.zip'
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath -Force
    Write-Host "==> portable ZIP: $zipPath" -ForegroundColor Green
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
    "",
    "== 파일 =="
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
$lines += '5. 위 결과를 이 파일 아래에 날짜와 함께 적는다. 확인하지 않은 운영체제는 지원 표에 넣지 않는다.'
$lines | Set-Content -Path $manifestPath -Encoding utf8

Write-Host "==> 완료. 산출물: $artifacts" -ForegroundColor Green
Get-Content $manifestPath | Write-Host
