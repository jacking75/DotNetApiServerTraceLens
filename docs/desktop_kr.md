# Windows 11 데스크톱 앱

TraceLens는 기존 ASP.NET Core 서버와 React 화면을 그대로 사용한다. 데스크톱 EXE는 로컬 서버를 시작하고 같은 화면을 WebView2 창에 연다. 현재 데스크톱 빌드 대상은 **Windows 11 x64**다. 개발 중에는 `run.ps1`과 브라우저/Vite를 계속 쓰고, 배포할 때 데스크톱 EXE를 만들 수 있다. 화면과 분석기를 두 벌로 개발할 필요는 없다.

## 필요한 환경

| 구분 | 필요한 것 |
|---|---|
| EXE를 만드는 PC | Windows 11 x64, .NET 10 SDK, Node.js 20 이상, PowerShell |
| EXE를 실행하는 PC | Windows 11 x64, .NET 10 Desktop Runtime, ASP.NET Core Runtime, WebView2 Runtime |
| 솔루션·프로젝트 분석 | 위 실행 환경에 더해 대상 프로젝트를 빌드할 수 있는 .NET SDK와 필요한 워크로드 |

실행 PC에 TraceLens 소스와 Node.js는 필요 없다. **단일 EXE는 .NET 런타임까지 내장한 자체 포함 배포를 뜻하지 않는다.** WebView2 Runtime도 실행 PC에 있어야 한다.

## 빌드

저장소 루트에서 실행한다.

```powershell
.\pack-desktop.ps1
```

스크립트가 필요한 경우 클라이언트 의존성을 설치하고 React 화면을 빌드한 다음, Windows x64용 데스크톱 프로젝트를 게시한다. 결과는 `artifacts/TraceLens.Desktop-win-x64.exe`다. EXE 한 파일만 다른 폴더나 PC로 옮겨 실행할 수 있다.

클라이언트가 이미 최신으로 빌드되어 있으면 다음 옵션으로 해당 빌드만 건너뛸 수 있다. 소스가 `wwwroot`보다 새로우면 스크립트가 중단된다.

```powershell
.\pack-desktop.ps1 -SkipClient
```

`pack.ps1`은 기존 portable ZIP과 .NET tool용 별도 스크립트다. 이 스크립트는 `artifacts/`를 다시 만들므로 두 배포물을 모두 만들 때는 **`pack.ps1`을 먼저**, `pack-desktop.ps1`을 나중에 실행한다.

## 실행

```powershell
# 앱 창 열기. 화면에서 대상을 고르고 '분석'을 명시적으로 누른다.
.\artifacts\TraceLens.Desktop-win-x64.exe

# 저장해 둔 결과가 있는 대상을 시작할 때 바로 연다. 자동 재분석은 하지 않는다.
.\artifacts\TraceLens.Desktop-win-x64.exe --target 'C:\repos\MyApi\MyApi.sln'
```

기본 포트는 `127.0.0.1:25011`이다. 이미 사용 중이면 사용 가능한 다른 루프백 포트를 고른다. 앱 창을 닫으면 이 EXE가 시작한 서버도 종료된다. 외부 웹 페이지 링크는 기본 브라우저에서 열린다.

EXE에는 화면·설명서·예제가 들어 있다. 첫 실행 때 이 파일을 `%LOCALAPPDATA%\TraceLens\desktop-assets`에 풀고 WebView2 프로필은 `%LOCALAPPDATA%\TraceLens\WebView2`에 둔다. .NET 단일 파일 실행에 필요한 구성 요소도 런타임이 자체 추출할 수 있다. 따라서 **배포 파일은 하나지만 실행 중 생성되는 캐시 파일은 있다.** 분석 결과는 앱 캐시가 아니라 분석 대상 폴더의 `.tracelens/`에 저장된다.

MCP 설정 화면은 현재 실행 중인 데스크톱 EXE를 호스트 명령으로 안내한다. 이 EXE는 MCP 클라이언트가 호출할 `mcp --graph <graph.json>` 명령도 지원한다. `doctor`, `analyze`, `serve` 같은 기존 CLI 명령은 CLI 배포물의 `tracelens.exe`에서 실행한다.

## 개발 흐름

```powershell
.\run.ps1 -Dev    # 서버와 Vite 개발 서버. 화면 수정 중에 사용한다.
.\run.ps1         # 빌드된 화면을 브라우저에서 확인한다.
.\pack-desktop.ps1 # 배포 직전에 단일 EXE를 만든다.
```

데스크톱 창은 기존 HTTP API와 React 화면을 사용한다. 분석 로직·UI를 고칠 때마다 EXE를 다시 만들 필요는 없다. 앱 창 자체, 내장 자산 경로, Windows 배포에 관계된 변경을 확인할 때 게시하면 된다. Linux/macOS용 데스크톱 앱은 현재 만들지 않는다. 기존 브라우저/CLI 배포 방식은 그대로 사용할 수 있다.
