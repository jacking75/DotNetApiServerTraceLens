#Requires -Version 5.1
<#
.SYNOPSIS
    Windows 11 x64용 TraceLens 단일 실행 파일을 만든다.
.DESCRIPTION
    React 화면을 먼저 빌드하고, WebView2 데스크톱 셸을 framework-dependent single-file로 게시한다.
    사용처에는 .NET 10 Desktop Runtime과 ASP.NET Core Runtime, WebView2 Runtime이 있어야 한다.
    프로젝트를 분석하려면 해당 프로젝트용 .NET SDK도 필요하다.
    완성 EXE 하나를 artifacts/TraceLens.Desktop-win-x64.exe에 둔다.
#>
[CmdletBinding()]
param([switch]$SkipClient)

$ErrorActionPreference = 'Stop'
$repositoryRoot = $PSScriptRoot
$clientPath = Join-Path $repositoryRoot 'client'
$projectPath = Join-Path $repositoryRoot 'src\TraceLens.Desktop\TraceLens.Desktop.csproj'
$publishPath = Join-Path $repositoryRoot 'artifacts\desktop-publish\win-x64'
$outputPath = Join-Path $repositoryRoot 'artifacts\TraceLens.Desktop-win-x64.exe'

if (-not $SkipClient) {
    Push-Location $clientPath
    try {
        if (-not (Test-Path 'node_modules')) {
            & npm.cmd install
            if ($LASTEXITCODE -ne 0) { throw '클라이언트 의존성 설치가 실패했다.' }
        }
        & npm.cmd run build
        if ($LASTEXITCODE -ne 0) { throw '클라이언트 빌드가 실패했다.' }
    }
    finally { Pop-Location }
}

$wwwroot = Join-Path $repositoryRoot 'src\TraceLens.Server\wwwroot\index.html'
if (-not (Test-Path $wwwroot)) {
    throw 'wwwroot/index.html이 없다. -SkipClient 없이 다시 실행해야 한다.'
}

if ($SkipClient) {
    $builtAt = (Get-Item $wwwroot).LastWriteTimeUtc
    $clientInputs = @(Get-ChildItem (Join-Path $clientPath 'src') -Recurse -File -ErrorAction SilentlyContinue)
    $clientInputs += @(@('package.json', 'vite.config.ts', 'index.html') |
        ForEach-Object { Join-Path $clientPath $_ } | Where-Object { Test-Path $_ } | ForEach-Object { Get-Item $_ })
    $newest = $clientInputs | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($newest -and $newest.LastWriteTimeUtc -gt $builtAt) {
        throw 'wwwroot가 클라이언트 소스보다 오래됐다. -SkipClient 없이 다시 실행해야 한다.'
    }
}

& dotnet publish $projectPath -c Release -r win-x64 `
    -p:SelfContained=false -p:PublishSelfContained=false `
    -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:CopyOutputSymbolsToPublishDirectory=false -o $publishPath --nologo
if ($LASTEXITCODE -ne 0) { throw 'Windows 데스크톱 게시가 실패했다.' }

$publishedExecutable = Join-Path $publishPath 'TraceLens.Desktop.exe'
if (-not (Test-Path $publishedExecutable)) { throw '게시된 EXE를 찾지 못했다.' }
Copy-Item -LiteralPath $publishedExecutable -Destination $outputPath -Force
$hash = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "완성: $outputPath"
Write-Host "SHA256: $hash"
