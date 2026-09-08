# TraceLens 데이터 계약

`graph.json`의 스키마와 HTTP·MCP 계약을 한곳에 적는다.
정본은 코드다 — Analyzer의 `Models/`, Server의 `Endpoints/`, client의 `types/`, `TraceLens.Context`의 DTO.
이 문서는 그 세 곳을 함께 바꿔야 한다는 사실과 **버전 이행 규칙**을 남기는 것이 목적이다.

현재 스키마는 **v7**이다.

## 1. 스키마 버전 이력

| 버전 | 추가된 것 |
|---|---|
| v1 | `entryPoints`, `nodes`, `edges`, `fileHashes` |
| v2 | `rootDirectory`(분석 루트) |
| v3 | `classes` |
| v4 | 타입 메타·상속·구현·멤버, 노드의 접근제한자·반환 타입·파라미터 타입 |
| v5 | `startupFlow`(Program.cs의 DI 등록·미들웨어 파이프라인) |
| v6 | `dataAccess`(DB·캐시 접근과 보상 처리), `middleware`(미들웨어 파이프라인) |
| **v7** | `snapshotId`, `inputFingerprint`, `manifest`, `diagnostics`, `callSites`, `analyzerVersion`, `rulesVersion`, 진입점의 `endpointId`·출처·확실도, 노드의 `callable`·`stableKey`·지문 |

## 2. 이행 규칙

읽는 쪽이 지켜야 할 규칙이다. 클라이언트는 `api/client.ts`의 `normalizeGraph`가, 서버·MCP는
`TraceLens.Context`의 `GraphJsonStore`가 이 규칙을 구현한다.

1. **없는 정보는 채우지 않는다.** v6 이하 문서를 읽으면 새 필드는 빈 목록·null·기본값으로 둔다. 존재하지 않는 호출 근거나 본문 지문을 추정으로 만들어 넣지 않는다.
2. **`endpointId`는 호환 생성만 한다.** v6 이하에는 없으므로 `"{METHOD} {route} :: {nodeId}"` 형태로 만들어 조회에 쓴다. 이 값으로 **이미 유실된 복수 라우트를 복원했다고 표시하지 않는다.**
3. **미래 스키마는 명시적으로 거절한다.** `schemaVersion > MaxSupportedSchemaVersion`이면 읽지 않고 `E_SCHEMA_TOO_NEW`로 알린다.
4. **읽을 때 최소 무결성을 확인한다.** 중복 노드 ID(`W_DUPLICATE_NODE`), 존재하지 않는 노드를 참조하는 엣지(`W_DANGLING_EDGE`), 핸들러가 없는 진입점(`W_DANGLING_ENTRYPOINT`), 파일 경로가 빈 노드(`W_NO_FILE`)를 경고로 남긴다.
5. **신선도 입력이 없으면 `fresh`라고 하지 않는다.** `manifest`가 없는 문서는 바뀐 파일이 없어도 `unknown`이다(새 파일·설정 변경을 확인할 방법이 없기 때문이다).
6. **저장은 항상 원자적으로 한다.** 임시 파일에 완전히 쓰고, 다시 읽어 파싱되는지 확인한 다음에야 교체한다. 실패하면 이전 정상 그래프가 남는다.

## 3. `graph.json` 최소 예제 (v7)

값은 형태를 보여 주기 위한 예시이며 실제 분석 결과가 아니다.

```json
{
  "schemaVersion": 7,
  "solutionPath": "C:/repos/OrderApi/OrderApi.csproj",
  "rootDirectory": "C:/repos/OrderApi",
  "analyzedAt": "2026-09-08T11:54:29.000+09:00",
  "snapshotId": "20260908T025429Z-255aa5bf",
  "inputFingerprint": "sha256:255aa5bf…",
  "analyzerVersion": "1.0.0",
  "rulesVersion": "1",
  "entryPoints": [
    {
      "nodeId": "OrderApi.Controllers.OrdersController.CreateAsync(OrderApi.Contracts.CreateOrderRequest,System.Threading.CancellationToken)",
      "httpMethod": "POST",
      "route": "/api/Orders",
      "endpointId": "OrderApi|POST|/api/orders|OrderApi::OrderApi.Controllers.OrdersController::Ordinary::CreateAsync(…)",
      "normalizedRoute": "/api/orders",
      "source": "Controller",
      "handlerKind": "Method",
      "confidence": "Certain",
      "registrationFile": "Controllers/OrdersController.cs",
      "registrationLine": 18,
      "handlerDisplayName": "OrdersController.CreateAsync",
      "projectKey": "OrderApi",
      "notes": []
    }
  ],
  "classes": [],
  "nodes": [
    {
      "id": "OrderApi.Services.OrderService.CreateAsync(…)",
      "kind": "Service",
      "displayName": "OrderService.CreateAsync",
      "namespace": "OrderApi.Services",
      "classId": "OrderApi.Services.OrderService",
      "file": "Services/OrderService.cs",
      "startLine": 44,
      "endLine": 51,
      "accessibility": "Public",
      "returnType": "Task<string>",
      "parameterTypes": ["OrderApi.Contracts.CreateOrderRequest", "System.Threading.CancellationToken"],
      "callable": "Method",
      "stableKey": "OrderApi::OrderApi.Services.OrderService::Ordinary::CreateAsync(…)",
      "signatureFingerprint": "sha256:…",
      "bodyFingerprint": "sha256:…"
    }
  ],
  "edges": [{ "from": "…OrdersController.CreateAsync(…)", "to": "…OrderService.CreateAsync(…)" }],
  "callSites": [
    {
      "from": "…OrdersController.CreateAsync(…)",
      "to": "…OrderService.CreateAsync(…)",
      "declaredTarget": "OrderApi.Services.IOrderService.CreateAsync(…)",
      "file": "Controllers/OrdersController.cs",
      "line": 22,
      "resolution": "DiRegistration",
      "confidence": "Certain",
      "note": "DI 등록으로 OrderService에 연결했다."
    }
  ],
  "fileHashes": { "Services/OrderService.cs": "sha256:…" },
  "manifest": {
    "inputs": [{ "path": "OrderApi.csproj", "hash": "sha256:…", "kind": "Project" }],
    "sourceRoots": ["."],
    "excludedDirectories": ["bin", "obj", ".git", ".tracelens", ".vs", "node_modules"],
    "linkedSources": [],
    "limitations": ["MSBuild import 전개 결과와 analyzer·source generator가 만든 소스는 입력 목록에 넣지 않는다."]
  },
  "diagnostics": {
    "totalProjects": 1,
    "loadedProjects": 1,
    "projectsWithCompileErrors": 0,
    "projects": [],
    "calls": {
      "inspected": 28, "resolved": 28, "direct": 12, "diRegistration": 16,
      "singleImplementation": 0, "ambiguous": 0, "unresolved": 0, "external": 143,
      "resolutionRate": 1.0
    },
    "excludedNamespacePrefixes": ["System", "Microsoft", "Swashbuckle"],
    "maxCallDepth": 8,
    "depthLimitReached": false,
    "analysisScope": "solution",
    "limitations": [],
    "changedDuringAnalysis": false,
    "changedDuringAnalysisFiles": [],
    "entryPointNotes": []
  },
  "startupFlow": null,
  "dataAccess": null,
  "middleware": null
}
```

## 4. 식별자 규칙

| 값 | 규칙 | 왜 이렇게 하나 |
|---|---|---|
| `nodes[].id` | `{전체타입이름}.{메서드}({파라미터타입,…})` | v1부터 쓰던 값이라 호환을 위해 유지한다. |
| `nodes[].id` (람다) | `{classId}.{감싼멤버}#lambda[{순번}]` | 람다는 이름이 없다. 순번은 감싸는 멤버 안 **소스 순서**다. 줄 번호를 쓰지 않아 코드가 밀려도 유지된다. |
| `nodes[].id` (로컬 함수) | `{classId}.{감싼멤버}#local({이름})` | 같은 타입의 진짜 메서드와 충돌하지 않게 구분한다. |
| `nodes[].stableKey` | `{projectKey}::{타입}::{메서드종류}::{이름}\`{제네릭수}({ref종류 타입,…})` | `id`는 프로젝트가 다른 동명 타입과 일부 오버로드를 구분하지 못한다. **비교는 이 키로 한다.** |
| `entryPoints[].endpointId` | `{projectKey}\|{HTTP동사}\|{정규화라우트}\|{등록식별자}` | 엔드포인트와 핸들러를 분리해서 같은 메서드의 여러 라우트를 잃지 않는다. 중복이 남으면 `#2`를 붙인다. |
| `snapshotId` | `{UTC yyyyMMddTHHmmssZ}-{입력지문 앞 8자}` | 분석 실행 하나를 가리킨다. 같은 코드를 다시 분석하면 새 값이 나온다. |
| `inputFingerprint` | 소스 해시 + 설정 해시 + 분석기·규칙 버전 + 범위를 접은 sha256 | 같은 입력이면 몇 번을 분석해도 같다. "내용이 같은 분석"을 알아보는 데 쓴다. |

정규화 라우트는 앞 슬래시 고정, 중복 슬래시 제거, 끝 슬래시 제거, `{id:int}`·`{id?}`의 제약·기본값 제거,
소문자화까지 적용한 값이다.

## 5. HTTP API

새 API의 오류는 `{ code, message, details }`로 통일한다. 과거 API는 `error` 필드를 유지하면서
같은 `code`를 함께 담는다. UI는 `code`로 번역하고, 모르는 코드는 서버 메시지를 그대로 보여 준다.

| 엔드포인트 | 처리 | 주요 실패 |
|---|---|---|
| `POST /api/analyze` | 분석하고 결과를 메모리와 `.tracelens/graph.json`에 반영한다(호환 경로). | `400 ANALYZE_FAILED` |
| `POST /api/analyze/load` | 재분석하지 않고 대상 폴더의 `.tracelens/graph.json`을 그대로 불러온다(서버 재시작 직후 복원용). | `400 INVALID_REQUEST` |
| `POST /api/analysis-jobs` | 분석을 작업으로 시작한다. `202`와 jobId를 돌려준다. | `400 INVALID_REQUEST` |
| `GET /api/analysis-jobs/{id}` | 작업 하나의 현재 상태를 조회한다(스트리밍 없이 한 번만). | `404 JOB_NOT_FOUND` |
| `GET /api/analysis-jobs/{id}/events` | 단계별 진행 상태를 SSE로 보낸다(진행률 % 없음). | `404 JOB_NOT_FOUND` |
| `DELETE /api/analysis-jobs/{id}` | 취소를 요청한다. 취소해도 기존 그래프는 유지된다. | `409 JOB_NOT_CANCELABLE` |
| `POST /api/analyze/import` | 사용자가 고른 그래프 파일을 `offlineSnapshot`으로 불러온다. 활성 `graph.json`을 덮어쓰지 않는다. | `400 GRAPH_INVALID` |
| `POST /api/analyze/adopt` | 가져온 결과의 원본 루트를 연결한다. 입력이 일치할 때만 소스 접근이 열린다. | `409 INPUT_MISMATCH` |
| `GET /api/freshness` | `fresh`/`stale`/`unknown`과 변경 종류·이유를 돌려준다. 재분석하지 않는다. | `404 NO_ANALYSIS` |
| `GET /api/analysis-status` | 분석 대상·시각·신선도·지원 기능·진단을 돌려준다. | `404 NO_ANALYSIS` |
| `GET /api/diagnostics` | 환경과 분석 능력을 점검한다. 비밀값을 반환하거나 설치를 실행하지 않는다. | — |
| `GET /api/source` | 분석 루트 안 소스를 읽고 `sourceVersion`(snapshot/current)을 함께 준다. | `400 SOURCE_DENIED`, `404` |
| `POST /api/context` | 선택·목적·예산에서 문맥 DTO와 Markdown을 만든다. | `400 INVALID_REQUEST`, `404 SELECTION_NOT_FOUND`, `409 SNAPSHOT_MISMATCH` |
| `POST /api/report` | 선택 범위만 담는 공유용 Markdown을 만든다(절대 경로·소스 본문 제외). | 위와 같다 |
| `POST /api/impact` | 여러 nodeId의 영향 후보를 조회한다. 상한과 중단 이유를 함께 준다. | `400 INVALID_REQUEST` |
| `GET/POST /api/snapshots` | 기준 목록 조회 / 현재 분석을 기준으로 저장한다. | `400 OFFLINE_SNAPSHOT` |
| `DELETE /api/snapshots/{snapshotId}` | 보관한 기준 하나를 지운다. | `404 SNAPSHOT_NOT_FOUND` |
| `POST /api/snapshots/compare` | 보관한 기준과 비교한다. 임의 경로는 받지 않는다. | `404 BASELINE_NOT_FOUND` |
| `GET /api/mcp-setup` | 대상·호스트에 맞춘 MCP 설정 미리보기를 준다. | `400 MCP_HOST_MISSING` |
| `POST /api/ask` | 문맥을 만들어 외부 CLI에 전달하고 응답을 SSE로 중계한다. | `404 NO_ANALYSIS`, `409 SNAPSHOT_MISMATCH` |
| `GET /api/browse` | 로컬 디렉터리를 훑어 `.sln`/`.csproj`를 찾는 대상 선택용 API(브라우저 파일 선택창은 절대 경로를 안 주므로). | — |
| `GET /api/graph`, `/graph/entrypoints`, `/graph/classes`, `/graph/namespaces` | 현재 메모리 그래프 전체/진입점/클래스 요약/네임스페이스 요약을 돌려준다(옛 `{ error }` 형태). | `404` |

### 오류 코드

`NO_ANALYSIS`, `SNAPSHOT_MISMATCH`, `SELECTION_NOT_FOUND`, `INVALID_REQUEST`, `SOURCE_DENIED`,
`ANALYZE_FAILED`, `OFFLINE_SNAPSHOT`, `GRAPH_INVALID`, `INPUT_MISMATCH`, `BASELINE_NOT_FOUND`,
`SNAPSHOT_NOT_FOUND`, `JOB_NOT_FOUND`, `JOB_NOT_CANCELABLE`, `MCP_HOST_MISSING`.

doctor는 별도로 `SDK_NOT_FOUND`, `SDK_MISMATCH`, `TARGET_NOT_FOUND`, `UNSUPPORTED_TARGET_FORMAT`,
`OUTPUT_NOT_WRITABLE`, `PORT_IN_USE`, `MCP_HOST_MISSING`을 쓴다.

## 6. 문맥 DTO (`contextVersion` 1)

`POST /api/context`와 MCP의 `get_context`가 **같은 DTO**를 돌려준다. UI 미리보기·클립보드·인앱 실행이
모두 이 하나에서 나오므로 어디서 복사해도 같은 문맥이 된다.

```
contextVersion, snapshotId, inputFingerprint, selection{kind,id,displayName},
intent, budget, freshness{status,reasons,changedFiles}, summary,
analysis{analyzedAt,analysisScope,loadedProjects,totalProjects,projectsWithCompileErrors,
         inspectedCallSites,resolvedCallSites,resolutionRate},
evidence[]{id,kind,file,startLine,endLine,resolution,confidence,detail},
sourceSnippets[]{evidenceId,file,startLine,endLine,content,truncated,sourceVersion,omittedRanges},
paths[]{endpointId,httpMethod,route,steps[],truncated},
callers[], callees[], findings[], testCandidates[],
limitations[], truncated, omitted{nodes,sourceRanges,sections},
question, markdown, characters, estimatedTokens, containsSource, files[]
```

- `estimatedTokens`는 모델별 tokenizer가 아니라 문자 수에서 추정한 값이다. 화면에도 "예상"이라고 쓴다.
- `sourceVersion`이 `current`면 분석 이후에 바뀐 현재 소스다. 오래된 위치에 현재 코드를 붙여 놓고 "분석 당시 코드"라고 부르지 않는다.
- `freshness.status`가 `stale`이면 수정용 문맥에 소스 본문을 기본으로 넣지 않는다.

## 7. `tracelens.config.json`

분석 루트에 두면 읽는다. 없어도 동작한다.

| 키 | 기본값 | 설명 |
|---|---|---|
| `entryPointAttributes` | `["HttpGet","HttpPost","HttpPut","HttpDelete","HttpPatch"]` | 컨트롤러 진입점으로 볼 특성 이름 |
| `kindRules` | 6개 규칙 | 클래스 분류(Controller/Service/Repository/…) |
| `maxCallDepth` | `8` | `analysisScope=entryPoints`에서 내려갈 최대 깊이 |
| `excludeNamespacePrefixes` | `["System","Microsoft","Swashbuckle"]` | 그래프에서 제외할 네임스페이스 |
| `analysisScope` | `solution` | `solution`이면 전체를, `entryPoints`면 진입점에서 도달 가능한 것만 |
| `includeObjectCreation` | `true` | `new X()`를 생성자 호출로 그래프에 넣을지 |
| `includeMembers` | `true` | 클래스 다이어그램용 멤버 수집 |
| `includeInterfaces` | `true` | 인터페이스 선언을 `classes`에 넣을지 |
| `includeStartupFlow` | `true` | 시작 흐름 수집 |
| `includeDataAccess` | `true` | DB·캐시 접근과 보상 처리 수집 |
| `includeMiddleware` | `true` | 미들웨어 파이프라인 수집 |
| `includeCallSites` | `true` | 호출 근거(`callSites`)를 저장할지 |
| `maxCallSitesPerEdge` | `3` | 한 엣지에 남길 근거 수 상한 |
| `maxBoundaryCallSites` | `300` | 미해석·모호 호출의 상한 |
| `db` | — | 사내 DB·캐시 계층 설정(`queryApis`, `cacheApis`, `dataSources`, `compensation` 등) |

예제 설정은 [`samples/OrderApi/tracelens.config.json`](../samples/OrderApi/tracelens.config.json)에 있다.

## 8. 스키마를 바꿀 때

1. Analyzer의 `Models/`를 바꾼다(새 필드는 `init` 프로퍼티 + 기본값으로 두어 옛 문서도 읽히게 한다).
2. `GraphDocument.CurrentSchemaVersion`을 올리고 XML 주석에 무엇이 추가됐는지 적는다.
3. `TraceLens.Context`의 `GraphIndex`·`GraphJsonStore`가 새 필드를 다루게 한다.
4. Server 응답 DTO와 MCP 도구 반환을 맞춘다. 기존 클라이언트를 깨는 변경이면 **새 도구 이름이나 명시적 버전**을 쓴다.
5. client의 `types/graph.ts`·`types/context.ts`와 `normalizeGraph`를 함께 갱신한다.
6. 이 문서의 버전 이력·예제·이행 규칙을 갱신한다.
