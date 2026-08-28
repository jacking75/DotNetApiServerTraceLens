# task.md — 구현 상세 및 단계별 작업 목록

이 문서는 TraceLens의 구현 상세를 담는다. 데이터 계약(스키마)을 먼저 정의하고, 그 뒤에 단계별(1~5단계) 작업 목록과 완료 기준을 정리한다. 다섯 가지 설계 제약은 CLAUDE.md에 있고, 여기서는 그 제약을 실제로 어떻게 구현하는지를 다룬다.

---

## 0. 데이터 계약

### 0.1 그래프 노드 ID 규칙

`{fullyQualifiedTypeName}.{methodName}({parameterTypes})` 형태의 문자열을 그대로 노드 ID로 쓴다. Roslyn의 `IMethodSymbol.ToDisplayString(SymbolDisplayFormat)`으로 안정적으로 생성할 수 있다. 예: `MyApp.Services.OrderService.CreateOrderAsync(MyApp.Contracts.CreateOrderRequest)`.

### 0.2 GraphDocument JSON 스키마

`.tracelens/graph.json`에 저장되는 형식이다.

```json
{
  "schemaVersion": 2,
  "solutionPath": "C:\\repos\\MyApp\\MyApp.sln",
  "analyzedAt": "2026-08-08T14:20:00+09:00",
  "entryPoints": [
    {
      "nodeId": "MyApp.Controllers.OrdersController.CreateOrder(MyApp.Contracts.CreateOrderRequest)",
      "httpMethod": "POST",
      "route": "/api/orders"
    }
  ],
  "classes": [
    {
      "id": "MyApp.Controllers.OrdersController",
      "name": "OrdersController",
      "namespace": "MyApp.Controllers",
      "kind": "Controller",
      "file": "Controllers/OrdersController.cs",
      "startLine": 18,
      "endLine": 60,
      "methodIds": [
        "MyApp.Controllers.OrdersController.CreateOrder(MyApp.Contracts.CreateOrderRequest)"
      ]
    }
  ],
  "nodes": [
    {
      "id": "MyApp.Controllers.OrdersController.CreateOrder(MyApp.Contracts.CreateOrderRequest)",
      "kind": "Controller",
      "displayName": "OrdersController.CreateOrder",
      "namespace": "MyApp.Controllers",
      "classId": "MyApp.Controllers.OrdersController",
      "file": "Controllers/OrdersController.cs",
      "startLine": 24,
      "endLine": 41
    }
  ],
  "edges": [
    {
      "from": "MyApp.Controllers.OrdersController.CreateOrder(MyApp.Contracts.CreateOrderRequest)",
      "to": "MyApp.Services.OrderService.CreateOrderAsync(MyApp.Contracts.CreateOrderRequest)"
    }
  ],
  "fileHashes": {
    "Controllers/OrdersController.cs": "sha256:9f1c...",
    "Services/OrderService.cs": "sha256:2ab0..."
  }
}
```

`kind`는 `Controller | Service | Repository | Validator | Infra | Db | Method`(분류 규칙에 안 걸리는 기본값) 중 하나다. 분류 규칙은 0.4의 `tracelens.config.json`을 따르고, 클래스와 그 클래스의 메서드 노드는 같은 `kind`를 갖는다.

`classes`는 솔루션 안에 선언된 **모든** 클래스/레코드/구조체다(인터페이스는 제외, `obj`·`bin` 아래 자동 생성 소스도 제외). 진입점에서 도달하지 못하는 클래스도 포함하므로 `classes`의 `methodIds`에 있는 메서드가 `nodes`에 없을 수 있다(`analysisScope`가 `entryPoints`일 때). `classId`는 노드가 속한 클래스의 `id`와 같은 문자열이고, 클래스 ID는 제네릭 인자를 포함한 전체 타입 이름이다(예: `MyApp.Common.Cache<TKey,TValue>`).

### 0.3 SnapshotHash 계산

`fileHashes`는 그래프에 등장하는 모든 노드가 속한 파일과 `classes`에 등장하는 파일 각각에 대해 SHA-256을 계산해서 저장한다. 신선도 비교 시 같은 파일 목록에 대해 다시 해시를 계산해서 하나라도 다르면 `stale`로 판정한다. 그래프에 등장하지 않는 파일의 변경은 신선도 판정에 영향을 주지 않는다(예: 테스트 코드만 수정한 경우).

### 0.4 tracelens.config.json (분석 대상 솔루션에 선택적으로 둘 수 있는 설정)

분석 대상 솔루션 루트에 이 파일이 있으면 읽어서 분류/탐색 규칙을 덮어쓴다. 없으면 아래 기본값을 그대로 쓴다.

```json
{
  "entryPointAttributes": ["HttpGet", "HttpPost", "HttpPut", "HttpDelete", "HttpPatch"],
  "kindRules": [
    { "kind": "Controller", "matchBaseType": "ControllerBase" },
    { "kind": "Repository", "matchNameSuffix": "Repository" },
    { "kind": "Validator", "matchNameSuffix": "Validator" },
    { "kind": "Infra", "matchNameSuffix": ["Publisher", "Client", "Bus"] },
    { "kind": "Service", "matchNameSuffix": "Service" },
    { "kind": "Db", "matchBaseType": "DbContext" }
  ],
  "maxCallDepth": 8,
  "excludeNamespacePrefixes": ["System", "Microsoft", "Swashbuckle"],
  "analysisScope": "solution"
}
```

규칙은 배열 순서대로 평가하고 처음 매치되는 규칙을 적용한다. 어느 규칙에도 안 걸리면 `kind: "Method"`로 분류한다.

`analysisScope`는 호출 그래프를 어디까지 만들지 결정한다.

| 값 | 노드/엣지 범위 | `maxCallDepth` | 비고 |
|---|---|---|---|
| `solution`(기본) | 솔루션 안의 모든 메서드와 그 호출 관계 | 사용하지 않음 | 클래스 메뉴에서 아무 클래스나 골라도 호출 관계가 보인다. 분석 시간과 `graph.json` 크기가 커진다. |
| `entryPoints` | 진입점에서 `maxCallDepth`까지 도달 가능한 범위만 | 적용 | 분석이 가볍다. 도달 못 하는 클래스는 메뉴에는 뜨지만 그래프가 비어 있다. |

`classes` 목록은 두 값 모두에서 솔루션 전체를 담는다.

### 0.5 API 계약 (TraceLens.Server)

| 메서드 | 경로 | 설명 |
|---|---|---|
| POST | `/api/analyze` | body `{ "solutionPath": string }`. 분석을 동기 실행하고 결과 요약(`{ entryPointCount, classCount, nodeCount, edgeCount, elapsedMs, analyzedAt, previousAnalyzedAt }`)을 반환. 첫 분석의 `previousAnalyzedAt`은 `null`이다. 서버는 마지막으로 분석한 `solutionPath`를 프로세스 메모리에 상태로 유지한다(로컬 단일 사용자 도구이므로 세션 관리 불필요). |
| GET | `/api/graph/entrypoints` | 현재 로드된 그래프의 진입점 목록(`entryPoints`)을 반환. 초기 화면에서 "어떤 엔드포인트부터 볼지" 선택에 사용. |
| GET | `/api/graph/classes` | 클래스 메뉴용 요약 목록(`{ id, name, namespace, kind, file, startLine, endLine, methodCount }[]`). 네임스페이스 → 이름 순으로 정렬되어 있다. |
| GET | `/api/graph` | 현재 로드된 `GraphDocument` 전체(`classes`, `nodes`, `edges`)를 반환. 레벨 단위로 어떻게 보여줄지는 프론트엔드가 결정한다(0.6 참고). |
| GET | `/api/source?file=&start=&end=` | 지정한 파일의 `start`~`end` 라인을 반환. `file`이 `solutionPath` 하위 경로가 아니면 400을 반환한다(경로 탈출 방지). |
| GET | `/api/freshness` | `{ status: "fresh" \| "stale", analyzedAt, changedFiles: string[] }`. `fileHashes`를 다시 계산해서 비교한 결과. |
| POST | `/api/ask` | (3단계) body `{ nodeId, question, mode }`. `mode`가 `"mcp"`일 때만 서버가 실제 작업을 수행하고 SSE로 응답을 스트리밍한다. `"clipboard"`는 프론트엔드에서만 처리하고 이 API를 호출하지 않는다. `"editor"`는 프론트엔드가 `vscode://file/...` URI를 직접 열거나, 서버가 로컬 프로세스로 에디터를 실행한다. |

### 0.6 포커스 기반 윈도잉 (그래프 규모 제약의 구현 방식)

서버는 진입점 기준으로 `maxCallDepth`까지 도달 가능한 노드/엣지를 미리 계산해서 `graph.json`에 담아둔다(전체 코드베이스를 한꺼번에 그래프화하지 않고, 진입점에서 도달 가능한 범위로만 한정). 프론트엔드는 이 데이터를 한 번에 받아오지만, 화면에는 항상 다음 규칙으로 일부만 렌더링한다.

- 처음 진입점을 선택하면 그 진입점에서 시작하는 **직선 호출 체인**(분기 없이 이어지는 다음 노드들)만 "Level 1"로 보여준다.
- 특정 노드에서 여러 갈래로 호출이 갈라지는 지점(예: 서비스 메서드 내부에서 검증기·리포지토리·이벤트 발행 등 여러 곳을 호출)은 드릴다운 대상으로 표시하고, 클릭하기 전까지는 펼치지 않는다.
- 드릴다운하면 그 노드로부터 1~2홉 이내의 하위 호출만 "Level 2"로 새로 렌더링하고, breadcrumb으로 이전 레벨로 돌아갈 수 있게 한다.

클래스 메뉴에서 클래스를 고르면 **클래스 중심 창**을 쓴다(`getClassWindow`). 그 클래스에 속한 메서드 노드 전부와, 각 메서드의 직접 호출자/피호출자 1홉까지만 보여주고, 선택한 클래스의 메서드는 하나의 박스(Cytoscape compound node)로 묶는다. 이 창의 포커스 ID는 노드 ID와 겹치지 않는 클래스 ID이고, 박스 안의 분기 노드를 클릭하면 기존 드릴다운 창으로 이어진다.

이 로직은 순수하게 클라이언트 상태(`client/src/graph/windowing.ts`)로 구현하고, 서버는 이미 계산된 전체 그래프를 넘겨주기만 한다. 코드베이스가 커서 그래프가 지나치게 크면(수천 노드) `analysisScope`를 `entryPoints`로 낮추고, 그래도 부족하면 서버 쪽 지연 계산(노드별 온디맨드 확장 API)으로 전환한다 — 5단계 백로그 항목으로 남겨둔다.

---

## 1단계 — 큰 틀 세우기

목표: 진입점을 고르면 그래프가 보이고, 노드를 클릭하면 실제 코드가 뜨고, 클립보드로 위치를 복사할 수 있는 상태. 이 상태부터 팀 내에서 실제로 켜놓고 코드 설명에 쓸 수 있어야 한다.

### 1.1 Analyzer

- [x] `SolutionLoader`: `MSBuildWorkspace`로 `.sln` 로드, 컴파일 성공 여부 확인, 실패한 프로젝트는 경고만 남기고 계속 진행
- [x] `EntryPointScanner`: `tracelens.config.json`의 `entryPointAttributes`에 해당하는 액션 메서드를 전체 프로젝트에서 스캔, `httpMethod`/`route` 추출(클래스 레벨 `[Route]` + 메서드 레벨 라우트 결합)
- [x] `DiResolver`: `Program.cs`(또는 `Startup.cs`)의 `AddScoped<TInterface, TImpl>` 등록 구문을 파싱해서 인터페이스 → 구현체 매핑 테이블 생성. 매핑을 못 찾으면 해당 호출은 그래프에서 제외하고 로그만 남김(예외로 죽지 않게)
- [x] `CallGraphWalker`: 진입점부터 BFS로 호출 그래프 탐색. 각 메서드 본문의 `InvocationExpressionSyntax`를 세만틱 모델로 심볼 해석 → 인터페이스면 `DiResolver`로 구현체 해석 → 해석된 메서드가 분석 대상 솔루션 내부 코드면 재귀 진행, 아니면(BCL/외부 패키지) 그래프에 포함하지 않고 무시. `maxCallDepth` 도달 시 중단
- [x] `NodeClassifier`: `tracelens.config.json`의 `kindRules`로 각 노드의 `kind` 결정
- [x] `SnapshotHasher`: 그래프에 등장한 파일들의 SHA-256 계산
- [x] `GraphDocument` 직렬화 (`System.Text.Json`), `.tracelens/graph.json`으로 저장
- [x] 단위 테스트: `tests/TraceLens.Analyzer.Tests/Fixtures`에 컨트롤러 1개 + 서비스 1개 + 리포지토리 1개짜리 미니 솔루션을 만들어두고, 분석 결과 노드 수/엣지 수/kind 분류가 기대값과 일치하는지 검증

### 1.2 Server

- [x] `Program.cs`: Minimal API 부트스트랩, `.tracelens` 폴더 관리, 마지막 분석 결과를 메모리에 캐시하는 `IGraphStore` 싱글톤
- [x] `POST /api/analyze`, `GET /api/graph/entrypoints`, `GET /api/graph`, `GET /api/source` 구현 (0.5 계약대로)
- [x] `/api/source`의 경로 검증 로직: `Path.GetFullPath`로 정규화 후 `solutionPath` 하위인지 확인, 아니면 400
- [x] 개발 중에는 Vite dev server(`localhost:5173`)에서 오는 요청을 CORS로 허용(로컬 전용이므로 `localhost`만 허용 목록에 추가)

### 1.3 Client

- [x] `App.tsx`: 솔루션 경로 입력 → `/api/analyze` 호출 → 진입점 목록 표시 → 진입점 선택 시 그래프 로드
- [x] `GraphView.tsx`: Cytoscape.js로 노드/엣지 렌더링, `breadthfirst` 또는 `dagre` 레이아웃으로 위→아래 흐름 표현, 0.6의 윈도잉 로직 적용, 노드 클릭 이벤트로 선택 상태 갱신, 분기 지점 노드에는 드릴다운 아이콘 표시
- [x] `client/src/graph/windowing.ts`: 포커스 노드 + 레벨에 따라 보여줄 노드/엣지 서브셋을 계산하는 순수 함수, 단위 테스트 포함
- [x] `CodePanel.tsx`: 선택된 노드의 `file`/`startLine`/`endLine`으로 `/api/source` 호출, Monaco Editor(읽기 전용)에 표시하고 대상 라인 강조
- [x] `AskPanel.tsx`: 선택된 노드의 파일/라인/심볼을 표시, 질문 입력창, "클립보드 복사" 모드만 1단계에서 동작하도록 구현(포맷은 목업의 `파일/라인/심볼/질문` 텍스트 블록과 동일하게 유지)
- [x] breadcrumb 컴포넌트: 현재 레벨/포커스 경로 표시, 클릭으로 이전 레벨 복귀

### 1.4 완료 기준

- [x] 컨트롤러 3~4개 정도의 실제(또는 샘플) ASP.NET Core 솔루션 경로를 입력하면 분석이 끝나고 진입점 목록이 뜬다
- [x] 진입점을 고르면 Level 1 그래프가 그려지고, 분기 지점에 드릴다운 아이콘이 보인다
- [x] 노드를 클릭하면 오른쪽에 실제 소스 코드가 해당 라인 강조와 함께 뜬다
- [x] 드릴다운 클릭 시 하위 흐름으로 이동하고 breadcrumb으로 되돌아갈 수 있다
- [x] "클립보드 복사"를 누르면 파일/라인/심볼/질문이 포함된 텍스트가 실제로 클립보드에 담긴다

---

## 2단계 — 신뢰성 채우기

목표: "지금 보고 있는 그래프가 실제 코드와 같다"를 항상 확신할 수 있는 상태.

### 2.1 Server

- [x] `GET /api/freshness` 구현: 저장된 `fileHashes`의 각 파일을 다시 읽어 해시 재계산, 하나라도 다르면 `stale` + 변경된 파일 목록 반환
- [x] `POST /api/analyze` 재호출(=재분석) 시 기존 `graph.json`을 덮어쓰기 전에 이전 `analyzedAt`을 응답에 함께 포함(프론트에서 "이전: hh:mm → 이후: hh:mm" 같은 표시에 사용 가능)

### 2.2 Client

- [x] `FreshnessBadge.tsx`: 화면 진입 시 및 일정 주기(예: 30초)로 `/api/freshness` 폴링, 상태에 따라 배지 스타일 전환(목업의 fresh/stale 배지 디자인 재사용)
- [x] "다시 분석" 버튼: `/api/analyze` 재호출 → 그래프/배지 갱신, 진행 중에는 버튼 비활성화 + 로딩 표시

### 2.3 완료 기준

- [x] 그래프에 포함된 파일 하나를 실제로 수정한 뒤 새로고침 없이도 배지가 "변경됨"으로 바뀐다
- [x] "다시 분석"을 누르면 배지가 "최신 상태"로 돌아오고 그래프에 변경 사항이 반영된다

---

## 3단계 — 에이전트 연동 넓히기

목표: 그래프에서 선택한 위치를 로컬 MCP 연동으로 Claude Code에게 바로 물어볼 수 있는 상태. 클립보드 방식은 대안으로 계속 남긴다.

### 3.1 TraceLens.Mcp (신규 프로젝트)

- [x] 공식 MCP C# SDK(`ModelContextProtocol` 패키지)로 stdio 기반 로컬 MCP 서버 구성
- [x] 도구: `get_node(nodeId)` — 노드 메타데이터 + 소스 반환, `get_source(file, startLine, endLine)`, `get_callers(nodeId)` / `get_callees(nodeId)` — 그래프 인접 노드 조회, `get_entrypoints()`, `search_symbol(query)` — 이름으로 노드 검색
- [x] 데이터 소스는 현재 로드된 `.tracelens/graph.json`을 그대로 읽음(Server와 파일을 공유, 별도 DB 없음)
- [x] Claude Code 쪽 설정 예시를 `docs/mcp-setup.md`에 기록(`claude mcp add` 명령 또는 `.mcp.json` 예시)

### 3.2 Server

- [x] `POST /api/ask` 중 `mode: "mcp"` 처리: 캡처된 위치 정보 + 질문을 프롬프트로 포맷 → `claude -p "<prompt>" --mcp-config <mcp 설정 경로>`를 `System.Diagnostics.Process`로 헤드리스 실행 → stdout을 Server-Sent Events로 클라이언트에 스트리밍
- [x] `mode: "editor"` 처리: 서버가 `code --goto {file}:{line}`을 로컬 프로세스로 실행(실패 시 프론트가 `vscode://file/...` URI를 여는 방식으로 폴백)
- [x] 프로세스 실행 로직은 테스트 가능하도록 `IProcessRunner` 인터페이스로 감싸서 실제 프로세스 실행 없이 단위 테스트 가능하게 함

### 3.3 Client

- [x] `AskPanel.tsx`에 "MCP 직접 연동" 모드 활성화: `/api/ask` 호출 + SSE 구독, 응답 스트림을 채팅 버블 형태로 표시(목업의 로그 영역 UI 재사용)
- [x] "에디터에서 열기" 모드 연결

### 3.4 완료 기준

- [x] Claude Code가 로컬 MCP 서버의 도구 목록을 인식하고 `get_source` 등을 실제로 호출할 수 있다
- [ ] 그래프에서 노드를 선택하고 "MCP 직접 연동"으로 질문을 보내면, 실제 Claude Code 응답이 UI 로그 영역에 스트리밍되어 보인다
- [x] 클립보드/에디터 모드가 3단계 이후에도 그대로 동작한다(회귀 없음)

---

## 4단계 — 클래스 기준 보기

목표: 진입점 목록 외에 두 번째 탐색 축으로 클래스 메뉴를 두고, 클래스를 고르면 그 클래스 중심의 호출 그래프를 볼 수 있는 상태. 파일 트리 탐색이 아니라 좌측 패널 탭 + 검색 메뉴로 고르는 방식이다.

### 4.1 Analyzer

- [x] `SolutionScanner`: 분석 가능한 모든 프로젝트의 문서를 한 번씩 훑어 클래스/레코드/구조체 선언과 그 메서드를 수집(인터페이스, `obj`·`bin` 아래 자동 생성 소스 제외). partial 클래스는 첫 선언 위치 기준으로 병합한다
- [x] `InvocationResolver`: 호출식 → 심볼 해석 + DI 인터페이스 해석 로직을 `CallGraphWalker`에서 분리해서 솔루션 전체 스캔과 진입점 BFS가 함께 쓰도록 함
- [x] `AnalysisScope` 설정(`solution` 기본 / `entryPoints`) 추가. `solution`이면 스캔 순회에서 노드·엣지까지 만들고, `entryPoints`면 기존 진입점 BFS 결과를 쓴다
- [x] 스키마 v2: `GraphDocument.classes` 추가, `GraphNode.classId` 추가. v1로 저장된 그래프를 읽으면 `classes`는 빈 목록으로 처리
- [x] 단위 테스트: 진입점에서 도달하지 못하는 클래스(`Fixtures/FixtureApp/Jobs/CleanupJob.cs`)가 클래스 목록과 엣지에 나오는지, `entryPoints` 범위에서는 그래프에서 빠지지만 클래스 목록에는 남는지 검증

### 4.2 Server

- [x] `GET /api/graph/classes`: 클래스 요약 목록 반환(`methodIds` 대신 `methodCount`)
- [x] `POST /api/analyze` 응답에 `classCount` 추가
- [x] `GraphFreshnessService` 해시 계산을 제한 병렬(8)로 변경 — 솔루션 전체 분석으로 대상 파일 수가 늘어난 것에 대응

### 4.3 Client

- [x] 좌측 패널에 `진입점 | 클래스` 탭 추가, 클래스 탭에는 이름/네임스페이스 검색 입력과 목록(한 번에 200개까지 렌더링)
- [x] `getClassWindow`: 클래스의 메서드 노드 + 직접 호출자/피호출자 1홉 창 계산(순수 함수 + 단위 테스트)
- [x] `GraphView`: 선택한 클래스의 메서드를 compound node 박스로 묶어 표시, 메서드가 없는 클래스는 안내 문구 표시
- [x] 재분석 시 선택 상태(진입점/클래스) 복원, breadcrumb 최상위가 클래스면 클래스 창으로 복귀

### 4.4 완료 기준

- [x] 클래스 탭에서 클래스를 고르면 그 클래스의 메서드와 호출자/피호출자가 한 화면에 뜬다
- [x] 진입점에서 도달하지 못하는 클래스도 목록에 뜨고, `solution` 범위에서는 호출 관계까지 보인다
- [x] 클래스 창에서 노드를 클릭하면 기존 코드 패널/질문 패널이 그대로 동작한다

---

## 5단계 — 필요해지면 그때 확장 (백로그, 지금 설계하지 않음)

아래 항목은 실제로 도구를 써보면서 필요성이 확인되면 그때 별도로 설계한다. 지금 단계에서 코드에 미리 훅을 만들어두지 않는다.

- 대규모 코드베이스에서 진입점 도달 범위 자체가 너무 커질 경우, 전체 사전 계산 대신 노드 단위 온디맨드 확장 API로 전환
- DI 의존 관계, 이벤트/메시지(MediatR 등) 흐름을 별도 레이어(엣지 종류 토글)로 추가할지 검토
- 여러 명이 동시에 보는 공유 뷰(원격 접근을 포함하므로 설계 제약 4번과 충돌 여부부터 재검토 필요)
- 그래프 검색/필터링 UI(이름, kind, 네임스페이스 기준)
- `.tracelens/graph.json` 저장 방식을 JSON에서 SQLite 등으로 교체할지 여부(현재는 JSON 파일로 충분하다고 판단)
