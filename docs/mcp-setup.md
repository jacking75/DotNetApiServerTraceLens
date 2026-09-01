# TraceLens MCP 설정

TraceLens.Mcp는 분석 대상 솔루션의 `.tracelens/graph.json`을 읽는 로컬 stdio MCP 서버다. 서버는 네트워크 포트를 열지 않으며 도구를 호출할 때마다 그래프 파일을 다시 읽는다. 표준 MCP stdio 서버라 Claude Code와 Codex CLI 등 MCP를 지원하는 CLI라면 등록 방식만 다를 뿐 똑같이 쓸 수 있다. 아래에 두 CLI 각각의 등록 방법을 적는다.

## 준비

저장소 루트에서 전체 솔루션을 먼저 빌드한다.

```powershell
dotnet build TraceLens.sln
```

분석 대상 솔루션은 TraceLens UI 또는 `POST /api/analyze`로 한 번 이상 분석해 `.tracelens/graph.json`을 생성해야 한다.

## Claude Code에 등록

아래 경로를 실제 저장소와 분석 대상 경로로 바꿔 실행한다.

```powershell
claude mcp add --scope local tracelens -- dotnet "C:\repos\tracelens\src\TraceLens.Mcp\bin\Debug\net8.0\TraceLens.Mcp.dll" --graph "C:\repos\MyApp\.tracelens\graph.json"
```

등록 후 Claude Code에서 `/mcp`를 실행하거나 다음 명령으로 상태를 확인할 수 있다.

```powershell
claude mcp get tracelens
```

### `.mcp.json` 예시

프로젝트 범위 설정을 파일로 관리하려면 분석 대상 솔루션 루트에 다음 내용을 둔다.

```json
{
  "mcpServers": {
    "tracelens": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "C:\\repos\\tracelens\\src\\TraceLens.Mcp\\bin\\Debug\\net8.0\\TraceLens.Mcp.dll",
        "--graph",
        "C:\\repos\\MyApp\\.tracelens\\graph.json"
      ]
    }
  }
}
```

TraceLens UI의 `MCP 직접 연동` 모드는 별도 등록 없이 동작한다. `POST /api/ask`가 현재 분석 결과에 맞는 `.tracelens/mcp.json`을 만들고 **Claude Code CLI**에 `--mcp-config`로 전달한다 — 이 모드는 Claude Code 전용이며, 아래 Codex CLI 등록과는 무관하다.

## Codex CLI에 등록

Codex CLI도 같은 방식으로 등록한다. 경로는 실제 저장소와 분석 대상 경로로 바꾼다.

```powershell
codex mcp add tracelens -- dotnet "C:\repos\tracelens\src\TraceLens.Mcp\bin\Debug\net8.0\TraceLens.Mcp.dll" --graph "C:\repos\MyApp\.tracelens\graph.json"
```

등록 상태는 `codex mcp list`로 확인하거나, `codex` 세션 안에서 `/mcp`를 실행해 확인한다.

### `config.toml` 예시

Codex CLI는 `~/.codex/config.toml`(또는 신뢰된 프로젝트의 `.codex/config.toml`)에 서버를 TOML로 적는다. `codex mcp add`가 이 파일에 자동으로 같은 내용을 써 준다.

```toml
[mcp_servers.tracelens]
command = "dotnet"
args = [
  "C:\\repos\\tracelens\\src\\TraceLens.Mcp\\bin\\Debug\\net8.0\\TraceLens.Mcp.dll",
  "--graph",
  "C:\\repos\\MyApp\\.tracelens\\graph.json",
]
```

TraceLens UI의 `MCP 직접 연동` 모드는 Codex CLI를 호출하지 않는다 — Codex CLI에서 TraceLens 도구를 쓰려면 위 등록을 마친 뒤 `codex` 세션에서 직접 질문한다.

## 제공 도구

- `get_node(nodeId)`: 노드 메타데이터와 해당 소스를 반환한다.
- `get_source(file, startLine, endLine)`: 솔루션 내부 소스의 지정 라인을 반환한다.
- `get_callers(nodeId)`, `get_callees(nodeId)`: 직접 연결된 호출자와 피호출자를 반환한다.
- `get_entrypoints()`: HTTP 진입점 목록을 반환한다.
- `search_symbol(query)`: 표시 이름, 전체 ID, 네임스페이스로 노드를 검색한다.

`file`은 분석 대상 솔루션 루트 기준 상대 경로만 허용한다. 솔루션 외부 경로는 거부한다.
