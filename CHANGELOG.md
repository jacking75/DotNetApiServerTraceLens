# 릴리스 노트

수동으로 갱신한다. 실제로 확인한 것만 적고, 확인하지 않은 지원 범위는 넣지 않는다.

## 0.9.0 (Unreleased) — 매일 쓰는 개발자 기준 3차 (schema v7, 부가 필드만)

[docs/next-tasks-3_kr.md](docs/next-tasks-3_kr.md)의 T43~T72를 구현했다.
기준은 "매일 분석하는 개발자가 둘째 주에도 계속 쓰겠는가"다.

### 성능 — 측정값

`client/scripts/profile.mjs`(`npm run profile`)가 헤드리스 Chrome에서 React 커밋 수와 실제 HTTP 요청 수를 센다.
커밋 수는 React DevTools 훅의 `onCommitFiberRoot` 호출 횟수이고, 요청 수는 브라우저가 보낸 `/api/*` 요청이다.

측정 조건: 1600×950, `samples/OrderApi`(진입점 8 · 노드 30), `tracelens serve --port 25099`, Chrome 헤드리스.

| 조작 | React 커밋 | `/api/*` 요청 |
|---|---|---|
| 스플리터 드래그(`pointermove` 20회) | 1회(놓을 때 한 번) | 0건 |
| 진입점 선택 | 6~7회(실행마다 다르다) | 2건(`/api/source`, `/api/context`) |
| 질문 15자 타이핑 | 15회(입력 한 글자당 한 번) | **0건** |

- 드래그는 `pointermove`마다 state를 바꾸지 않는다 — 이동 20번에 커밋이 1회다.
- 타이핑은 더 이상 문맥을 다시 요청하지 않는다(예전에는 200ms 디바운스로 `POST /api/context`를 보냈다).
- 고치기 **전** 수치는 재지 않았다(코드를 이미 바꾼 뒤에 측정 도구를 만들었다). 위 값은 고친 뒤의 값이다.
- 목록은 한 번에 50개까지만 그린다(호출자·피호출자·근거·접근 지도). `DetailPanel.dom.test.tsx`가 상한을 지킨다.

### 정합성 — 버전·오류 형태·죽은 계약

- **버전이 `0.9.0`이다.** `Directory.Build.props`가 `0.8.0`인 채라 `graph.json`의 `analyzerVersion`과
  스냅샷 비교 전제에 옛 번호가 찍히고 있었다.
- **모든 400이 `{code,message}`를 갖는다.** 본문이 JSON이 아니면 ASP.NET이 본문 없는 400을 냈다.
  `ThrowOnBadRequest`를 켜고 `INVALID_REQUEST`로 감싼다(`details.reason`에 바인딩 실패 이유).
- **`/api/*`의 없는 경로가 SPA HTML 200 대신 `404 ENDPOINT_NOT_FOUND`다.**
- **지운 엔드포인트.** `GET /api/analysis-status`·`GET /api/graph/classes`는 부르는 쪽이 없었다.
  같은 내용은 `GET /api/graph` + `GET /api/freshness`, 에이전트는 MCP 도구로 얻는다.
- **쓰게 된 계약.** `GET /api/analysis-jobs`로 새로고침 뒤 **진행 중이던 분석에 다시 붙는다**(자동 재분석이
  아니라 이미 도는 작업에 붙을 뿐이다). 미들웨어 단계 상세에 `isTerminal`(종단 표시)과
  `filters`(`AddEndpointFilter`로 붙은 엔드포인트 필터, 상태 코드 포함)를 그린다.
- **계약 문서와 코드를 테스트가 대조한다.** `HttpContractTests`가 `Endpoints/*.cs`의 `Map*`과
  `data-contract.md`의 표를 양방향으로 비교한다(문서에만 있는 것·코드에만 있는 것 둘 다 잡는다).
- **`pack.ps1`이 `docs/`를 검증한다.** 설명서·설정 스키마가 빠지면 배포물 생성이 실패한다.

### i18n — 검사 범위를 `src/**`로

- `noHardcodedKorean` 검사가 `components`·`content`·`App.tsx`만 봐서 `graph/`·`api/`의 한국어가
  영어 화면에 그대로 나왔다. 검사를 `src/**`로 넓히고(사전·테스트·벤치 제외)
  `graph/`는 문장 대신 **호출자가 번역해 넘긴 라벨**을 받는다(`FlowMapLabels`, `relationLabelKeys`).
- 점검 결과 상세의 규칙 이름이 서버가 준 한국어 원문이었다. 목록과 같은 `ruleText(rule, lang)`을 쓴다.

### 구조 — App.tsx에서 훅 두 개를 떼어 냈다

상태 관리 라이브러리를 도입하지 않는 원칙은 그대로다(App이 상태를 들고 props로 내린다).
한 보기·한 흐름에 속한 것끼리 묶어 둔 것이다.

- `hooks/useCallFlowState.ts` — 호출 흐름의 진입점·파고든 자리·깊이·방향·펼친 묶음·강조 경로와 그 창.
- `hooks/useAnalysisJob.ts` — 분석 작업의 시작·진행·취소·실패·재접속.

### 고침 — 호출 흐름 캔버스가 통째로 죽던 문제

진입점을 고르는 순간 화면이 빈 채로 멈췄다. Cytoscape 스타일시트에
`background-gradient-stop-colors: data(color)`를 넣었는데, 값을 여러 개 받는 속성 안에서는 `data()`를
쓸 수 없어 파싱이 예외를 던졌다(`Cannot read properties of null (reading 'value')`).
종류별 색은 노드 클래스(`kind-<종류>`)와 리터럴 색 규칙으로 바꿨다.
CSS 변수에서 읽은 `#rrggbbaa`(번들러가 `rgba()`를 이렇게 줄인다)도 Cytoscape가 못 읽어 테두리 색이
조용히 사라지고 있었다 — 읽는 쪽에서 `rgba()`로 되돌린다.
`cytoscapeStyle.test.ts`가 실제 Cytoscape로 스타일을 파싱해 보고 경고가 하나도 없어야 통과한다.

## 0.9.0 (Unreleased) — 신입 사용성 2차 (schema v7, 부가 필드만)

[docs/next-tasks-2_kr.md](docs/next-tasks-2_kr.md)의 T20~T42를 모두 구현했다.
기준은 "신입 개발자가 혼자 설치하고, 분석하고, 결과를 읽고, AI 에이전트에게 도움을 받을 수 있는가"다.
스키마 번호는 올리지 않았다 — 아래 필드는 모두 **추가 필드**이고 옛 그래프는 빈 값으로 읽는다.

### 바뀐 동작

- **기반 클래스에 선언한 액션이 파생 컨트롤러마다 진입점으로 나온다.** 예전에는 `/api/CrudControllerBase/{id}`
  같은 **존재하지 않는 라우트**가 "확실"로 하나 나왔다. 파생이 없으면 진입점을 만들지 않고 `notes`에 사유를 남긴다.
- **`[ApiVersion]`을 읽는다.** 라우트에 `{version:apiVersion}` 토큰이 있으면 버전마다 진입점이 하나씩 나온다.
  토큰이 없으면(헤더·쿼리 버전) 라우트는 같고 `endpointId`로만 구분된다.
- **Minimal API의 표시 라우트가 소스의 대소문자를 유지한다.** `{customerId}`가 `{customerid}`로 바뀌어
  소스와 대조할 수 없던 문제를 고쳤다. ID와 비교에 쓰는 정규화 라우트는 그대로다.
- **`new TransactionScope(...)`를 트랜잭션 경계로 잡는다.** 호출식이 아니라 객체 생성이라 예전에는 경계가 통째로
  빠져 그 안의 쓰기가 "트랜잭션 없는 쓰기"로 COMP-001/002에 **확실하게 오탐**했다.
- **저장 프로시저를 Read로 단정하지 않는다.** `commandType: CommandType.StoredProcedure`가 있으면 대상은 프로시저
  이름, 연산은 `Unknown`(근거 포함)이다. Dapper `Execute*`는 쓰기 후보로 남기되 확실도를 추정으로 낮춘다.
- **클래식 `Startup.cs`(.NET Core 3.1~5)를 읽는다.** `Program.cs`와 함께 한 타임라인으로 합쳐
  시작 흐름·미들웨어 두 보기가 채워진다. 호스트를 하나도 못 찾으면 `limitations`에 사유가 남는다.
- **솔루션 안 미들웨어 확장 메서드를 펼친다.** `app.UseCompanyAuth()`는 `Extension`으로 분류하고 본문의 등록을
  하위 단계로 펼친다(깊이 2). "본문은 어셈블리 안에 있어 열어 볼 수 없다"는 실제로 솔루션 밖일 때만 쓴다.
- **`tracelens demo`의 기본 복사 위치가 `%LOCALAPPDATA%\TraceLens\samples`다.** 설치 폴더에서 실행해도
  권한 오류가 나지 않는다. `--dir`은 그대로 존중한다.
- **DB·캐시 보기의 기본 하위 탭이 "실행 경로"다**(예전에는 가장 어려운 "보상 처리"였다).
  이미 저장해 둔 사용자의 선택은 그대로 둔다.
- **진단이 실제 바인딩 포트를 검사한다.** `--port 25099`로 띄운 서버가 25011에 대해 `PORT_IN_USE`를 말하던
  틀린 정보를 고쳤다.

### 추가 — 결과에 없는 것을 말한다

- `diagnostics.calls.externalByNamespace` — 솔루션 밖 호출을 네임스페이스로 묶은 상위 20개. 알려진
  프레임워크(EF Core·MediatR·Mongo·NHibernate·MassTransit·Kafka·Hangfire·Quartz·gRPC·SignalR·System.Transactions)는
  `limitations`에 한 줄씩 승격된다. 엣지·해석률 분모는 바뀌지 않는다.
- `diagnostics.entryPointCandidates` — HTTP 밖 실행 단위(HostedService·MessageConsumer·SignalRHub·GrpcService·
  ScheduledJob·AzureFunction·HealthCheck·ConventionalRouting)를 종류별 개수와 이름 3개로. 진입점·엣지를 만들지 않는다.
- `dataAccess.notes` — 접근 0건인데 ORM 타입이 있거나 `db.queryApis`가 기본값일 때 그 사실. 규칙 결과는 바꾸지 않는다.
- 해석률 문장을 UI·CLI·문맥·MCP에서 "해석 N/M · 외부 호출 K는 분모에서 뺌"으로 통일했다.

### 추가 — 화면

- **분석 진단 패널.** 프로젝트별 로드 결과·컴파일 오류 첫 3건·호출 해석 내역·범위·한계·분석 중 변경을 한 화면에.
  분석 뒤 빈 보기는 "분석하면 보인다"가 아니라 "찾지 못했다 + 다음 행동"을 말한다.
- **도움말(`?`).** 용어집 12항목·단축키·실무 절차. 헤더의 "사용 설명서"는 `/manual/ko`·`/manual/en`을 연다
  (배포물에도 들어간다). 해석률·신선도·호출 근거·경계·보상 처리·단락·MCP 옆에 `?` 링크가 있다.
- **첫 사용 안내가 분석 전에도 열린다.** 첫 방문에 자동으로 열리고, localStorage 복원만으로는 단계가 완료되지 않는다.
- **실패에 다음 행동이 있다.** 분석 실패를 `SDK_NOT_FOUND`·`RESTORE_FAILED`·`FILE_LOCKED`·`TARGET_NOT_FOUND`로
  분류해 한 줄 요약·환경 점검·대상 다시 고르기를 붙이고 원문은 접는다. AI CLI가 없으면 보내기 전에 안다.
- **질문 패널.** 목적별 프리셋 질문 칩, 목적·길이 설명, 답변 복사, 최근 답변 5개 보관(새로고침에는 남기지 않는다).
- **MCP 설정.** 호스트 4개(Claude Code·Codex CLI·VS Code·Cursor) 탭, 복사 버튼, 붙여넣기용 원라이너(경로를 항상
  따옴표로 감싼다), 검증 질문, 대상 저장소 `CLAUDE.md`/`AGENTS.md`에 붙일 에이전트 안내문.
- **`Ctrl+K` 통합 찾기**와 메서드 이름 검색. 진입점 목록은 컨트롤러별로 묶이고 20개를 넘으면 접힌다.
- **화면의 모든 `파일:줄`이 눌린다.** 코드 보기가 그 구간으로 이동하고, 호출 지점 줄에 여백 표시가 찍힌다.
  표시를 누르면 그 호출의 대상 노드로 간다. Shift+클릭은 에디터에서 연다.
- **라이트 테마·글꼴 3단계·좌우 패널 접기.** 배너는 한 스택(최대 30vh)으로 묶여 작업 영역을 밀지 않는다.
- **브레드크럼이 모두 동작한다**(되돌아갈 자리가 없으면 비활성). URL 해시로 화면을 재현하고 뒤로 가기가 그래프 창을
  되돌린다. 최근 대상 5개를 드롭다운으로 고른다.

### 추가 — CLI·설정

- `tracelens init` — `$schema`가 붙은 기본 설정을 만든다. 이미 있으면 덮어쓰지 않고 모르는 키와 기본값과 다른 키만 알린다.
- `tracelens config --show` — 기본값과 파일을 합친 유효 설정 전체. `doctor --json`에도 `effectiveConfig`로 들어간다.
- `docs/tracelens.config.schema.json` — 편집기 자동완성용. 코드의 키 집합과 어긋나면 테스트가 실패한다.
- `excludeProjectNamePatterns` — 테스트 프로젝트를 로드에서 뺀다. 뺀 프로젝트는 진단에 `Loaded=false`로 남는다.
- `doctor` 새 코드: `SDK_TOO_OLD`·`TFM_NOT_INSTALLED`·`MSBUILD_REGISTERED`·`OS_UNVERIFIED`·`NUGET_UNREACHABLE`.
- `run.ps1 -Port`와 서버 `--port`를 이었다. `analyze`는 `.gitignore` 팁과 테스트로 보이는 프로젝트 수를 알린다.

### MCP

- 도구 열두 개의 설명에 "언제 이걸 쓰고 언제 다른 걸 쓰나"를 한 문장씩(한·영 병기) 붙였다.
- `get_analysis_status`에 `suggestedNextTools`와 `dataAccessNotes`를 넣었다. `suggestedNextTools`는 **제안이지 보장이 아니다**.
- 인앱 실행의 허용 도구 목록을 `[McpServerTool]` 특성에서 리플렉션으로 만든다. 불일치는 테스트가 잡는다.

### 문서

- README가 **존재하지 않는 배포 채널을 전제하지 않는다.** 내려받을 곳이 없다는 사실을 먼저 적고 `pack.ps1`이나
  소스 실행을 안내한다. 퀵스타트는 `tracelens`가 배포물인지 소스인지 밝힌다.
- README·매뉴얼·첫 화면의 지원 범위에 **"지금 읽지 않는 것"**을 이름으로 적고, 그것이 진단에 어떻게 남는지 함께 적었다.
- 같은 대상을 두 인스턴스에서 동시에 분석하지 말라는 주의와 종료 방법, `pack.ps1` 검증 절차의 공백·한글 경로 단계를 추가했다.

### 확인한 것

빌드 경고 0. .NET 테스트 210개(Analyzer 89 · Server 112 · Mcp 9), 클라이언트 테스트 96개 통과.
새 픽스처 두 개(`FixturesExternal`·`FixturesClassic`)로 외부 호출 집계·HTTP 밖 실행 단위·클래식 호스트·
기반 클래스 액션·`[ApiVersion]`의 회귀를 막는다. macOS·Linux는 여전히 확인하지 않았다.

## 0.8.0 (Unreleased) — 실제 프로젝트 점검 반영 (schema v7)

버전은 `Directory.Build.props`의 `<Version>` 한 곳에서 바꾼다(`pack.ps1 -Version`이 덮어쓴다).
이 값이 `graph.json`의 `analyzerVersion`과 스냅샷 비교 전제 조건에 그대로 들어간다.

[docs/next-tasks_kr.md](docs/next-tasks_kr.md)의 태스크를 구현했다.
실제 사내 프로젝트(ASP.NET Core, SqlKata + CloudStructures) 1개를 기준선으로 전후를 측정했다.

### 바뀐 동작 (이전 그래프와 수치가 달라진다)

- **람다 본문 안의 호출을 그래프에 포함한다.** 트랜잭션 래퍼·`Task.Run`·LINQ 람다 안의 호출이
  감싸는 메서드의 호출로 잡힌다. 확실도는 **추정**이고 근거에 "람다 본문"과 그 람다를 받는 메서드가 남는다.
  예전에는 이 호출들이 엣지에도 경계 목록에도 남지 않아 해석률 분모에서 빠져 있었다.
  (실제 프로젝트: 고립 노드 118/732 → 92/765, 검사한 호출 1141 → 1544)
- **쿼리 빌더(SqlKata)로 실행하는 DB 접근을 인식한다.** 테이블 이름이 상수면 확실, 변수면 `?`에 추정이다.
  (실제 프로젝트: DB 접근 0 → 248, 트랜잭션 2 → 32)
- **솔루션 안에 선언된 캐시 래퍼를 캐시 클라이언트로 보지 않는다.** 만료 인자를 판단할 수 있는
  실제 클라이언트 호출에서만 CACHE-003을 낸다. 내부 래퍼를 클라이언트로 보려면 `db.cacheWrappers`에 적는다.
  (실제 프로젝트: CACHE-003 36 → 10, COMP-002 22 → 19)
- **`MapControllers()` 같은 엔드포인트 등록은 터미널이 아니다.** MW-012는 `UseEndpoints`/`app.Run(delegate)`
  뒤에 등록한 미들웨어에만 난다. (실제 프로젝트: MW-012 3 → 0 — 인증 미들웨어 오판이 사라졌다)
- **`.csproj` 대상의 저장된 분석 결과를 조상 폴더까지 찾는다.** 분석기가 쓰는 위치(공통 상위 폴더)와
  복원이 읽는 위치가 달라 재시작 뒤 404가 나던 문제를 고쳤다.
- **같은 대상의 두 번째 분석 작업은 409 `JOB_ALREADY_RUNNING`으로 거절한다.** 예전에는 둘 다 돌고 나중 것이 이겼다.
- **`.tracelens/mcp.json`을 통째로 덮어쓰지 않는다.** `mcpServers.tracelens`만 갈아 끼운다.

### 성능 — 측정 조건을 함께 적는다

측정 기준은 두 가지다.

- **분석기**: 실제 사내 프로젝트(ASP.NET Core, 프로젝트 3개, `.cs` 126개, 클래스 564개, 메서드 839개)를
  `tracelens analyze <csproj> --profile`로 3회 연속 실행. Windows 11, i7-12700KF, .NET 8, 파일 캐시가 더워진 상태.
  전후 모두 결과물은 같다(클래스 564 · 메서드 839 · 엣지 1291 · 해석률 1700/1702).
- **클라이언트 배치**: `npm run measure:layout`(`vitest bench`, Node 22.17.1, 같은 기계).
  합성 입력이다 — 클래스 564개에 상속·연관·의존 관계를 실제 비율(컨트롤러 1 : 서비스 3 : 저장소 2 : 모델 4)로 흉내 냈다.

| 구간 | 전(3회) | 후(3회) | 비고 |
| --- | --- | --- | --- |
| analyze 전체 | 9421 · 9446 · 10694ms | 6849 · 7049 · 7239ms | 아래 단계 합계 |
| load(MSBuild + 시맨틱 모델 준비) | 4077 ~ 4963ms | 4047 ~ 4150ms | 전체의 57~60%. 여기가 가장 크고 MSBuild가 대부분이다 |
| entrypoints | 88 ~ 142ms | 80 ~ 104ms | |
| scan | 2255 ~ 2780ms | 1812 ~ 2110ms | |
| rules(DB·캐시 + 미들웨어) | 2195 ~ 2370ms | 352 ~ 412ms | 문서를 다시 바인딩하지 않게 되면서 가장 크게 줄었다 |
| save(graph.json 4077KB) | 531 ~ 536ms | 492 ~ 589ms | |
| 최대 작업 집합 | 259 ~ 263MB | 263 ~ 264MB | 프로세스 전체 값이다. 분석만의 값이 아니다 |
| `layoutTree` 클래스 150개 | — | 평균 1.21ms | |
| `layoutTree` 클래스 564개 | — | 평균 15.6ms (p99 25.2ms) | 배치는 병목이 아니다 |
| `cullToViewport` 클래스 564개 | — | 평균 0.0045ms | |

- **`tracelens analyze --profile`을 추가했다.** 단계별 시간과 최대 작업 집합을 출력한다.
  최대 작업 집합은 프로세스 전체 값이라 분석만의 사용량이 아니다 — 출력에도 그렇게 적는다.
- **구문 트리와 시맨틱 모델을 문서마다 한 번만 만든다.** 진입점·DI·타입·DB/캐시·미들웨어 수집기가 각각
  `GetSemanticModelAsync`를 부르면서 같은 문서를 여러 번 바인딩하고 있었다(Roslyn의 캐시는 약한 참조라 그 사이에 버려진다).
  이제 분석 시작 직후 `Parallel.ForEachAsync`로 한 번 만들어 모든 수집기가 나눠 쓴다.
  문서가 3000개를 넘으면 메모리 때문에 준비 단계를 건너뛴다 — **결과는 같고 시간만 예전으로 돌아간다.**
- **Minimal API 등록자 해석을 색인으로 바꿨다.** 등록마다 전체 등록을 훑던 것을 대상 이름별 사전으로 바꿨다.
  기준 프로젝트에는 Minimal API 진입점이 0개라 **이 프로젝트에서는 효과를 측정하지 못했다.**
  등록이 많은 프로젝트에서만 의미가 있다.
- **클래스 다이어그램·전체 흐름도가 보이는 박스만 그린다.** 스크롤 사각형(문서 좌표) 밖 박스를 DOM에서 뺀다.
  여백은 600px이고, 스크롤이 80px 이내로 바뀌면 다시 그리지 않는다.
  합성 입력 기준으로 100% 줌·1600x900 창에서 **564개 중 11개**만 그린다.
  다만 **"맞춤" 줌에서는 캔버스 전체가 화면에 들어오므로 컬링이 줄여 주는 것이 없다.** 그때는 멤버 접기·관계 토글이 손잡이다.
- **효과가 없었지만 남긴 것**: 시작 흐름 스캐너와 DB·캐시 스캐너를 `Task.WhenAll`로 동시에 돌렸다.
  이 변경만 놓고 재면 rules 단계는 2163ms → 2125~2496ms로 실행 간 편차 안이었다. **측정 가능한 개선이 없었다.**
  rules가 실제로 줄어든 이유는 병렬화가 아니라 위의 시맨틱 모델 공유다.
- **손대지 않은 것**: load 단계(MSBuild 워크스페이스)가 여전히 전체의 절반을 넘는다. 여기는 MSBuild 쪽 비용이라
  이번 범위에서 줄이지 않았다.

### 추가

- 첫 화면의 "예제 분석" 버튼(`GET /api/samples`, `POST /api/samples/{id}/prepare`).
- 공유 보고서 모달, 가져온 분석의 원본 루트 연결, 문맥 미리보기의 파일별 제외.
- `/api/ask`의 `timeoutSeconds`와 `DELETE /api/ask/{runId}`.
- 롤링 파일 로그(`%LOCALAPPDATA%\TraceLens\logs`)와 500 응답의 `errorId`.
- 상태를 바꾸는 `/api/*` 요청의 Origin 검사(`FORBIDDEN_ORIGIN`).
- 영어 UI 완성과 하드코딩 한국어 회귀 테스트.
- 테스트: .NET 93 → 171개(Analyzer 72 · Server 90 · Mcp 9), 클라이언트 41 → 71개.
- **규칙 카탈로그 42개의 영어 문구**(`client/src/i18n/ruleText.ts`, 규칙 ID로 대응).
  분석기가 규칙을 늘리면 `ruleText.test.ts`가 분석기 소스에서 ID를 읽어 대조해 실패한다.
  개별 점검 결과(what·why·fix)는 번역하지 않는다 — 분석할 때 실제 심볼·테이블 이름을 섞어 만드는
  문장이라 미리 번역할 수 없다. 영어 화면에서도 분석기가 만든 그대로 보여 주고 번역한 척하지 않는다.
- `GET /api/graph` 계열과 `/api/browse`의 실패 응답에도 `code`를 담는다(옛 `error` 필드는 그대로).
- **`pack.ps1 -Runtimes`** — RID별 portable ZIP을 각각 만들고 SHA256을 manifest에 남긴다
  (`win-x64`·`win-arm64`·`linux-x64`·`linux-arm64`·`osx-x64`·`osx-arm64`). **빌드만 한다** —
  ZIP을 만든 것과 그 OS를 지원하는 것은 다르므로 README 지원 표는 그대로 두고,
  manifest에도 그 문장과 함께 "Windows에서 만든 linux/osx ZIP은 실행 권한이 보존되지 않는다"를 적는다.
- `pack.ps1 -SkipClient`가 `index.html`의 존재만 보지 않고 **빌드 시각**을 클라이언트 소스와 비교한다.
  wwwroot가 더 오래됐으면 옛 UI가 배포물에 들어가므로 거기서 멈춘다.

### 문서

- README·README_kr: 해석 종류 표에 `External`(해석률 분모에서 빼는 이유 포함), CLI 표에 `--no-browser`·
  `--profile`, `.csproj` 대상의 결과 저장 위치, 규칙 수를 "42개 중 기본 실행 26개(꺼짐 12 · 샤드 전용 4)"로,
  `⊕` 확장이 **2홉**이라는 사실과 창 상한 120개, 작업 목적 네 가지와 다섯 번째 `Share`를 목록에 두지 않는 이유.
- `docs/data-contract.md`: 부가 필드 표(`edges[].confidence`·`callSites[].viaLambda`·`manifest.ignoredSources`·
  `dataAccess.accesses[].confidence/note/transactionWrapper`·`transactions[].kind/wrapperName`·
  `middleware.steps[].kind`·`startupFlows[]`·`middlewarePipelines[]`), 새 엔드포인트와 오류 코드 전체,
  `db.queryBuilders`·`transactionWrappers`·`cacheWrappers`, 모르는 설정 키 처리.
- 매뉴얼(한·영): 설정 표를 실제 키와 맞추고, 미들웨어 단계의 세 종류(미들웨어 · 엔드포인트 등록 · 종단)와
  쿼리 빌더를 어디까지 읽는지(못 읽는 경우 포함)를 적었다.

---

## Unreleased — 보급·바이브 코딩 개선 (schema v7)

[docs/adoption-and-vibe-coding-roadmap_kr.md](docs/adoption-and-vibe-coding-roadmap_kr.md)의 A~I를 구현했다.

### 추가

- **Minimal API 진입점** — `MapGet/MapPost/MapPut/MapDelete/MapPatch/MapMethods/Map`, `MapGroup` 접두사(중첩 포함),
  메서드 그룹·람다·로컬 함수 핸들러, 확장 메서드 안에 숨은 등록(깊이 4까지). 이름만 같은 사용자 메서드는 제외한다.
- **엔드포인트 ID** — 핸들러 노드 ID와 분리했다. 같은 메서드에 라우트가 여러 개면 각각 다른 진입점이 된다.
- **호출 근거(`callSites`)** — 엣지마다 실제 호출 줄과 해석 종류(직접/DI 등록/유일 구현체/모호/미해석)를 남긴다.
  확정하지 못한 호출은 "경계"로 따로 보여 준다.
- **분석 진단(`diagnostics`)** — 프로젝트 로드 결과, 컴파일 오류 수, 호출 해석률(분모 정의 포함), 깊이 제한 도달, 한계 목록.
- **분석 입력 목록(`manifest`)** — 어떤 프로젝트·설정 파일을 읽었는지와 소스 루트. 신선도가 **새로 생긴 `.cs`와 설정 변경**까지 잡는다.
- **snapshotId·inputFingerprint** — UI·API·MCP가 같은 분석을 보고 있는지 확인한다. 다르면 `SNAPSHOT_MISMATCH`로 알린다.
- **`TraceLens.Context`** — 색인·문맥·영향·비교·소스 접근 정책을 Server와 MCP가 함께 쓰는 라이브러리로 분리했다.
- **AI 작업 문맥** — `POST /api/context`가 근거·경로·소스·한계를 담은 DTO와 Markdown을 만든다.
  클립보드·인앱 실행·MCP가 모두 이 하나를 쓴다. 목적(4종)과 길이(3종)를 고를 수 있고, 미리보기에서 포함 파일·소스 포함 여부·길이·생략 항목을 확인한다.
- **변경 영향** — 메서드에서 역방향으로 도달하는 HTTP 진입점 후보와 근거 경로. 상한과 한계를 함께 돌려준다.
- **분석 전후 비교** — 기준 저장(`.tracelens/history/<snapshotId>/`)과 재분석 후 비교. 메서드 본문 지문으로 서식만 바뀐 변경을 구분하고, 규칙이 꺼져 사라진 점검은 "비교 불가"로 표시한다.
- **MCP 도구 6개 추가** — `get_analysis_status`, `get_context`, `get_entrypoint_flow`, `get_findings`, `get_impact`, `compare_snapshots`.
  기존 6개 도구에는 페이지 구분(`limit`/`cursor`)과 스냅샷 확인을 더했다.
- **`TraceLens.Cli`** — `tracelens doctor|analyze|serve|mcp|demo`. .NET tool 패키지와 Windows portable ZIP으로 배포한다.
- **`pack.ps1`** — 배포물을 만들고 체크섬·버전·수동 검증 절차를 `artifacts/manifest.txt`에 남긴다(게시는 하지 않는다).
- **첫 화면과 안내** — 분석 전 빈 화면 대신 예제·내 프로젝트·지원 범위·레시피를 보여 준다. 첫 사용 안내는 실제 완료 이벤트로 진행한다.
- **영어 UI** — 한국어 사전이 원본이고 영어 사전은 그 키로 타입이 제한된다. 버튼·오류·빈 화면·`aria-label`·날짜 표시에 적용했다.
- **환경 점검(doctor)** — SDK/MSBuild, 대상 형식, 저장 권한, 포트, MCP 호스트, 선택적 CLI를 확인하고 다음 행동을 알려 준다. 설치·설정 변경은 하지 않는다.
- **분석 작업 API** — `POST /api/analysis-jobs`로 단계별 진행 상태(SSE)와 취소를 지원한다. 취소해도 기존 그래프는 유지된다.
- **공유용 Markdown 보고서** — 선택 범위만 담고 절대 경로와 소스 본문을 제외한다.
- **예제 프로젝트** — `samples/OrderApi`. 컨트롤러 4개 라우트와 Minimal API 4개 라우트, 트랜잭션 없는 4단계 쓰기 경로를 담았다.

### 바뀐 동작

- 진입점을 노드 ID로 중복 제거하지 않는다. 같은 메서드의 여러 라우트가 모두 보인다.
- 심볼이 모호할 때 첫 후보를 확정 호출로 채택하지 않는다. 모호로 기록하고 엣지를 만들지 않는다.
- DI 등록은 이름만 보지 않고 실제 심볼이 `IServiceCollection` 확장인지 확인한다. keyed·조건부·복수 등록은 확정하지 않는다.
- 람다·로컬 함수 본문의 호출을 부모 메서드의 직접 호출로 잡지 않는다.
- 신선도에 `unknown`이 생겼다. 신선도 입력이 없는 옛 분석은 `fresh`라고 단정하지 않는다.
- 가져온 분석 결과는 `offlineSnapshot`으로 취급한다. 원본 루트를 연결해 입력이 일치할 때까지 로컬 소스를 읽지 않고, 대상 폴더의 `graph.json`도 덮어쓰지 않는다.
- `graph.json`은 임시 파일에 쓰고 다시 읽어 검증한 뒤에 교체한다. 같은 대상의 동시 분석은 직렬화한다.
- 질문 패널 이름을 "Claude에게 질문"에서 "AI에게 질문"으로 바꾸고, 실행 방식을 실제 동작(문맥 복사 / Claude Code로 실행 / 에디터 열기)으로 표시한다.

### 알려진 제한

- `.slnx`는 지금 쓰는 MSBuildWorkspace가 열지 못해 지원하지 않는다. doctor가 이유를 알려 준다.
- macOS·Linux는 확인하지 않았다. 지원 표에 넣지 않는다.
- 동적 라우트, reflection, 이벤트 구독, 메시지 핸들러 연결은 확정하지 않는다.
- 사용자 관찰(§16)과 공개 게시는 아직 하지 않았다. 관련 목표 수치는 모두 "초기 목표"이며 측정 결과가 아니다.
