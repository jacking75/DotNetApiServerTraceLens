#Requires -Version 5.1
<#
.SYNOPSIS
    TraceLens 서버와 클라이언트를 한 번에 빌드하고 실행한다.

.DESCRIPTION
    기본 모드는 클라이언트를 프로덕션 빌드해서 서버의 wwwroot에 넣고 서버 하나만 띄운다.
    브라우저에서 http://127.0.0.1:25011 하나로 UI와 API를 모두 쓴다.

    -Dev를 주면 API 서버(25011)와 Vite 개발 서버(25012)를 함께 띄운다.
    클라이언트 코드를 고치면서 HMR로 확인할 때 쓴다.

    배포물을 만들려면 .\pack.ps1 을 쓴다(소스·Node.js 없이 실행할 수 있는 ZIP과 .NET tool 패키지).
    분석만 하거나 MCP만 띄우려면 CLI를 쓴다: dotnet run --project src/TraceLens.Cli -- analyze <대상>

.EXAMPLE
    .\run.ps1

.EXAMPLE
    .\run.ps1 -Dev
#>
[CmdletBinding()]
param(
    # API 서버와 Vite 개발 서버를 함께 띄운다.
    [switch]$Dev,
    # 빌드를 건너뛰고 바로 실행한다(이미 빌드해 둔 경우).
    [switch]$SkipBuild,
    # 브라우저를 자동으로 열지 않는다.
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = $PSScriptRoot
$clientPath = Join-Path $repositoryRoot 'client'
$serverProject = Join-Path $repositoryRoot 'src\TraceLens.Server'
$solutionPath = Join-Path $repositoryRoot 'TraceLens.sln'
$apiUrl = 'http://127.0.0.1:25011'
$devUrl = 'http://localhost:25012'

function Assert-Command {
    param([string]$Name, [string]$Hint)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name 을(를) 찾을 수 없다. $Hint"
    }
}

function Invoke-Step {
    param([string]$Title, [scriptblock]$Action)

    Write-Host "==> $Title" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Title 이(가) 실패했다 (종료 코드 $LASTEXITCODE)."
    }
}

function Start-BrowserWhenReady {
    param([string]$Url)

    if ($NoBrowser) { return }

    Start-Job -ArgumentList $Url -ScriptBlock {
        param([string]$Url)

        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            try {
                Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2 | Out-Null
                Start-Process $Url
                return
            }
            catch {
                Start-Sleep -Milliseconds 500
            }
        }
    } | Out-Null
}

Assert-Command 'dotnet' '.NET 8 이상 SDK를 설치해야 한다.'
# -SkipBuild면 클라이언트를 빌드하지 않으므로 npm이 없어도 된다(배포물만 쓰는 사용자를 막지 않는다).
if (-not $SkipBuild) {
    Assert-Command 'npm' 'Node.js 20 이상을 설치해야 한다. 빌드 없이 실행하려면 -SkipBuild를 준다.'
}

if (-not $SkipBuild) {
    if (-not (Test-Path (Join-Path $clientPath 'node_modules'))) {
        Push-Location $clientPath
        try { Invoke-Step '클라이언트 의존성 설치' { npm install } } finally { Pop-Location }
    }

    Invoke-Step '서버 빌드' { dotnet build $solutionPath --nologo -v q }

    if (-not $Dev) {
        # vite 빌드 결과가 src/TraceLens.Server/wwwroot로 들어가서 서버가 그대로 호스팅한다.
        Push-Location $clientPath
        try { Invoke-Step '클라이언트 빌드' { npm run build } } finally { Pop-Location }
    }
}

if (-not $Dev) {
    Write-Host "==> TraceLens 실행: $apiUrl (Ctrl+C로 종료)" -ForegroundColor Green
    Start-BrowserWhenReady $apiUrl
    dotnet run --project $serverProject --no-launch-profile
    exit $LASTEXITCODE
}

$serverAssembly = Join-Path $serverProject 'bin\Debug\net8.0\TraceLens.Server.dll'
if (-not (Test-Path $serverAssembly)) {
    throw "서버 빌드 결과를 찾을 수 없다: $serverAssembly (-SkipBuild 없이 다시 실행한다.)"
}

# 개발 모드에서는 API 서버를 백그라운드로 띄우고, 포그라운드의 Vite 개발 서버가 /api 요청을 프록시한다.
$env:ASPNETCORE_ENVIRONMENT = 'Development'
Write-Host "==> API 서버 실행: $apiUrl" -ForegroundColor Green
$serverProcess = Start-Process 'dotnet' -ArgumentList $serverAssembly -PassThru

try {
    Write-Host "==> Vite 개발 서버 실행: $devUrl (Ctrl+C로 둘 다 종료)" -ForegroundColor Green
    Start-BrowserWhenReady $devUrl
    Push-Location $clientPath
    try { npm run dev } finally { Pop-Location }
}
finally {
    if ($serverProcess -and -not $serverProcess.HasExited) {
        Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
        Write-Host '==> API 서버를 종료했다.' -ForegroundColor DarkGray
    }
}
