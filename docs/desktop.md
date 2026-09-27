# Windows 11 desktop app

TraceLens keeps its ASP.NET Core server and React UI. The desktop EXE starts a loopback server and opens the same UI in a WebView2 window. The desktop target is currently **Windows 11 x64**. You can keep using `run.ps1`, a browser, and Vite while developing, then package the desktop app for distribution. There is one UI and one analyzer to maintain.

## Requirements

| Machine | Requirements |
|---|---|
| Build machine | Windows 11 x64, .NET 10 SDK, Node.js 20 or later, PowerShell |
| Run machine | Windows 11 x64, .NET 10 Desktop Runtime, ASP.NET Core Runtime, WebView2 Runtime |
| Project analysis | The above, plus a .NET SDK and workloads capable of building the target project |

The run machine does not need the TraceLens source or Node.js. **The one-file package is framework-dependent:** it does not bundle the .NET or WebView2 runtimes.

## Build

From the repository root:

```powershell
.\pack-desktop.ps1
```

The script installs client dependencies if needed, builds the React UI, publishes for Windows x64, and writes `artifacts/TraceLens.Desktop-win-x64.exe`. You can move that EXE alone to another folder or machine.

If the client has already been built, `-SkipClient` skips that build. The script rejects it when client sources are newer than `wwwroot`.

```powershell
.\pack-desktop.ps1 -SkipClient
```

The existing `pack.ps1` builds the portable ZIP and .NET tool. It recreates `artifacts/`, so run **`pack.ps1` first** and `pack-desktop.ps1` second if you need both outputs.

## Run

```powershell
# Open the window, select a project, then explicitly start analysis in the UI.
.\artifacts\TraceLens.Desktop-win-x64.exe

# Load an existing analysis for this target at startup. This does not re-analyze.
.\artifacts\TraceLens.Desktop-win-x64.exe --target 'C:\repos\MyApi\MyApi.sln'
```

The app uses `127.0.0.1:25011` by default and picks another loopback port when it is occupied. Closing the window stops the server started by that EXE. External links open in the default browser.

The EXE bundles the UI, manuals, and samples. It extracts them under `%LOCALAPPDATA%\TraceLens\desktop-assets` and stores its WebView2 profile under `%LOCALAPPDATA%\TraceLens\WebView2`. The .NET single-file host may also extract runtime components. Thus **one distribution file does not mean zero cache files**. Analysis results still go under `.tracelens/` beside the target project.

The MCP setup screen points to the running desktop EXE as the host. The EXE also accepts `mcp --graph <graph.json>` for MCP clients. Use the CLI package's `tracelens.exe` for existing commands such as `doctor`, `analyze`, and `serve`.

## Development workflow

```powershell
.\run.ps1 -Dev    # API server and Vite for UI work.
.\run.ps1         # Built UI in a browser.
.\pack-desktop.ps1 # One-file desktop package for distribution.
```

UI and analyzer work does not require rebuilding the desktop EXE on every edit. Publish when checking the desktop shell, bundled assets, or Windows distribution. There is currently no Linux/macOS desktop build; the existing browser and CLI distributions remain available.
