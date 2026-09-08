*[English](mcp-setup.md) · 한국어*

# TraceLens MCP 설정

TraceLens MCP는 분석 대상의 `.tracelens/graph.json`을 읽는 **로컬 stdio** 서버다.
네트워크 포트를 열지 않고, **이미 저장된 분석 결과만 읽으며 재분석하지 않는다.**
표준 MCP stdio 서버라 Claude Code·Codex CLI·VS Code처럼 MCP를 지원하는 도구라면 등록 방식만 다를 뿐 똑같이 쓴다.

## 가장 쉬운 방법: 앱이 만들어 주는 설정 조각

TraceLens UI에서 분석한 뒤 질문 패널의 **"MCP 연동 설정 보기"**를 누르면
지금 대상과 설치된 호스트에 맞춰 경로가 채워진 설정 조각이 나온다.
**기존 설정을 덮어쓰지 않는다** — 나온 내용을 해당 파일에 더하면 된다.

같은 내용을 API로도 받을 수 있다.

```powershell
curl http://127.0.0.1:25011/api/mcp-setup
```

## 준비

1. 배포물을 쓰면 `tracelens.exe`가 그대로 MCP 호스트다. 소스에서 쓰면 먼저 빌드한다.

   ```powershell
   dotnet build TraceLens.sln
   ```

2. 분석 대상을 한 번 이상 분석해 `.tracelens/graph.json`을 만든다.

   ```powershell
   tracelens analyze C:\repos\MyApp\MyApp.sln
   # 또는 UI에서 분석, 또는 POST /api/analyze
   ```

## 실행 명령

배포물(권장):

```text
command: C:\tools\tracelens\tracelens.exe
args:    ["mcp", "--graph", "C:\\repos\\MyApp\\.tracelens\\graph.json"]
```

소스에서 실행할 때:

```text
command: dotnet
args:    ["C:\\repos\\tracelens\\src\\TraceLens.Mcp\\bin\\Debug\\net8.0\\TraceLens.Mcp.dll",
          "--graph", "C:\\repos\\MyApp\\.tracelens\\graph.json"]
```

`--graph` 대신 `TRACELENS_GRAPH_PATH` 환경 변수로도 지정할 수 있다.

## Claude Code

```powershell
claude mcp add --scope local tracelens -- C:\tools\tracelens\tracelens.exe mcp --graph "C:\repos\MyApp\.tracelens\graph.json"
claude mcp get tracelens
```

파일로 관리하려면 분석 대상 루트에 `.mcp.json`을 둔다. 이미 파일이 있으면 `mcpServers` 아래에
`tracelens` 항목만 더한다.

```json
{
  "mcpServers": {
    "tracelens": {
      "type": "stdio",
      "command": "C:\\tools\\tracelens\\tracelens.exe",
      "args": ["mcp", "--graph", "C:\\repos\\MyApp\\.tracelens\\graph.json"]
    }
  }
}
```

TraceLens UI의 **"Claude Code로 실행"**은 별도 등록 없이 동작한다.
`POST /api/ask`가 현재 분석에 맞는 `.tracelens/mcp.json`을 만들어 Claude Code CLI에 `--mcp-config`로 넘긴다.
이 모드만 Claude Code 전용이고, 아래 등록들과는 서로 무관하다.

## Codex CLI

```powershell
codex mcp add tracelens -- C:\tools\tracelens\tracelens.exe mcp --graph "C:\repos\MyApp\.tracelens\graph.json"
codex mcp list
```

`~/.codex/config.toml`(또는 신뢰된 프로젝트의 `.codex/config.toml`)에 직접 적을 수도 있다.

```toml
[mcp_servers.tracelens]
command = "C:\\tools\\tracelens\\tracelens.exe"
args = ["mcp", "--graph", "C:\\repos\\MyApp\\.tracelens\\graph.json"]
```

## VS Code

VS Code는 로컬 MCP 서버와 그 도구를 지원한다. 작업 폴더의 `.vscode/mcp.json`에 적는다.
이미 파일이 있으면 `servers` 아래에 `tracelens` 항목만 더한다.

```json
{
  "servers": {
    "tracelens": {
      "type": "stdio",
      "command": "C:\\tools\\tracelens\\tracelens.exe",
      "args": ["mcp", "--graph", "C:\\repos\\MyApp\\.tracelens\\graph.json"]
    }
  }
}
```

## 제공 도구

### 먼저 부를 것

| 도구 | 입력 | 반환 |
|---|---|---|
| `get_analysis_status` | `expectedSnapshotId?` | 분석 대상·시각·신선도·진단·지원 기능. 다른 도구를 부르기 전에 결과를 얼마나 믿을 수 있는지 알려 준다. |

### 작업 문맥

| 도구 | 입력 | 반환 |
|---|---|---|
| `get_context` | `selectionKind`, `selectionId`, `intent?`, `budget?`, `question?`, `expectedSnapshotId?` | UI 미리보기와 같은 작업 문맥. 시그니처·검증된 소스·진입점까지의 경로·근거·한계가 들어 있다. |
| `get_entrypoint_flow` | `endpointId`, `depth?`, `maxNodes?` | 진입점 하나의 제한된 호출 경로와 확정하지 못한 경계. |
| `get_findings` | `nodeId?`, `ruleId?`, `limit?`, `cursor?` | DB·미들웨어 점검과 확실도, **꺼진 규칙 목록**. |
| `get_impact` | `nodeIds`, `maxDepth?`, `maxNodes?` | 이 메서드를 바꾸면 영향받을 수 있는 진입점 후보와 근거 경로. |
| `compare_snapshots` | `baselineId`, `currentId?` | 보관한 기준과의 구조·본문·점검 차이와 비교 조건 일치 여부. |

`selectionKind`는 `node`, `endpoint`, `class`, `namespace`, `finding`, `writePath`, `middlewareStep`,
`startupStep` 중 하나다. `intent`는 `explain`·`planChange`·`reviewChange`·`investigateData`·`share`,
`budget`은 `short`·`balanced`·`detailed`다.

### 낱개 조회

| 도구 | 입력 | 반환 |
|---|---|---|
| `get_node` | `nodeId`, `expectedSnapshotId?` | 노드 메타데이터와 그 메서드의 소스. |
| `get_source` | `file`, `startLine`, `endLine` | 분석 루트 안 소스와 `sourceVersion`(snapshot/current). |
| `get_callers` · `get_callees` | `nodeId`, `limit?`, `cursor?` | 인접 노드와 **호출 근거**, 확정하지 못한 경계 목록. |
| `get_entrypoints` | `query?`, `limit?`, `cursor?` | HTTP 진입점 목록. 같은 메서드의 여러 라우트가 각각 나온다. |
| `search_symbol` | `query`, `limit?`, `cursor?` | 표시 이름·전체 ID·네임스페이스로 노드 검색. |

## 알아 둘 것

- **재분석하지 않는다.** 코드가 바뀌었으면 사용자가 `tracelens analyze`나 UI의 "다시 분석"을 실행해야 한다. `get_analysis_status`의 `freshness`가 `stale`이면 그 사실을 답변에 적는 편이 좋다.
- **스냅샷이 바뀌면 알려 준다.** 응답의 `snapshotId`를 후속 호출에 `expectedSnapshotId`로 넘기면, 그 사이 분석이 바뀌었을 때 `SNAPSHOT_MISMATCH`로 알린다.
- **소스 접근은 제한된다.** `file`은 분석 루트 기준 상대 경로만 받고, 이 스냅샷이 실제로 분석한 파일만 읽는다. 심볼릭 링크·정션의 실제 대상까지 확인하며, 응답 크기에 상한이 있다.
- **빈 목록이 "없음"은 아니다.** `get_callers`/`get_callees`의 `boundaries`에 확정하지 못한 호출이 들어 있다. 이 목록을 함께 보고 판단해야 한다.
- **큰 응답은 나눠 받는다.** 목록 도구는 `limit`/`cursor`를 지원한다. 응답의 `nextCursor`가 있으면 이어서 받으면 된다.

## 확인해 보기

등록만 하고 끝내지 말고 실제 질문으로 확인한다.

```text
tracelens의 get_analysis_status로 지금 분석 상태를 확인하고,
POST /api/orders 진입점에 대해 get_context를 불러 무엇을 저장하는지 근거와 함께 설명해라.
```

도구 목록이 보이는 것만으로 연동이 됐다고 보지 않는다. 위 질문이 실제 파일·줄을 인용해야 성공이다.
