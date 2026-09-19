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

**부가 필드는 버전을 올리지 않는다.** 기본값이 있고 옛 문서를 그대로 읽을 수 있으면 v7 안에서 더한다.
0.8.0에서 이렇게 더한 필드는 아래와 같다. 읽는 쪽은 없으면 이 기본값으로 채운다(2절 1번 규칙).

| 필드 | 뜻 | 없을 때 |
|---|---|---|
| `edges[].confidence` | 이 엣지를 확정했는지(`Certain`/`Estimated`) | `Certain` |
| `callSites[].viaLambda` | 람다 본문 안의 호출이라 감싸는 메서드에 귀속했다 | `false` |
| `manifest.ignoredSources` | 입력 목록에 넣지 않은 소스(생성 코드 등). 신선도의 "새 파일"에서도 뺀다 | `[]` |
| `dataAccess.accesses[].confidence` | 대상 이름을 상수로 확정했는지 | `Certain` |
| `dataAccess.accesses[].note` | 그렇게 판정한 근거 한 줄 | `""` |
| `dataAccess.accesses[].transactionWrapper` | 이 접근을 감싼 트랜잭션 래퍼 메서드 이름 | `""` |
| `dataAccess.transactions[].kind` | `Explicit`(직접 Begin/Commit) 또는 `Wrapper`(람다를 받는 래퍼) | `Explicit` |
| `dataAccess.transactions[].wrapperName` | `Wrapper`일 때 그 래퍼 이름 | `""` |
| `middleware.steps[].kind` | `Middleware` / `EndpointRegistration` / `Terminal` | `Middleware` |
| `startupFlows[]` | 호스트가 여럿인 솔루션의 시작 흐름 전부. `startupFlow`는 그중 첫 번째 | `[]` |
| `middlewarePipelines[]` | 같은 이유로 파이프라인 전부. `middleware`는 그중 첫 번째 | `[]` |

`startupFlow`/`middleware` 단수 필드는 호환을 위해 남긴다. **새 코드는 복수 쪽을 읽는다.**

## 2. 이행 규칙

읽는 쪽이 지켜야 할 규칙이다. 클라이언트는 `api/client.ts`의 `normalizeGraph`가, 서버·MCP는
`TraceLens.Context`의 `GraphJsonStore`가 이 규칙을 구현한다.

1. **없는 정보는 채우지 않는다.** v6 이하 문서를 읽으면 새 필드는 빈 목록·null·기본값으로 둔다. 존재하지 않는 호출 근거나 본문 지문을 추정으로 만들어 넣지 않는다.
2. **`endpointId`는 호환 생성만 한다.** v6 이하에는 없으므로 `"{METHOD} {route} :: {nodeId}"` 형태로 만들어 조회에 쓴다. 이 값으로 **이미 유실된 복수 라우트를 복원했다고 표시하지 않는다.**
3. **미래 스키마는 명시적으로 거절한다.** `schemaVersion > MaxSupportedSchemaVersion`이면 읽지 않고 `E_SCHEMA_TOO_NEW`로 알린다.
4. **읽을 때 최소 무결성을 확인한다.** 중복 노드 ID(`W_DUPLICATE_NODE`), 존재하지 않는 노드를 참조하는 엣지(`W_DANGLING_EDGE`), 핸들러가 없는 진입점(`W_DANGLING_ENTRYPOINT`), 파일 경로가 빈 노드(`W_NO_FILE`)를 경고로 남긴다.
5. **신선도 입력이 없으면 `fresh`라고 하지 않는다.** `manifest`가 없는 문서는 바뀐 파일이 없어도 `unknown`이다(새 파일·설정 변경을 확인할 방법이 없기 때문이다).
6. **저장은 항상 원자적으로 한다.** 임시 파일에 완전히 쓰고, 다시 읽어 파싱되는지 확인한 다음에야 교체한다. 실패하면 이전 정상 그래프가 남는다.

### v7의 부가 필드 (schema 번호를 올리지 않는다)

아래 셋은 **추가 필드**다. 옛 그래프를 읽으면 빈 목록이 되고, 있으면 그대로 그린다.
그래프의 노드·엣지·접근 수를 바꾸지 않으므로 스냅샷 비교에도 영향이 없다.

| 필드 | 뜻 | 없으면 |
|---|---|---|
| `diagnostics.calls.externalByNamespace` | 솔루션 밖 호출을 네임스페이스로 묶은 상위 20개(건수·대표 타입). 알려진 프레임워크는 `diagnostics.limitations`에 한 줄로 승격된다. | 빈 목록 — 진단 패널의 해당 절을 그리지 않는다 |
| `diagnostics.entryPointCandidates` | HTTP 밖 실행 단위 후보(`HostedService`·`MessageConsumer`·`SignalRHub`·`GrpcService`·`ScheduledJob`·`AzureFunction`·`HealthCheck`·`ConventionalRouting`). 진입점·엣지를 만들지 않는다. | 빈 목록 |
| `middleware.steps[].type = "Extension"` | 솔루션 안 확장 메서드(`app.UseCompanyAuth()`). 본문에서 등록한 미들웨어를 하위 단계로 펼치고 `parentId`로 잇는다(깊이 2). 펼치지 못하면 그 사유를 `pre`에 적는다. | `Framework`로 읽는다 |
| `middleware.steps[].parentId` | 이 단계를 펼쳐서 나온 상위 단계 ID. 최상위면 null. | null |
| `browse` 응답의 `kind = "Unsupported"` | 아직 지원하지 않는 형식(.slnx 등). 목록에는 보이되 고를 수 없고 `reason`에 사유가 있다. | 이 값이 없던 서버는 그 파일을 목록에서 뺐다 |
| `get_analysis_status`의 `suggestedNextTools` | 지금 상태에서 다음에 부를 만한 MCP 도구. 보장이 아니라 제안이다. | 빈 목록 |
| `get_analysis_status`의 `dataAccessNotes` | `dataAccess.notes`와 같은 내용. 에이전트가 빈 결과를 잘못 읽지 않게 한다. | 빈 목록 |
| `entryPoints[].apiVersions` | `[ApiVersion]`으로 선언한 버전. 라우트에 `{version:apiVersion}` 토큰이 있으면 버전마다 진입점이 하나씩 나오고, 없으면 라우트는 같고 `endpointId`로만 구분한다. | 빈 목록 |
| `dataAccess.accesses[].operation = "Unknown"` | 저장 프로시저처럼 본문을 읽지 않아 읽기/쓰기를 확정하지 못한 접근. 쓰기 경로 단계로 세지 않고 점검 결과 상단에 개수로만 나온다. | 이 값이 없던 문서에는 나오지 않는다 |
| `dataAccess.transactions[].kind = "Scope"` | `using var scope = new TransactionScope(...)`로 연 주변 트랜잭션. `scope.Complete()`가 커밋이다. | `Explicit`로 읽는다 |
| `dataAccess.notes` | 인식했지만 해석하지 않은 저장소 입력(EF Core·Mongo·NHibernate 타입이 있는데 접근 0건 등). 규칙 결과는 바꾸지 않는다. | 빈 목록 |

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
  "edges": [{ "from": "…OrdersController.CreateAsync(…)", "to": "…OrderService.CreateAsync(…)", "confidence": "Certain" }],
  "callSites": [
    {
      "from": "…OrdersController.CreateAsync(…)",
      "to": "…OrderService.CreateAsync(…)",
      "declaredTarget": "OrderApi.Services.IOrderService.CreateAsync(…)",
      "file": "Controllers/OrdersController.cs",
      "line": 22,
      "resolution": "DiRegistration",
      "confidence": "Certain",
      "viaLambda": false,
      "note": "DI 등록으로 OrderService에 연결했다."
    }
  ],
  "fileHashes": { "Services/OrderService.cs": "sha256:…" },
  "manifest": {
    "inputs": [{ "path": "OrderApi.csproj", "hash": "sha256:…", "kind": "Project" }],
    "sourceRoots": ["."],
    "excludedDirectories": ["bin", "obj", ".git", ".tracelens", ".vs", "node_modules"],
    "linkedSources": [],
    "ignoredSources": [],
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
      "resolutionRate": 1.0,
      "externalByNamespace": [
        { "namespace": "Microsoft.EntityFrameworkCore", "count": 96, "sampleType": "DbSet" },
        { "namespace": "System.Transactions", "count": 4, "sampleType": "TransactionScope" }
      ]
    },
    "excludedNamespacePrefixes": ["System", "Microsoft", "Swashbuckle"],
    "maxCallDepth": 8,
    "depthLimitReached": false,
    "analysisScope": "solution",
    "limitations": [],
    "changedDuringAnalysis": false,
    "changedDuringAnalysisFiles": [],
    "entryPointNotes": [],
    "entryPointCandidates": [
      { "kind": "HostedService", "count": 3, "samples": ["RankBatchWorker", "CleanupWorker"] }
    ]
  },
  "startupFlow": null,
  "dataAccess": null,
  "middleware": null,
  "startupFlows": [],
  "middlewarePipelines": []
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
| `DELETE /api/ask/{runId}` | 진행 중인 질문 실행을 취소한다. | `404 RUN_NOT_FOUND` |
| `GET /api/samples` | 배포물에 함께 넣은 예제 목록과 예상 결과를 돌려준다. | — |
| `POST /api/samples/{id}/prepare` | 예제를 `%LOCALAPPDATA%\TraceLens\samples\{id}`에 복사한다. 이미 있으면 그대로 쓴다(`overwrite`로 덮어쓴다). | `404 SAMPLE_NOT_FOUND`, `500 SAMPLE_COPY_FAILED` |
| `GET /api/browse` | 로컬 디렉터리를 훑어 `.sln`/`.csproj`를 찾는 대상 선택용 API(브라우저 파일 선택창은 절대 경로를 안 주므로). | `400 SOURCE_DENIED`, `400 INVALID_REQUEST` |
| `GET /api/graph`, `/graph/entrypoints`, `/graph/classes`, `/graph/namespaces` | 현재 메모리 그래프 전체/진입점/클래스 요약/네임스페이스 요약을 돌려준다. 실패 응답은 옛 `error` 필드를 유지하면서 `code`를 함께 담는다. | `404 NO_ANALYSIS` |
| `GET /api/analysis-jobs` | 진행 중·최근 분석 작업 목록. 새로고침 뒤 진행 중 작업에 다시 붙을 때 쓴다. | — |
| `GET /manual/{lang}` | `ko`·`en` 설명서를 정적으로 서빙한다. | `404 MANUAL_NOT_FOUND` |

### 오류 코드

`NO_ANALYSIS`, `SNAPSHOT_MISMATCH`, `SELECTION_NOT_FOUND`, `INVALID_REQUEST`, `SOURCE_DENIED`,
`ANALYZE_FAILED`, `OFFLINE_SNAPSHOT`, `GRAPH_INVALID`, `GRAPH_TOO_LARGE`, `INPUT_MISMATCH`,
`BASELINE_NOT_FOUND`, `SNAPSHOT_NOT_FOUND`, `SNAPSHOT_SAVE_FAILED`, `JOB_NOT_FOUND`,
`JOB_NOT_CANCELABLE`, `JOB_ALREADY_RUNNING`, `RUN_NOT_FOUND`, `AGENT_NOT_FOUND`, `AGENT_TIMEOUT`,
`SAMPLE_NOT_FOUND`, `SAMPLE_COPY_FAILED`, `MCP_HOST_MISSING`, `MANUAL_NOT_FOUND`, `FORBIDDEN_ORIGIN`,
`UNEXPECTED_ERROR`.

- `JOB_ALREADY_RUNNING`(409) — 같은 대상의 분석이 이미 돌고 있다. 두 번째 요청은 시작하지 않는다.
- `FORBIDDEN_ORIGIN`(403) — 상태를 바꾸는 `/api/*` 요청의 `Origin`이 이 서버가 아니다. 루프백 전용 도구라
  브라우저 밖에서 온 요청(Origin 없음)은 막지 않는다.
- `UNEXPECTED_ERROR`(500) — 처리하지 못한 예외다. `details.errorId`가 로그 파일의 같은 id와 이어진다.
- 클라이언트는 서버에 닿지 못했을 때 `NETWORK`(status 0)를 자체적으로 만든다. 서버가 보내는 코드가 아니다.

doctor는 별도로 `SDK_NOT_FOUND`, `SDK_MISMATCH`, `TARGET_NOT_FOUND`, `UNSUPPORTED_TARGET_FORMAT`,
`OUTPUT_NOT_WRITABLE`, `PORT_IN_USE`, `MCP_HOST_MISSING`을 쓴다(정상일 때는 `SDK_FOUND`·`TARGET_FOUND`·
`OUTPUT_WRITABLE`·`PORT_FREE`·`MCP_HOST_FOUND`처럼 같은 자리에 성공 코드를 넣는다).
서버 안에서 부른 점검은 자기 자신이 듣고 있는 포트를 `PORT_SELF`(Ok)로 돌려준다 —
"이미 실행 중인 TraceLens를 종료하라"는 자기 자신을 가리키는 처방이 되기 때문이다.

`graph.json`을 읽을 때 쓰는 코드는 따로다: `E_NOT_FOUND`, `E_EMPTY`, `E_INVALID_JSON`,
`E_SCHEMA_TOO_NEW`, `E_NO_TARGET`와 경고 `W_DUPLICATE_NODE`·`W_DANGLING_EDGE`·
`W_DANGLING_ENTRYPOINT`·`W_NO_FILE`.

## 6. 문맥 DTO (`contextVersion` 1)

요청은 `{ kind, id, intent, budget, question, includeSource, excludeFiles[], expectedSnapshotId }`다.
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
- `intent`는 `Explain`·`PlanChange`·`ReviewChange`·`InvestigateData`·`Share` 다섯이다.
  화면 목록에는 앞의 네 개만 둔다 — `Share`는 공유 보고서(`POST /api/report`)가 쓰는 값이고,
  소스 본문과 절대 경로를 빼는 것이 목적이라 "질문" 흐름과 섞지 않는다.
- 요청의 `excludeFiles`(접두사 일치)에 든 파일은 `sourceSnippets`에서 빠진다. 근거(`evidence`)의 위치는
  남으므로 "어디를 뺐는지"는 보인다. `files[]`는 근거와 소스에 실제로 등장한 파일을 합친 목록이다.
  비밀값을 자동으로 찾아내는 기능에 기대지 않고 **손으로 빼는 수단**을 두는 것이 이 필드의 목적이다.

## 7. MCP 도구

로컬 stdio MCP 서버(`tracelens mcp`)가 내보내는 도구다. **모두 저장된 분석 결과만 읽고 재분석하지 않는다**
(CLAUDE.md 제약 3). 모든 도구는 `expectedSnapshotId`를 받고, 응답에 `snapshotId`가 들어간다.

실패는 예외가 아니라 `{ "isError": true, "error": { "code", "message", "details" } }`로 온다.
코드 표는 §5의 HTTP 오류 코드와 **같은 것**이다(`TraceLens.Context/ContextErrors.cs`가 정본).
같은 입력에 서버와 MCP가 같은 `code`를 돌려준다.

| 도구 | 주요 파라미터 | 페이징·상한 | 주요 오류 코드 |
|---|---|---|---|
| `get_analysis_status` | `expectedSnapshotId` | 변경 파일 30개 | `SNAPSHOT_MISMATCH` |
| `get_context` | `selectionKind`, `selectionId`, `intent`, `budget`, `question`, `format`, `neighborDepth` | `format` 기본 `markdown`(구조 필드는 `json`·`both`일 때만), 예산은 실제로 돌려주는 바이트 기준 | `SELECTION_NOT_FOUND`, `SELECTION_KIND`, `SNAPSHOT_MISMATCH` |
| `get_entrypoint_flow` | `endpointId`, `depth`, `maxNodes` | 깊이 기본 4, 노드 기본 80(최대 2000) | `SELECTION_NOT_FOUND` |
| `get_findings` | `nodeId`, `ruleId`, `limit`, `cursor` | `limit` 기본 50·최대 500 | `SNAPSHOT_MISMATCH` |
| `get_impact` | `nodeIds`, `maxDepth`, `maxNodes`, `timeoutMs`, `limit`, `cursor` | `limit` 기본 200·최대 2000, `timeoutMs` 기본 5000·최대 60000 | `SELECTION_NOT_FOUND` |
| `compare_snapshots` | `baselineId`, `currentId`, `limit` | 목록마다 기본 100·최대 2000, `totals`로 전체 수 | `SNAPSHOT_NOT_FOUND` |
| `get_node` | `nodeId` | — | `SELECTION_NOT_FOUND`, `SOURCE_DENIED` |
| `get_source` | `file`, `startLine`, `endLine`, `maxLines` | `maxLines` 기본 400·최대 4000, 넘으면 `truncated`와 `nextStartLine` | `SOURCE_DENIED` |
| `get_callers` / `get_callees` | `nodeId`, `limit`, `cursor` | `limit` 기본 50·최대 500 | `SELECTION_NOT_FOUND` |
| `get_entrypoints` | `query`, `method`, `pathPrefix`, `projectKey`, `limit`, `cursor` | `limit` 기본 50·최대 500 | — |
| `search_symbol` | `query`, `limit`, `cursor` | `limit` 기본 50·최대 500 | `INVALID_REQUEST` |
| `list_snapshots` | — | — | — |
| `get_middleware_pipeline` | `projectKey`, `limit`, `cursor` | `limit` 기본 50·최대 500 | `SELECTION_NOT_FOUND` |
| `get_startup_flow` | `projectKey`, `limit`, `cursor` | `limit` 기본 50·최대 500 | `SELECTION_NOT_FOUND` |
| `get_class` | `classId` | — | `SELECTION_NOT_FOUND` |
| `list_classes` | `query`, `namespace`, `limit`, `cursor` | `limit` 기본 50·최대 500 | — |
| `list_write_paths` | `entryPointId`, `limit`, `cursor` | `limit` 기본 50·최대 500 | `SELECTION_NOT_FOUND` |

**절삭 규칙.** 결과를 잘랐으면 응답에 반드시 그 사실이 있어야 한다 —
목록은 `totalCount`(또는 `totals`)와 `nextCursor`, 소스는 `truncated`·`nextStartLine`,
문맥은 `truncated`와 `omitted.counts`로 남긴다. 상한 때문에 뺀 것을 조용히 버리지 않는다.

**`selectionKind`와 발견 도구.** `get_context`가 받는 8종은 각각 id를 찾을 도구가 있다.

| selectionKind | id를 찾는 도구 |
|---|---|
| `node` | `search_symbol`, `get_class`, `get_callers`/`get_callees` |
| `endpoint` | `get_entrypoints` |
| `class` | `list_classes` |
| `namespace` | `list_classes`(`namespace` 필터) |
| `finding` | `get_findings` |
| `writePath` | `list_write_paths` |
| `middlewareStep` | `get_middleware_pipeline` |
| `startupStep` | `get_startup_flow` |

이 표는 `McpToolContractTests`가 `[McpServerTool]` 리플렉션과 대조한다. 도구를 더하거나 파라미터를 바꾸면
이 표도 함께 고쳐야 테스트가 통과한다.

## 8. CLI 종료 코드

`tracelens impact`·`context`·`compare`는 서버를 띄우지 않고 저장된 `.tracelens/graph.json`만 읽는다.
CI·PR 훅이 종료 코드로 분기하므로 이 값들은 계약이다. `tracelens --help`와 README에도 같은 표가 있다.

| 종료 코드 | 상수 | 뜻 |
|---|---|---|
| `0` | `GraphCommands.Success` | 성공 |
| `1` | `GraphCommands.Failure` | 분석 결과를 찾지 못했거나 읽지 못했다 |
| `2` | `GraphCommands.BadArguments` | 대상·`--node` 같은 필수 인자가 없거나 값이 잘못됐다 |
| `3` | `GraphCommands.PreconditionMismatch` | 비교 전제 조건 불일치(대상·분석기 버전·규칙 버전·분석 범위) |
| `4` | `GraphCommands.StaleGraph` | 분석 결과가 현재 소스와 다르다(stale). 출력은 옛 코드 기준이다 |

- `3`은 결과를 냈지만 **비교의 전제가 다르다**는 뜻이다. "차이 없음"을 안전으로 읽으면 안 된다.
- `4`는 실패가 아니라 경고다. 출력은 정상이지만 기준이 되는 코드가 현재 소스가 아니다.
  명령이 스스로 재분석하지 않는다(CLAUDE.md 제약 3).
- 다른 명령(`analyze`·`serve`·`doctor`·`init`·`config`)은 `0`/`1`/`2`만 쓴다.

## 9. `tracelens.config.json`

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
| `db` | — | 사내 DB·캐시 계층 설정. 아래 표 참고 |

`db` 아래 키:

| 키 | 기본값 | 설명 |
|---|---|---|
| `queryApis` · `cacheApis` | Dapper·EF·StackExchange.Redis 등 | DB·캐시 호출로 볼 메서드 패턴(`*IDbConnection.QueryAsync` 같은 와일드카드) |
| `dataSources` | — | 커넥션 문자열 이름 → 표시 이름·역할 |
| `compensation` | — | 보상 처리로 볼 메서드·되돌릴 수 없는 동작 |
| `queryBuilders` | SqlKata 1종 | 쿼리 빌더의 루트(`QueryFactory.Query`)·빌더 타입·실행 종단 메서드·조인 메서드. 빌더로 만든 SQL의 테이블을 여기서 읽는다 |
| `transactionWrappers` | `[]` | 람다를 받아 그 안에서 트랜잭션을 여닫는 사내 래퍼. 적지 않아도 본문에 `BeginTransaction*`이 있고 델리게이트 파라미터를 부르면 자동으로 찾는다 |
| `cacheWrappers` | `[]` | 솔루션 안에 선언됐지만 캐시 **클라이언트**로 봐야 하는 래퍼. 적지 않으면 솔루션 안 메서드는 클라이언트로 보지 않는다(만료 인자를 판단할 수 없기 때문이다) |

`_comment`와 `$schema`는 주석용으로 허용한다. 그 밖의 모르는 키는 **경고로 알리고 무시한다**(분석을 멈추지 않는다).
값의 형식이 틀리면(숫자 자리에 문자열 등) 같은 방식으로 경고하고 기본값을 쓴다. 이 경고는 분석 결과의
`diagnostics.limitations`에도 들어가서 UI에서 볼 수 있다.

예제 설정은 [`samples/OrderApi/tracelens.config.json`](../samples/OrderApi/tracelens.config.json)에 있다.

## 10. 스키마를 바꿀 때

1. Analyzer의 `Models/`를 바꾼다(새 필드는 `init` 프로퍼티 + 기본값으로 두어 옛 문서도 읽히게 한다).
2. `GraphDocument.CurrentSchemaVersion`을 올리고 XML 주석에 무엇이 추가됐는지 적는다.
3. `TraceLens.Context`의 `GraphIndex`·`GraphJsonStore`가 새 필드를 다루게 한다.
4. Server 응답 DTO와 MCP 도구 반환을 맞춘다. 기존 클라이언트를 깨는 변경이면 **새 도구 이름이나 명시적 버전**을 쓴다.
5. client의 `types/graph.ts`·`types/context.ts`와 `normalizeGraph`를 함께 갱신한다.
6. 이 문서의 버전 이력·예제·이행 규칙을 갱신한다.
