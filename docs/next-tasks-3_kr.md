# TraceLens 기능·UX 점검과 다음 작업 (3차)

작성 기준: 2026-09-18 20:12 KST, 커밋 `2f8f4f3` (T20~T42 완료 직후).
이 문서는 [`next-tasks-2_kr.md`](next-tasks-2_kr.md)의 T20~T42를 모두 끝낸 상태에서 **"실제 50~300개 엔드포인트짜리 ASP.NET Core 솔루션을 매일 분석하는 개발자가 계속 쓰겠는가"**를 기준으로 기능과 UX를 다시 점검한 결과다.
점검 방법은 세 갈래다. (1) **실제로 켜서 봤다** — `tracelens serve`로 `samples/OrderApi`를 띄우고 헤드리스 Chrome(1600×950, 1366×768)으로 여섯 보기·대화상자·라이트/다크 테마를 스크린샷으로 확인했다. (2) 클라이언트(`client/src`)·분석기(`src/TraceLens.Analyzer`)·서버/문맥/MCP/CLI를 코드로 읽었다. (3) 2차 문서 §2.4의 미지원 표를 현재 코드와 대조해 무엇이 고쳐졌고 무엇이 남았는지 갈랐다.
근거는 `파일:줄`로 적고, **실행으로 확인한 것**과 **코드 읽기로 추론한 것**을 구분했다.

> **사용 규칙.** 태스크 하나를 끝낼 때마다 아래 목록의 체크박스를 즉시 `[x]`로 바꾸고 완료 일시(KST, 시스템 명령으로 확인)를 덧붙인다.
> 여러 개를 몰아서 체크하지 않는다. 상세 절의 "완료 기준"을 모두 만족했을 때만 체크한다.
> 번호는 2차 문서(T20~T42)에 이어 **T43부터** 시작한다.

---

## 태스크 목록

### P0 — 매일 쓰면 바로 걸리는 버그 (실행으로 확인)

- [x] [T43. 진입점 목록 — 접힘 논리 반전을 고치고 세 벌을 하나로 합친다](#t43) — 2026-09-19 19:25 KST
- [x] [T44. 라이트 테마를 완성한다 — 어두운 상자 위의 안 보이는 글자](#t44) — 2026-09-19 19:25 KST
- [x] [T45. URL 상태를 시작할 때 읽고, 링크 복사와 히스토리 규칙을 정한다](#t45) — 2026-09-19 19:25 KST
- [x] [T46. 선택을 바꿔도 따라오지 않는 상태 네 가지](#t46) — 2026-09-19 19:25 KST
- [x] [T47. DB·캐시/미들웨어 상세에 코드 보기를 붙이고 `파일:줄` 동작을 통일한다](#t47) — 2026-09-19 19:25 KST
- [x] [T48. 환경 점검 모달 — 자기 포트 오탐·줄 붙음·Esc](#t48) — 2026-09-19 19:25 KST
- [x] [T49. 통합 찾기가 막다른 길로 보내지 않게 한다](#t49) — 2026-09-19 19:25 KST

### P1 — 호출 흐름 보기의 읽기 품질

- [x] [T50. 호출 흐름 노드·엣지·맞춤·컨트롤](#t50) — 2026-09-19 19:25 KST
- [x] [T51. 1단계 팬아웃 상한과 호출자 방향 창](#t51) — 2026-09-19 19:25 KST
- [x] [T52. 헤더·툴바·우측 열이 1600px에서 넘치지 않게 한다](#t52) — 2026-09-19 19:25 KST

### P1 — 분석기 정직성·정확도

- [x] [T53. `base.Method()`가 자기 자신으로 가는 엣지를 만든다](#t53) — 2026-09-19 19:25 KST
- [x] [T54. 진입점 네 가지 — 메서드 `[Route]`·HEAD/OPTIONS·비추상 기반 컨트롤러·`IgnoreApi`](#t54) — 2026-09-19 19:25 KST
- [x] [T55. DB·캐시 notes 조건과 프로퍼티 본문 스캔](#t55) — 2026-09-19 19:25 KST
- [x] [T56. 트랜잭션 범위·호출자 전파 — COMP 오탐](#t56) — 2026-09-19 19:25 KST
- [x] [T57. 시작 흐름·미들웨어 표시의 정보 손실](#t57) — 2026-09-19 19:25 KST
- [x] [T58. 진단에 남지 않는 절단 다섯 가지](#t58) — 2026-09-19 19:25 KST
- [x] [T59. 테스트 프로젝트의 구현체가 유일 구현체 판정을 흔든다](#t59) — 2026-09-19 19:25 KST

### P1 — MCP·CLI·자동화

- [x] [T60. `get_findings` 규칙 접두사 버그와 MCP 오류 계약·로그](#t60) — 2026-09-19 19:25 KST
- [x] [T61. MCP 호출 비용과 응답 크기](#t61) — 2026-09-19 19:25 KST
- [x] [T62. UI에는 있고 MCP에는 없는 것 + 도구 계약 문서](#t62) — 2026-09-19 19:25 KST
- [x] [T63. 스크립트용 CLI 명령(`impact`·`context`·`compare`)과 종료 코드](#t63) — 2026-09-19 19:25 KST
- [x] [T64. 문맥·영향·비교의 내용 공백](#t64) — 2026-09-19 19:25 KST

### P2 — 매일 쓰는 편의

- [ ] [T65. 실행 프로젝트가 여럿인 솔루션](#t65)
- [ ] [T66. stale·서버 다운·취소를 화면이 말하게 한다](#t66)
- [ ] [T67. 성능 — 드래그 리렌더·타이핑마다 재요청·전량 스캔·가상화](#t67)
- [ ] [T68. 접근성 — 포커스 표시·모달 트랩·단축키 목록](#t68)
- [ ] [T69. 내보내기·북마크·다중 대상 영향](#t69)
- [x] [T70. 인앱 실행 에이전트 확장과 MCP 호스트 추가](#t70) — 2026-09-19 19:25 KST
- [x] [T71. MVC 필터·`[Authorize]` 정책을 미들웨어 보기에 표시한다](#t71) — 2026-09-19 19:25 KST
- [ ] [T72. 정합성 묶음 — 버전·오류 형태·죽은 계약·i18n 검사 범위·문서](#t72)

권장 순서: **T43 → T44 → T46 → T45 → T48** (하루 안에 체감) → **T50 → T51 → T52** (호출 흐름) → **T53 → T54 → T56 → T55 → T57 → T58 → T59** (정직성) → **T60 → T61 → T62** (에이전트) → 나머지.
T47은 T46과 같은 파일(`App.tsx`·`DetailPanel.tsx`)을 건드리므로 이어서 한다. T64는 T62의 도구 추가와 응답 형태를 공유하므로 T62 뒤에 한다.

---

## 1. 판정: 매일 쓰는 개발자가 계속 쓰겠는가

**첫 주는 쓰지만 둘째 주에 이탈할 위험이 크다.** 신입 온보딩(T20~T42)은 잘 됐고 정직한 문구 원칙도 화면 전반에 살아 있다. 그러나 **매일 반복하는 조작**에서 걸리는 것이 많다.

| 매일 하는 일 | 지금 상태 | 근거 |
|---|---|---|
| 진입점 목록에서 하나 고르기 | **미달.** 컨트롤러 그룹이 항상 접혀 있어 매 세션 그룹을 열어야 하고, 고른 항목이 접힌 그룹 안에 있으면 무엇을 골랐는지 목록에서 안 보인다 | 실행 확인(스크린샷). `SidePanels.tsx:79,105`의 `open = expanded.has(id) !== collapsedByDefault`가 주석("목록이 짧으면 펼침이 기본")과 반대로 동작한다 |
| 라이트 테마로 보기 | **미달.** 클래스 다이어그램의 클래스 이름, DB·캐시 실행 경로의 메서드 이름, 미들웨어 단계 이름, 도움말의 강조 항목 제목이 **보이지 않는다** | 실행 확인. `styles.css`에 `background: #0…/#1…` 하드코딩이 101곳, 라이트 테마 재정의는 21개 선택자뿐 |
| 동료에게 화면 링크 보내기 | **미달.** 해시를 쓰기만 하고 읽지 않는다. `#view=data`로 열면 `#view=callflow`로 바뀐다 | 실행 확인. `App.tsx:390`의 `parseUrlState`는 popstate에서만 불린다 |
| 노드를 바꿔 가며 상세·소스 보기 | **미달.** `파일:줄`을 한 번 누르면 이후 다른 메서드를 골라도 상세의 소스가 **이전 파일**에 머문다. 영향 결과도 이전 메서드 것이 남는다 | `DetailPanel.tsx:336,364`, `ImpactPanel.tsx:23`(초기화 없음) |
| 호출 흐름을 읽기 | **부분.** 노드가 빈 색 상자이고 이름은 위에 작은 글씨, 엣지에 화살표가 없고, 파고든 뒤 화면이 맞춰지지 않는다. 컨트롤러 액션이 30개 서비스를 직접 부르면 1단계부터 상한 없이 그린다 | 실행 확인(스크린샷). `windowing.ts:42-71` |
| AI 에이전트가 MCP로 묻기 | **부분.** 안내문이 시키는 `get_findings(ruleId=COMP-*)`가 **0건**을 돌려준다(정확 일치). 미들웨어·시작 흐름·클래스는 MCP에서 볼 수 없다. 도구 호출마다 솔루션 전체를 다시 해시한다 | `TraceLensTools.cs:226` vs `McpConfigWriter.cs:59`, `TraceLensTools.cs:33,113` |
| 스크립트·CI에서 쓰기 | **미달.** `impact`·`context`·`compare` CLI 명령이 없고 종료 코드가 문서에 없다 | `CommandRouter.cs:24-36` |
| 분석 결과를 믿기 | **부분.** `base.Foo()`가 자기 순환 엣지로, 메서드 `[Route]`가 "확실"로 틀린 라우트로, 항상 트랜잭션 안에서만 불리는 리포지터리가 COMP-001로 나온다. EF Core+Dapper 혼용이면 "EF Core는 안 읽었다"가 사라진다 | `InvocationResolver.cs:111-144`, `EntryPointScanner.cs:74-88`, `CompensationAnalyzer.cs:113-120`, `DataRuleEngine.cs:450-453` |

## 2. 이번 점검에서 확인한 사실

### 2.1 이미 잘 돼 있어 손대지 않는 것

- 정직한 문구 원칙: 신선도 3상태와 "옛 그래프를 fresh로 단정하지 않음", 규칙 카탈로그의 "꺼짐" 유지, 영향 목록의 "후보·한계" 표기, 해석률의 분모 표기, `analyze` 요약의 실패 프로젝트·한계 전부 출력.
- 오류 처리 형태: `{code,message,details}` → `translateError` → 한 줄 요약 + 원문 접기 + errorId·로그 경로. 분석 실패 분류(`SDK_NOT_FOUND` 등)와 다음 행동.
- `graph/` 순수 함수 + 단위 테스트 분리, i18n 안전망 3종(미사용 키·한국어 하드코딩·ko/en 동치), 뷰포트 컬링과 스로틀, SSE 파서 순수 함수화, 인앱 실행 중단 시 프로세스 트리 정리.
- `SourceAccessPolicy`(상대 경로·스냅샷 파일 목록·루트 밖·심볼릭 링크 최종 대상·가져온 그래프 차단)와 테스트 9건. `GraphJsonStore`의 미래 schema 거절·원자적 쓰기·재파싱 검증.
- `GraphFreshnessService`의 5초 TTL 캐시와 in-flight 병합. `AnalysisJobService`의 같은 대상 중복 거절과 취소 후 기존 그래프 유지.
- 분석기: 2차 문서 §2.4의 11개 항목 중 8개가 실제로 고쳐졌다(`[ApiVersion]`, 클래식 `Startup.cs`, 솔루션 안 MW 확장, `TransactionScope`, Dapper 저장 프로시저, External 집계, HTTP 밖 실행 단위, 추상 기반 클래스 액션). 수집만 하고 안 쓰는 진단 데이터는 찾지 못했다 — `DiagnosticsPanel.tsx`가 다 그린다.
- `TODO/FIXME/HACK`·`console.*`·`NotImplementedException`이 저장소 전체에 0건.

### 2.2 실행으로 확인한 버그 (스크린샷 근거)

`tracelens serve --target samples/OrderApi --port 25099`, 헤드리스 Chrome, 저장된 그래프를 `/api/analyze/load`로 불러온 상태.

| # | 본 것 | 어디서 |
|---|---|---|
| E1 | 호출 흐름 좌측 진입점 그룹 3개가 **모두 접혀** 있고, 전체 흐름도 좌측의 같은 목록은 펼쳐져 있다(다른 컴포넌트) | 호출 흐름 첫 화면 |
| E2 | 라이트 테마에서 UML 상자의 클래스 이름, DB·캐시 실행 경로 카드의 `OrderCache.Get`·`필드 cache(추정)`, 미들웨어 단계 카드의 `RequestLogMiddleware`, 도움말 용어집의 강조된 "신선도" 제목이 **어두운 배경 위 어두운 글자**로 안 보인다 | 클래스 다이어그램·DB·캐시·미들웨어·도움말 |
| E3 | 새 탭에서 `#view=data`로 열면 `#view=callflow`로 바뀌고 호출 흐름이 뜬다. `#view=callflow&entry=…`도 진입점이 선택되지 않는다 | URL 직접 입력 |
| E4 | 통합 찾기(`Ctrl+K`)에서 `ReserveAsync`를 고르면 `IInventoryRepository.ReserveAsync`(인터페이스 멤버)가 먼저 나오고, 고르면 상세에 **"이 메서드는 마지막 분석 결과에 없다"** | 통합 찾기 |
| E5 | 헤더 오른쪽 끝 "분석 전후 비교" 버튼이 1600px에서 **잘려** "분"만 보인다. 클래스 다이어그램 툴바는 두 줄로 꺾인다. 헤더의 "ASP.NET CORE 호출 탐색기" 부제가 "탐/색기"로 줄바꿈된다 | 모든 보기 |
| E6 | 호출 흐름 노드는 **빈 색 상자**이고 이름은 상자 위 작은 글씨다. 엣지에 화살표가 없다. 상단 그래프 요약이 한국어 화면에서 `6 nodes · 5 edges`(영어) | 호출 흐름 |
| E7 | URL로 포커스를 바꾸면 포커스 노드가 화면 하단에 걸리고 위 500px이 빈다(맞춤이 안 된다) | 호출 흐름 레벨 2 |
| E8 | 시작 흐름의 `UseMiddleware<ApiKeyMiddleware>()`가 **`UseMiddleware()`** 로 두 줄 표시돼 무엇을 등록했는지 알 수 없다. 좌측 "전체 단계 22개" vs 가운데 "19단계" | 시작 흐름 |
| E9 | 미들웨어 파이프라인에서 `MapControllers`·`MapGroup`·`MapGet`·`MapPost` 8단계가 **같은 문장**("엔드포인트를 등록한다. 실제 실행은…")을 반복한다. 단락 배지 `Status401Unauthorized context.Req &&…`가 배지 밖으로 넘친다 | 미들웨어 |
| E10 | 환경 점검이 **자기 자신이 듣고 있는 25099**에 대해 `PORT_IN_USE`("이미 실행 중인 TraceLens가 있는지 확인하거나 그 프로세스를 종료해야 한다")를 낸다. 하단 줄이 `.sln, .csprojTraceLens 0.8.0 · .NET .NET 8.0.30 · Microsoft Windows 10.0.26200로그 파일:`로 붙어 나온다. **Esc로 닫히지 않는다** | 환경 점검 모달 |
| E11 | 950px 높이에서 우측 열의 "AI에게 질문" 입력칸이 접힘선 아래에 있다. 패널 제목 줄의 "문맥 복사"와 본문의 "문맥 복사" 버튼이 중복이다 | 우측 열 |
| E12 | 잘못된 JSON 본문을 보내면 `POST /api/analyze/load`가 **본문 없는 400**을 돌려준다(`{code,message}` 없음) | curl |

### 2.3 코드 읽기로 확인한 것

- **클라이언트.** `sourceTarget`은 `App.tsx:166`에서 만들고 세팅만 하며 선택이 바뀌어도 비우지 않는다. `highlightPath`는 좌측 목록에서 진입점을 바꿔도(`App.tsx:1789-1793`) 남는다. `ImpactPanel.tsx:23`의 `result`는 `nodeId`가 바뀌어도 초기화 effect가 없다. 우측 열을 접으면(`App.tsx:1997`) `AskPanel`·`ImpactPanel`이 언마운트되어 입력 중인 질문·최근 답변·영향 결과가 사라지고 헤더의 "비교" 버튼이 아무 일도 하지 않는다. `getInitialWindow`(`windowing.ts:42-71`)에는 `MaxWindowNodes` 검사가 없다(드릴다운 `:91`에만 있다). `Splitter`는 `pointermove`마다 `setColumns`(`App.tsx:1868`)를 불러 저장 effect·`measureShell`·`buildClassDiagram`이 연쇄한다. `AskPanel.tsx:167-199`의 effect 의존 배열에 `question`이 있어 타이핑마다 `POST /api/context`를 다시 보낸다. `DataSidePanels.tsx:87`은 `key={entry.nodeId}`라 한 메서드에 라우트가 둘이면 React 중복 키다. 진입점 목록이 세 벌이다(`SidePanels.tsx:52`·`:328`, `DataSidePanels.tsx:84`).
- **분석기.** `ResolveOverrideCall`(`InvocationResolver.cs:111-144`)은 `base.` 한정자를 보지 않아 `Derived.Foo`에서 `base.Foo()`를 부르면 유일 재정의 = 자기 자신으로 추정 엣지를 만든다(픽스처로 재현 필요). 메서드 라우트는 진입 특성의 생성자 인자에서만 읽어(`EntryPointScanner.cs:74-88`) `[Route("items")] [HttpGet]`이 `/api/orders`가 되고 확실도는 "확실"이다. T33의 기반 클래스 처리는 `IsAbstract`(`EntryPointScanner.cs:92`)일 때만이다. `dataAccess.notes`는 접근 0건일 때만 나온다(`DataRuleEngine.cs:450-453`). 트랜잭션 깊이가 구문 범위가 아니라 선형 카운터(`DataAccessScanner.cs:354-397`)이고 호출자→피호출자로 전파되지 않는다(`CompensationAnalyzer.cs:113-120`). `FormatCall`(`StartupFlowScanner.cs:375`)은 리터럴 인자만 남기고 제네릭 타입 인자를 버린다. `Startup` 후보가 여럿이면 `startups[0]`만 쓴다(`HostDocuments.cs:85-90`). 시작 흐름 400단계 절단(`StartupFlowScanner.cs:103,133`)과 External 상위 20/200 절단(`CallSiteCollector.cs:33,36`)은 로그에만 남는다.
- **서버·MCP·CLI.** `get_findings`의 `ruleId`는 정확 일치(`TraceLensTools.cs:226`)인데 도구 설명(`:210`)과 에이전트 안내문(`McpConfigWriter.cs:59`)은 접두사라고 말한다. MCP는 도구마다 `new GraphFreshnessEvaluator()`를 만든다(`TraceLensTools.cs:33,113`). `get_context`는 구조 필드 전체와 그것을 렌더한 Markdown을 함께 돌려주고 예산은 Markdown 길이만 잰다(`ContextBuilder.cs:20-25,195`). `compare_snapshots`·`get_impact` 결과에는 상한이 없다. MCP는 예외를 그대로 던져(`:144,286,465`) `{code,message}`가 없고 파일 로그도 없다(`McpHost.cs:18-20`). `GET /api/graph/classes`·`GET /api/analysis-status`·`GET /api/analysis-jobs`는 클라이언트가 부르지 않는다. `McpConfigWriter.BuildPreview`(`:216-241`)의 호스트는 4종, 원라이너는 2종이다. 인앱 실행은 `claude` 고정(`ServerHost.cs:79`)이고 `--model`을 넘기지 않는다. `data-contract.md`에 MCP 도구 12개의 계약과 `GET /api/analysis-jobs`·`GET /manual/{lang}`이 없다.

### 2.4 검증으로 기각했거나 보류한 주장

- **"URL 해시로 화면을 재현한다"(T40 완료 기준)** — 같은 탭 안에서 해시를 바꾸면 되지만(popstate) **새 탭·북마크에서는 안 된다**(E3). T40의 완료 기준을 만족하지 못한 채 체크된 것이므로 T45에서 다시 다룬다.
- **"`--permission-mode dontAsk`·`--no-session-persistence`가 Claude Code의 공식 인자가 아닐 수 있다"** — 이 세션에서 실제 CLI로 확인하지 못했다. 사실 여부와 무관하게 인자 검증 테스트가 없다는 점은 맞으므로 T70에 "실제 CLI 도움말과 대조하는 검증"으로 넣는다.
- **`POST /api/analyze/load`에 폴더 경로를 주면 404** — 설계상 `.sln`/`.csproj`만 받으므로 버그로 보지 않는다. 다만 E12(잘못된 JSON → 빈 400)는 T72에 넣는다.
- **다이어그램 PNG/SVG 내보내기·북마크·다중 선택** — 요구는 분명하지만 매일의 핵심 경로가 아니어서 P2(T69)로 둔다.

### 2.5 저장소 상태

- `dotnet build` 경고 0. .NET 테스트 210개(Analyzer 89 · Server 112 · Mcp 9) 통과. 클라이언트 테스트 96개(17파일) 통과. 즉 **테스트가 다 통과하는 상태에서 위 버그들이 존재한다** — E1·E2·E3·E10은 어느 테스트도 보지 않는 자리다.
- `Directory.Build.props`의 `<Version>`이 `0.8.0`인데 `CHANGELOG.md`는 `0.9.0 (Unreleased)`다. 헤더·`graph.json`·스냅샷 비교 전제에 `0.8.0`이 들어간다(T72).

### 2.6 하지 않기로 한 것

CLAUDE.md의 설계 제약과 로드맵 §18에 따라 이 문서에 넣지 않는다.

- 파일 트리 탐색기, 저장 감지 자동 재분석, 원격 바인딩, DI·이벤트 엣지, 구현체 여러 개일 때 팬아웃 엣지 — 제약 1·2·3·4.
- MediatR·EF Core·MassTransit·Hangfire·gRPC·Refit의 **해석**(엣지·접근 생성) — 실패 사례 수집 후 별도 설계(로드맵 §18). 이 문서는 "못 봤다"를 더 정확히 말하는 것(T55·T58)까지만 한다.
- MCP에서 재분석을 트리거하는 도구 — 재분석은 사용자가 명시적으로 실행한다(제약 3). stale일 때 에이전트가 사용자에게 요청하는 문구를 다듬는 것(T64)까지만 한다.
- 새 버전 자동 확인, 사용 통계 전송, macOS·Linux 검증, 공개 게시 — 로컬 전용 제약과 "사람이 할 일".
- 모바일·1040px 미만 대응 — 범위 밖. 1366px 노트북과 1600px 데스크톱만 다룬다(T52).

---

## 3. 태스크 상세

<a id="t43"></a>
### T43. 진입점 목록 — 접힘 논리 반전을 고치고 세 벌을 하나로 합친다

**목적.** 매일 첫 조작인 "진입점 고르기"에서 그룹을 매번 열어야 하고, 고른 항목이 목록에서 안 보이는 문제를 없앤다. 보기마다 다른 세 벌의 목록을 하나로 합쳐 검색·그룹·접기·선택 표시가 어디서나 같게 한다.

**현재 상태(근거).**
- E1: 그룹 3개가 모두 접혀 있다. `SidePanels.tsx:79`의 `collapsedByDefault = groups.length > 20 && …`는 3개면 `false`인데, `:105`의 `open = expanded.has(classId) !== collapsedByDefault`가 `has(id) !== false` = `has(id)`가 되어 **기본 닫힘**이다. 그룹이 21개를 넘으면 반대로 기본 열림이다. `:63` 주석("목록이 짧으면 펼침이 기본")과 정반대다.
- 고른 진입점이 접힌 그룹 안에 있으면 선택 표시가 보이지 않는다(스크린샷 `cf-entry`: 좌측에 선택 강조가 없다). 선택이 바뀌어도 해당 그룹을 자동으로 열지 않는다.
- 목록이 세 벌이다: `SidePanels.tsx:52`(검색+그룹+접기), `SidePanels.tsx:328`(전체 흐름도용, 그룹만), `DataSidePanels.tsx:84`(검색만, 그룹 없음, `key={entry.nodeId}` 중복 키).
- Minimal API 진입점의 그룹 이름이 핸들러 선언 클래스(`Program`)라 의미가 없다. 등록 파일(`ReportEndpoints`)로 묶이는 것과 섞여 기준이 둘이다.
- 긴 라우트(`/api/v1/reports/{custo…`)가 잘리고 툴팁 외 대안이 없다. 정렬 옵션이 없다(삽입 순).

**변경할 파일.** `client/src/components/SidePanels.tsx`, `components/DataSidePanels.tsx`, `components/EntryPointList.tsx`(신규, 공용), `graph/entryGroups.ts`(신규, 순수 함수 + 테스트), `App.tsx`, `i18n/ko.ts`·`en.ts`, `styles.css`.

**구현 방법.**
1. `graph/entryGroups.ts`에 그룹화·정렬·"선택 항목이 속한 그룹" 계산을 순수 함수로 뺀다. 그룹 키는 **컨트롤러 진입점은 클래스, Minimal API는 `MapGroup` 접두사(없으면 등록 파일)**로 하고 라벨에 출처 아이콘을 붙인다.
2. 접힘 상태를 `open = collapsedByDefault ? expanded.has(id) : !expanded.has(id)`로 바로잡고, 21개 초과에서만 기본 접힘이 되게 한다. 선택이 바뀌면 그 항목의 그룹을 강제로 연다(`useEffect`로 `expanded`에 추가).
3. `EntryPointList` 하나로 세 벌을 대체한다. props: `entryPoints`, `selectedEndpointId`, `onSelect`, `showSearch`, `groupBy`. `DataSidePanels`의 `key`는 `endpointId`로 바꾼다.
4. 정렬 토글(라우트 / 동사 / 출처)을 목록 머리에 두고 `PersistedState.entryListSort`에 저장한다. 라우트는 잘리면 뒤쪽(`…/{customerId}`)이 남게 `direction: rtl` 대신 앞부분을 생략하는 표시를 쓴다(공통 접두사 `/api/v1`을 그룹 머리로 올리면 대부분 해결된다).

**검증 방법.** `entryGroups.test.ts`에 (a) 3개 그룹은 기본 열림, 21개 그룹은 기본 접힘, (b) 선택 항목의 그룹이 열림, (c) Minimal API가 접두사로 묶임을 넣는다. 수동: 예제 분석 → 목록이 펼쳐진 채로 시작 → 통합 찾기로 진입점을 고르면 좌측에서 강조가 보인다 → 전체 흐름도·DB·캐시에서 같은 목록이 보인다.

**완료 기준.** 그룹 20개 이하에서 기본 펼침, 초과에서 기본 접힘. 어떤 방법으로 진입점을 골라도 좌측 목록에서 강조가 보인다. 진입점 목록 컴포넌트가 하나다. React 중복 키 경고가 없다.

<a id="t44"></a>
### T44. 라이트 테마를 완성한다 — 어두운 상자 위의 안 보이는 글자

**목적.** T41이 만든 라이트 테마에서 네 곳의 핵심 글자가 안 보인다. 테마가 켜진 채 클래스 다이어그램·DB·캐시·미들웨어를 쓸 수 없다.

**현재 상태(근거).**
- E2: UML 상자(`.uml`, `styles.css:180` `background: #141d2c`)의 이름, DB·캐시 실행 경로 카드, 미들웨어 단계 카드, 도움말의 강조 항목이 어두운 배경 위에 테마 글자색(어두움)으로 그려진다.
- `styles.css`에 `background: #0…`/`#1…` 하드코딩이 **101곳**이고 `[data-theme="light"]` 재정의는 21개 선택자뿐이다. `.btn`(`:41`), `.freshness-badge`(`:50`), `.mode-bar`(`:61`), `.panel`(`:102`), `.side-search`(`:110`), `.pill`(`:226`), `.captured-context`(`:264`) 등 대부분이 라이트에서도 어두운 색이다(스크린샷의 검은 검색창·검은 버튼이 그것이다).

**변경할 파일.** `client/src/styles.css`, `components/UmlClassBox.tsx`(인라인 색이 있으면), `client/src/theme.test.ts`(신규).

**구현 방법.**
1. 하드코딩된 배경·테두리·글자색을 `--surface-1/2/3`, `--border`, `--text`, `--dim`, `--accent-*` 변수로 바꾼다. 라이트 테마는 변수 값만 재정의한다. 상태색(선택·확실/추정·심각도)은 두 테마 모두에서 대비 4.5:1이 되는 값을 고른다.
2. `theme.test.ts`: `styles.css`를 읽어 `:root`·`[data-theme]` 블록 밖에서 `#`으로 시작하는 `background`/`color`가 있으면 실패한다(예외 목록은 파일 상단 주석에 사유와 함께 둔다).
3. 헤드리스 스크린샷 스크립트를 `client/scripts/screenshot.mjs`로 저장소에 넣는다(puppeteer-core, 로컬 Chrome 사용, 의존성은 devDependencies). `npm run screenshots -- --theme light`가 여섯 보기를 `docs/screenshots/light/`에 찍는다. 자동 판정은 하지 않고 사람이 본다.

**검증 방법.** `npm test`의 `theme.test.ts` 통과. 수동: 라이트·다크 각각 여섯 보기 + 도움말·환경 점검·MCP 대화상자 스크린샷을 찍어 안 보이는 글자가 없는지 확인한다.

**완료 기준.** 라이트 테마에서 E2의 네 곳이 읽힌다. 테마 블록 밖에 하드코딩 색이 없다(예외는 사유 주석). 스크린샷 스크립트가 저장소에 있고 README의 개발 절에 한 줄로 적혀 있다.

<a id="t45"></a>
### T45. URL 상태를 시작할 때 읽고, 링크 복사와 히스토리 규칙을 정한다

**목적.** T40이 약속한 "URL로 화면 재현"이 새 탭·북마크에서는 되지 않는다(E3). 동료에게 위치를 보내고 어제 보던 자리로 돌아오는 가장 싼 방법을 실제로 켠다.

**현재 상태(근거).**
- `App.tsx:371-386`이 `pushState`로 해시를 쓰지만 `parseUrlState`는 `App.tsx:390`(popstate)에서만 불린다. 마운트 시 `window.location.hash`를 읽지 않는다.
- 해시 범위가 view/entry/focus/selection뿐이다(`state/urlState.ts:8-15`). DB·캐시 하위 탭·전체 흐름도 진입점·깊이·클래스 다이어그램 토글은 없다.
- 선택 변경마다 `pushState`(`App.tsx:385`)라 노드 20개를 눌러 보면 뒤로 가기를 20번 눌러야 이전 보기로 나간다.
- 해시가 가리키는 노드가 창 밖이면 `GraphView.tsx:133-138`이 조용히 넘어가고, 상세는 "없다"만 말한다(E4와 같은 경로).

**변경할 파일.** `client/src/App.tsx`, `state/urlState.ts`(+테스트), `components/ViewToolbar.tsx`, `i18n/ko.ts`·`en.ts`.

**구현 방법.**
1. 마운트 직후 그래프 로드가 끝난 시점에 `parseUrlState(location.hash)`를 적용한다. 그래프에 없는 ID는 무시하고 "링크의 항목이 지금 분석 결과에 없다" 배너를 띄운다(옛 링크는 흔하다). localStorage 복원보다 **URL이 우선**이다.
2. 해시에 `sub`(DB·캐시 하위 탭), `flowEntry`, `depth`, `cm`(클래스 다이어그램 토글 비트)를 추가한다. 파서는 모르는 키를 무시한다(기존 규칙 유지).
3. 히스토리 규칙: 보기·진입점·포커스 변경은 `pushState`, **선택 변경은 `replaceState`**. `urlState.test.ts`에 규칙을 적는다.
4. 툴바에 "링크 복사" 버튼(현재 해시 + 서버 주소). 루프백 주소라 외부에 공개되지 않는다는 문구를 툴팁에 둔다(제약 4).
5. 선택 노드가 현재 창 밖이면 캔버스 위에 "선택한 노드는 이 창에 없다 — [그 노드로 이동]" 바를 띄운다. 이동은 진입점→선택 노드 경로 창(`getPathWindow`)을 쓴다.

**검증 방법.** `urlState.test.ts`에 왕복(직렬화→파싱) 테스트와 replace/push 규칙 테스트. 헤드리스 스크립트로 새 탭에서 `#view=data&sub=paths`를 열어 그 화면이 뜨는지 확인한다(T44의 스크립트 재사용). 수동: 링크 복사 → 새 탭 → 같은 화면.

**완료 기준.** 새 탭·북마크에서 여섯 보기와 하위 탭·선택이 재현된다. 노드 20개를 눌러도 뒤로 가기 한 번에 이전 보기로 간다. 창 밖 선택에 안내와 이동 버튼이 있다.

<a id="t46"></a>
### T46. 선택을 바꿔도 따라오지 않는 상태 네 가지

**목적.** 다른 메서드를 골랐는데 **이전 메서드의 소스·경로·영향 결과**가 남아 있는 것은 잘못된 코드를 보게 만드는 버그다. 우측 열을 접었다 펴면 입력 중이던 질문이 사라지는 것도 같은 부류다.

**현재 상태(근거).**
- `sourceTarget`: `App.tsx:166`에서 만들고 `:909,2040,2060,2068`에서 세팅만 한다. `DetailPanel.tsx:336`은 `sourceTarget?.file ?? node?.file`로 **있으면 무조건** 그 파일을 쓴다. `파일:줄`을 한 번 누른 뒤 다른 메서드를 고르면 상세의 소스가 이전 파일에 머물고 호출 지점 마커도 그 파일 기준이다.
- `highlightPath`: `App.tsx:287-293`이 비어 있지 않으면 `getPathWindow`를 쓰는데, 좌측 목록에서 진입점을 바꾸는 `App.tsx:1789-1793`은 비우지 않는다(`:980,1925`만). 영향 목록에서 한 번 이동한 뒤 다른 API를 고르면 그 API + 이전 경로 노드가 끊긴 채 한 캔버스에 뜬다.
- `ImpactPanel.tsx:23`의 `result`는 `nodeId`가 바뀌어도 남는다. `:70`의 `onOpenEntryPoint(endpoint.handlerNodeId, nodeId)`는 옛 결과 + 새 nodeId로 빈 경로를 만든다.
- 우측 열을 접으면(`App.tsx:1997`) `AskPanel`·`ImpactPanel`이 언마운트된다 → 질문·최근 답변 5개·영향 결과가 사라지고, 헤더 "분석 전후 비교"(`App.tsx:1473-1477`)와 안내의 `onOpenImpact`(`:1718-1721`)가 대상이 없어 아무 일도 하지 않는다.
- `changeMode`(`App.tsx:935-947`)는 `focusOnly`만 끄고 `highlightPath`·`sourceTarget`·`search`는 둔다.

**변경할 파일.** `client/src/App.tsx`, `components/ImpactPanel.tsx`, `components/AskPanel.tsx`, `components/DetailPanel.tsx`, `state/viewState.ts`.

**구현 방법.**
1. 선택 변경을 한 함수(`selectItem`)로 모으고 거기서 `sourceTarget`·`highlightPath`를 비운다. `파일:줄`로 연 소스는 "선택한 항목의 소스"와 구분되는 **임시 보기**로 두고, 상세 패널에 "선택 항목의 선언으로 돌아가기" 링크를 둔다.
2. `ImpactPanel`에 `useEffect(() => setResult(null), [nodeId])`. 결과 머리에 "대상: X"를 적어 무엇의 결과인지 보이게 한다.
3. 우측 열 접기는 언마운트가 아니라 `hidden`(CSS)으로 바꾼다. 질문 초안·최근 답변·영향 결과는 `App` 상태로 올려 새로고침 전까지 유지한다. 접힌 상태에서 "비교"·"영향" 버튼을 누르면 열을 편 뒤 스크롤한다.
4. `changeMode`에서 보기와 무관한 상태(`highlightPath`·`sourceTarget`·검색어)를 정리한다. 무엇을 비우는지 주석으로 적는다.

**검증 방법.** `viewState.test.ts`에 "선택 변경 시 sourceTarget·highlightPath가 비워진다"를 넣는다(순수 리듀서로 뽑아서). 수동: `파일:줄` 클릭 → 다른 메서드 선택 → 소스가 새 메서드다. 영향 실행 → 다른 메서드 선택 → 결과가 비고 "대상"이 바뀐다. 우측 열 접기 → 펴기 → 질문 초안이 남아 있다.

**완료 기준.** 위 네 상태가 선택을 따라온다. 우측 열을 접어도 입력·결과가 남는다. 접힌 상태에서 헤더 버튼이 열을 편다.

<a id="t47"></a>
### T47. DB·캐시/미들웨어 상세에 코드 보기를 붙이고 `파일:줄` 동작을 통일한다

**목적.** 점검 결과에서 `파일:줄`을 눌러도 화면이 바뀌지 않는다(상태만 바뀐다). 호출 흐름 상세에만 있는 코드 보기와 Shift+클릭을 모든 상세에 같게 둔다.

**현재 상태(근거).**
- `DataDetailPanel.tsx`는 `SourceLink`만 임포트하고 `SourceView`가 없다. `App.tsx:2040`이 `onOpenSource={setSourceTarget}`을 넘기지만 그리는 곳이 없다. `ContextPreview.tsx:93,115`·`ImpactPanel.tsx:81`의 링크도 선택이 Finding일 때는 죽은 버튼이다.
- `onOpenInEditor`(Shift+클릭 = 에디터에서 열기)는 `DetailPanel.tsx:386-387,529,545`만 넘긴다. `DataDetailPanel.tsx:292`, `ImpactPanel.tsx:81,96`, `ContextPreview.tsx:93,115`는 안 넘겨 툴팁 문구까지 다르다(`SourceLink.tsx:30`).
- 호출 흐름의 우측 열 상단(코드 패널 자리)이 선택 전에는 빈 흰 영역이다 — 안내 문구가 없다(스크린샷 `cf-entry`).

**변경할 파일.** `client/src/components/DataDetailPanel.tsx`, `components/MiddlewareView.tsx`(상세 부분), `components/ImpactPanel.tsx`, `components/ContextPreview.tsx`, `components/SourceLink.tsx`, `components/CodePanel.tsx`, `App.tsx`, `i18n`.

**구현 방법.**
1. 코드 패널을 우측 열 상단 **공용** 영역으로 두고(이미 자리가 있다) 어떤 보기에서든 `sourceTarget`을 그린다. 선택 전에는 "항목을 고르거나 `파일:줄`을 누르면 소스가 여기 보인다"를 적는다.
2. `SourceLink`가 `onOpenInEditor`를 필수로 받게 하고, App에서 한 번 만든 핸들러를 모든 패널에 내린다. 툴팁을 한 문장으로 통일한다.
3. 점검 결과 상세(`DataDetailPanel`)의 "근거" 항목마다 `파일:줄`이 코드 패널을 그 구간으로 옮기고, 접근 사이트 줄에 여백 표시(T39와 같은 마커)를 찍는다.

**검증 방법.** 수동: DB·캐시 → 점검 결과 → 근거 `파일:줄` 클릭 → 코드 패널 이동 + 강조. Shift+클릭 → 에디터. 미들웨어 단계 카드의 `Program.cs:24` 클릭 → 이동. 영향 목록·문맥 미리보기의 링크도 같다.

**완료 기준.** 화면의 모든 `파일:줄`이 코드 패널을 움직이고 Shift+클릭이 에디터를 연다(T39 완료 기준이 실제로 성립). 코드 패널이 비어 있을 때 안내가 있다.

<a id="t48"></a>
### T48. 환경 점검 모달 — 자기 포트 오탐·줄 붙음·Esc

**목적.** 앱 안에서 연 환경 점검이 **항상** "포트를 이미 쓰고 있다 → 프로세스를 종료하라"고 말한다. 자기 자신을 문제로 보고하는 진단은 신뢰를 깎는다.

**현재 상태(근거).**
- E10: `CheckPort`(`EnvironmentDiagnostics.cs:462-484`)는 활성 리스너에 그 포트가 있으면 무조건 `PORT_IN_USE`다. 서버 안에서 부르면 서버 자신이 그 리스너다. T36은 "실제 바인딩 포트를 검사"하게 바꿨지만 자기 자신을 제외하지 않았다.
- 하단 요약 줄이 `.sln, .csprojTraceLens 0.8.0 · .NET .NET 8.0.30 · …로그 파일:`로 붙는다. `DiagnosticsCard.tsx:83-85`가 `<small>` 뒤에 구분 없이 버전 줄을 이어 붙이고, `runtimeVersion`에 이미 `.NET`이 들어 있다.
- Esc로 닫히지 않는다(`App.tsx:1675-1695`, 배경 클릭만). 도움말은 "Esc가 열린 대화상자를 닫는다"(`ko.ts:799`)고 적어 둔 상태다. 스크린샷 `dark-data`·`laptop-1366-data`에서 Esc를 눌러도 모달이 남아 있다.

**변경할 파일.** `src/TraceLens.Context/EnvironmentDiagnostics.cs`, `src/TraceLens.Server/Endpoints/DiagnosticsEndpoints.cs`, `client/src/components/DiagnosticsCard.tsx`, `App.tsx`, `tests/TraceLens.Server.Tests`.

**구현 방법.**
1. `DiagnosticsOptions`에 `SelfListening: bool`을 더한다. 서버 안에서 부를 때는 `true`로 넘기고, 그때는 `PORT_IN_USE` 대신 `PORT_SELF`("이 TraceLens가 이 포트로 실행 중이다", Ok)를 낸다. CLI `doctor`는 기존 동작을 유지한다.
2. `DiagnosticsCard`의 요약을 `<dl>` 세 줄(지원 대상 / 버전 / 로그 파일)로 나누고 `.NET` 접두사를 한 번만 쓴다.
3. 환경 점검 모달에 Esc 처리와 포커스 복원을 넣는다(T68에서 공용 모달 훅으로 옮길 때 그것을 쓴다).

**검증 방법.** `DiagnosticsEndpointsTests`에 "서버 안에서 부르면 자기 포트가 `PORT_SELF`"를 추가한다. 수동: 앱에서 환경 점검 → 포트 항목이 Ok → Esc로 닫힘 → 요약 세 줄이 분리돼 있다.

**완료 기준.** 앱 안 환경 점검에서 자기 포트를 문제로 보고하지 않는다. 요약 줄이 붙지 않는다. Esc로 닫힌다.

<a id="t49"></a>
### T49. 통합 찾기가 막다른 길로 보내지 않게 한다

**목적.** `Ctrl+K`로 고른 항목이 "이 메서드는 마지막 분석 결과에 없다"로 끝나면(E4) 찾기를 믿지 않게 된다.

**현재 상태(근거).**
- 검색 소스가 `classes[].members`라 그래프 노드가 아닌 **인터페이스 멤버·프로퍼티·record 위치 인자**(`OrderId`, `IOrderCache.Set`)가 "메서드" 그룹에 나온다. 고르면 노드가 없어 상세가 "없다"를 낸다. 인터페이스와 구현이 같은 이름으로 나란히 나와 무엇이 무엇인지 표시가 없다.
- 그룹별 20개 상한 + "잘렸다" 안내뿐이고 범위 지정(동사·네임스페이스·종류)이 없다(`graph/search.ts:39`). 최근 검색어가 없다. 클래스·규칙·파일은 검색 대상이 아니다.

**변경할 파일.** `client/src/graph/search.ts`(+테스트), `components/QuickFind.tsx`, `i18n`.

**구현 방법.**
1. 결과를 종류별로 나눈다: 진입점 / 메서드(그래프 노드만) / 클래스 / 규칙·점검 결과 / 파일. 노드가 없는 멤버는 **클래스 결과의 부제**로만 보이고 고르면 클래스가 선택된다. 인터페이스 멤버를 고르면 "구현체 N개"를 펼쳐 구현 노드로 간다(DI 등록으로 이어진 것을 먼저).
2. 접두사 필터: `GET `, `POST `, `ns:`, `rule:`, `file:`. 순위는 정확 일치 > 접두사 > 부분, 같은 순위면 진입점 > 메서드.
3. 최근 선택 5개를 `PersistedState.quickFindRecent`에 저장해 빈 입력일 때 보여 준다.

**검증 방법.** `search.test.ts`에 (a) 노드 없는 멤버가 메서드 그룹에 나오지 않음, (b) 인터페이스 멤버 → 구현 노드, (c) 접두사 필터를 넣는다. 수동: `ReserveAsync` → 첫 결과가 구현 노드 → 상세가 채워진다.

**완료 기준.** 통합 찾기에서 고른 것이 "없다"로 끝나지 않는다. 종류·접두사 필터와 최근 항목이 있다.

<a id="t50"></a>
### T50. 호출 흐름 노드·엣지·맞춤·컨트롤

**목적.** 호출 흐름은 도구의 첫 화면인데 노드가 빈 상자이고 방향이 안 보이고 파고든 뒤 화면이 맞춰지지 않는다(E6·E7). 다른 두 캔버스에는 있는 줌·맞춤 컨트롤이 여기만 없다.

**현재 상태(근거).**
- 노드 상자 안이 비어 있고 라벨은 상자 위 10px 굵은 글씨다(스크린샷 `cf-entry`). 클래스·메서드·종류·HTTP 동사·확실도가 한눈에 안 들어온다. `⊕`는 라벨 위 작은 원이라 누르기 어렵다.
- 엣지에 화살표가 없다. 레벨 간 세로 간격이 400px 가까이 돼 6개 노드가 화면을 다 쓴다(120개면 스크롤 지옥).
- URL로 포커스를 바꾼 뒤 `fit`이 안 된다(E7). `GraphView.tsx`에 줌·맞춤·리셋·배율 표시가 없다(`DiagramCanvas.tsx:209-240`에는 있다). 도움말은 "Ctrl+휠 다이어그램 확대"(`ko.ts:792`)지만 Cytoscape는 맨 휠로 줌한다.
- 상단 요약 `${n} nodes · ${m} edges`가 영어 하드코딩이다(`App.tsx:1298`). 한국어 하드코딩 테스트는 영어를 못 잡는다.
- 사이클 표시가 보기마다 다르다: 흐름도는 `↺`(`flowMap.ts:196-197`), 호출 흐름 창은 표시 없음(`windowing.ts:62,90`).

**변경할 파일.** `client/src/components/GraphView.tsx`, `graph/cytoscapeStyle.ts`(신규), `graph/windowing.ts`, `App.tsx`, `i18n`, `styles.css`.

**구현 방법.**
1. 노드를 두 줄(클래스 / 메서드명)로 상자 **안에** 그리고(Cytoscape `label` + `text-wrap: wrap`), 왼쪽에 종류 색띠, 진입점이면 동사 배지, 추정 엣지는 점선. `⊕`는 상자 오른쪽 아래 24px 원으로 키운다.
2. 엣지에 `target-arrow-shape: triangle`. 레이아웃 `rankSep`을 줄이고 창이 바뀔 때마다 `cy.fit(padding)`을 부른다(팬·줌 초기화 문제는 `graphWindow` 의존만 유지, T18의 콜백 ref 패턴을 깨지 않는다).
3. `DiagramCanvas`의 줌 컨트롤을 공용 컴포넌트로 빼서 호출 흐름에도 붙인다. 휠 동작을 세 캔버스에서 **Ctrl+휠 = 줌, 휠 = 스크롤**로 통일하거나, 통일하지 않으면 도움말의 단축키 표에 보기별로 적는다.
4. 요약 문구를 i18n 키로 바꾸고, `noHardcodedKorean.test.ts` 옆에 **`noHardcodedEnglishInKoView`**를 추가하지는 않는다(범위 과다). 대신 `graph/`·`App.tsx`의 사용자 문구를 i18n으로 옮길 때 그 테스트의 glob을 `graph/`까지 넓힌다(T72와 공유).
5. 창 안에서 방문 집합에 걸린 노드에 `↺` 배지를 붙인다(흐름도와 같은 규칙).

**검증 방법.** `windowing.test.ts`에 사이클 배지 플래그 테스트. 수동(스크린샷): 진입점 선택 → 상자 안에 두 줄, 화살표, `⊕` 24px. `⊕` 클릭 → 화면이 새 창에 맞춰진다. 영어 문구가 한국어 화면에 없다.

**완료 기준.** 노드 안에 이름이 있고 엣지에 방향이 있다. 창이 바뀌면 맞춤된다. 호출 흐름에 줌·맞춤 컨트롤이 있다. 도움말의 휠 설명이 실제 동작과 같다.

<a id="t51"></a>
### T51. 1단계 팬아웃 상한과 호출자 방향 창

**목적.** 컨트롤러 액션이 30~50개 메서드를 직접 부르면 첫 창부터 무너지고 "잘렸다"는 안내도 없다. "이 메서드를 부르는 3개 경로"를 그래프에서 못 본다.

**현재 상태(근거).**
- `getInitialWindow`(`windowing.ts:42-71`)는 첫 분기점의 자식을 전부 넣고 `MaxWindowNodes` 검사가 없다(`getDrilldownWindow` `:91`에만). `hiddenCount`도 0이다.
- `windowing.ts` 전체가 outgoing만 본다. 호출자는 상세 패널의 pill 목록(`DetailPanel.tsx:435-438`)뿐이다. 호출 엣지 범위 안이므로 제약 2 위반이 아니다.

**변경할 파일.** `client/src/graph/windowing.ts`(+테스트), `components/GraphView.tsx`, `components/DetailPanel.tsx`, `App.tsx`, `i18n`.

**구현 방법.**
1. `getInitialWindow`에도 `MaxWindowNodes`를 적용하고 넘치면 자식을 **종류별(Repository·Service·External…)로 묶은 요약 노드** `"+N개 더"`로 대체한다. 요약 노드를 누르면 그 종류만 펼친다. `hiddenCount`를 채운다.
2. 상세 패널의 호출자 pill 옆에 "호출자 창 보기"를 두고 `getCallerWindow(graph, nodeId, hops=2)`를 추가한다. 호출자 창은 위로 자라는 트리로 그리고 브레드크럼에 "↑ 호출자"로 표시한다. 진입점까지 이어진 경로가 있으면 진입점 동사 배지를 붙인다.
3. 창 종류(`down`/`up`)를 URL 해시에 넣는다(T45).

**검증 방법.** `windowing.test.ts`에 (a) 자식 200개 → 요약 노드 + `hiddenCount`, (b) 호출자 창 2홉, (c) 사이클 종료를 넣는다. 수동: 픽스처 대신 실제 사내 솔루션에서 팬아웃이 큰 액션을 골라 첫 창이 120개 이하인지 본다.

**완료 기준.** 어떤 진입점을 골라도 첫 창이 상한을 넘지 않고 넘친 개수가 보인다. 호출자 방향 창이 있고 URL로 재현된다.

<a id="t52"></a>
### T52. 헤더·툴바·우측 열이 1600px에서 넘치지 않게 한다

**목적.** 1600px에서 헤더 마지막 버튼이 잘리고(E5) 툴바가 두 줄이 되며 우측 열의 질문 입력칸이 접힘선 아래다(E11). 1366px 노트북에서는 더 심하다.

**현재 상태(근거).**
- 헤더에 컨트롤이 15개다: 상태 배지·`?`·다시 분석·경로·찾아보기·불러오기·저장·분석·버전·A-/A/A+·테마·언어·안내·`?`·환경 점검·설명서·비교. 부제 "ASP.NET CORE 호출 탐색기"가 "탐/색기"로 꺾인다.
- 클래스 다이어그램 툴바(배치·멤버·관계 4종·선택 1홉·줌 4개)가 한 줄을 넘는다.
- 우측 열은 코드 패널·상세·영향·질문이 세로로 쌓여 950px에서 질문 입력칸이 보이지 않는다. 질문 패널 제목 줄과 본문에 "문맥 복사"가 둘이다.

**변경할 파일.** `client/src/App.tsx`, `components/ViewToolbar.tsx`, `components/AskPanel.tsx`, `styles.css`, `i18n`.

**구현 방법.**
1. 헤더를 두 묶음으로 줄인다: 왼쪽 = 로고·상태·경로·찾아보기·분석·다시 분석. 오른쪽 = "도움말" 드롭다운(안내·용어집·설명서·환경 점검·MCP 설정·공유 보고서)과 "설정" 드롭다운(글꼴·테마·언어). "불러오기·저장·비교"는 상태 배지 옆 "결과" 드롭다운으로. 부제는 1400px 미만에서 숨긴다.
2. 클래스 다이어그램 툴바는 줌 4개를 캔버스 위 컨트롤(T50의 공용 컴포넌트)로 옮기고 관계 4종은 하나의 다중 선택 칩 메뉴로 접는다.
3. 우측 열은 **탭**(상세 / 영향 / 질문)으로 바꾸고 코드 패널은 위에 고정한다. 탭 전환 시 상태는 남는다(T46). 질문 패널의 중복 버튼을 하나로 한다.
4. 1366×768에서도 접힘선 위에 질문 입력칸이 보이는지를 완료 기준에 넣는다.

**검증 방법.** 헤드리스 스크린샷(1600×950, 1366×768) 두 벌에서 잘린 버튼·두 줄 툴바가 없는지 본다. 키보드로 드롭다운을 열고 닫을 수 있다(T68과 연결).

**완료 기준.** 두 해상도에서 헤더·툴바가 한 줄이고 우측 열 탭 세 개가 모두 보인다. 헤더 버튼 수가 절반 이하다.

<a id="t53"></a>
### T53. `base.Method()`가 자기 자신으로 가는 엣지를 만든다

**목적.** 재정의 안에서 `base.Foo()`를 부르는 흔한 코드가 그래프에 **자기 순환 엣지(추정)**로 나온다. 재귀처럼 보여 사람도 에이전트도 오독한다.

**현재 상태(근거).** `ResolveOverrideCall`(`InvocationResolver.cs:111-144`)은 `Direct` 해석 + 대상이 virtual/abstract이면 `diResolver.ResolveOverride(target)`로 유일 재정의를 찾아 추정 엣지를 추가한다. 호출식이 `base.` 한정자인지 보지 않으므로 `Derived.Foo` 안의 `base.Foo()`가 `Derived.Foo → Derived.Foo`가 된다. 코드 읽기로 추론했고 **픽스처로 재현이 먼저**다.

**변경할 파일.** `src/TraceLens.Analyzer/InvocationResolver.cs`, `tests/TraceLens.Analyzer.Tests/Fixtures/…`(기반/파생 클래스 픽스처 추가), `tests/…/InvocationResolverTests.cs`.

**구현 방법.**
1. 픽스처에 `abstract class BaseHandler { public virtual Task HandleAsync() }`, `class OrderHandler : BaseHandler { public override Task HandleAsync() { … base.HandleAsync(); } }`를 넣고 현재 결과를 테스트로 고정한다(실패해야 한다).
2. `ResolveOverrideCall`에서 호출식의 수신자가 `BaseExpressionSyntax`이면 재정의 추가 엣지를 만들지 않고 기반 선언으로 가는 직접 엣지만 둔다(런타임 타입이 아니라 **정적으로 확정된 호출**이므로 "확실"). `this.Foo()`/`Foo()`는 지금처럼 둔다.
3. 같은 자리에서 `new()` 제약·`sealed override`도 확인한다: 대상이 `sealed`면 재정의 탐색을 건너뛴다.

**검증 방법.** 위 테스트가 통과하고 자기 순환 엣지가 0개다. 기존 89개 테스트 통과. 실제 사내 솔루션에서 자기 순환 엣지 수를 전후로 비교해 CHANGELOG에 적는다.

**완료 기준.** `base.Method()`가 기반 메서드로 가는 "확실" 엣지 하나만 만든다. 픽스처 회귀 테스트가 있다.

<a id="t54"></a>
### T54. 진입점 네 가지 — 메서드 `[Route]`·HEAD/OPTIONS·비추상 기반 컨트롤러·`IgnoreApi`

**목적.** README(`README_kr.md:399`)가 "`[Route]` 지원"이라고 적었는데 메서드에 붙은 `[Route]`는 읽지 않아 **"확실"로 틀린 라우트**가 나온다. 나머지 셋은 조용히 빠지거나 숨긴 API가 목록에 남는다.

**현재 상태(근거).**
- `EntryPointScanner.cs:74-88`: 메서드 라우트는 진입 특성(`[HttpGet("…")]`)의 생성자 인자에서만 읽는다. `[Route("items")] [HttpGet]`은 `/api/orders`가 되고 확실도 "확실"이다.
- `TraceLensConfig.cs:64` 기본 목록에 `HttpHead`·`HttpOptions`가 없다(`[AcceptVerbs]`로 쓴 HEAD만 잡힌다).
- T33은 `IsAbstract`(`EntryPointScanner.cs:92`)일 때만 파생별 진입점을 만든다. 비추상 기반 컨트롤러(`class CrudController<T> : ControllerBase`를 상속하는 `OrdersController`)는 2차 문서 §2.4의 원래 문제(존재하지 않는 라우트 하나)가 그대로다.
- `[ApiExplorerSettings(IgnoreApi = true)]`는 저장소에 문자열 0건. 숨긴 API가 목록에 그대로다.
- 파생별 진입점 N개가 같은 `nodeId`(기반 메서드)를 가리켜 호출 흐름이 N개 동일하다(`:213-228`). 이것은 그대로 두되 상세에 "기반 클래스 메서드 — 파생 K개가 공유"를 적는다.

**변경할 파일.** `src/TraceLens.Analyzer/EntryPointScanner.cs`, `TraceLensConfig.cs`, `docs/tracelens.config.schema.json`, `Models/EntryPoint.cs`(`hiddenFromApiExplorer: bool` 추가 — 부가 필드, 스키마 번호 유지), `tests/…/FixturesClassic`, `README_kr.md`·`README.md`, `client/src/components/DetailPanel.tsx`·`SidePanels.tsx`.

**구현 방법.**
1. 메서드의 `[Route]`를 모두 읽어 클래스 `[Route]` × 메서드 `[Route]` × 진입 특성 라우트의 곱으로 라우트를 만든다(ASP.NET Core 규칙: 진입 특성에 템플릿이 있으면 그것이 메서드 `[Route]`를 대체하지 않고 **각각 별도 라우트**가 된다 — 문서로 확인해 테스트에 적는다).
2. 기본 `entryPointAttributes`에 `HttpHead`·`HttpOptions`를 넣고 스키마 기본값을 맞춘다(`ConfigSchemaTests`가 잡는다).
3. 기반 클래스 판정을 "액션을 선언한 타입이 `[ApiController]`/`ControllerBase` 파생이면서 **그 타입을 상속하는 컨트롤러가 솔루션에 있으면**"으로 넓힌다. 기반 클래스 자체가 비추상이고 라우팅 가능한 컨트롤러이면 기반 클래스의 진입점도 남긴다(둘 다 실제 라우트다).
4. `[ApiExplorerSettings(IgnoreApi = true)]`를 읽어 `hiddenFromApiExplorer`에 넣는다. 진입점 목록에서 기본 숨김 + "숨긴 API N개 보기" 토글. 삭제하지 않는다(실제 라우트는 존재한다).

**검증 방법.** `FixturesClassic`에 네 경우를 추가하고 `EntryPointScannerTests`에 기대 라우트·동사·개수를 적는다. README 지원 표를 갱신한다.

**완료 기준.** 메서드 `[Route]`가 라우트에 반영되고 틀린 라우트가 "확실"로 나오지 않는다. HEAD/OPTIONS 진입점이 잡힌다. 비추상 기반 컨트롤러의 액션이 파생별로 나온다. 숨긴 API가 구분된다.

<a id="t55"></a>
### T55. DB·캐시 notes 조건과 프로퍼티 본문 스캔

**목적.** 실전에서 가장 흔한 EF Core + Dapper 혼용 솔루션에서 "EF Core는 안 읽었다"가 사라진다. 프로퍼티 getter 안의 쿼리는 조용히 빠진다. 둘 다 제약 5(못 본 것을 말한다)의 문제다.

**현재 상태(근거).**
- `DataRuleEngine.cs:450-453`: `dataAccess.notes`는 **접근 0건일 때만** 나온다. Dapper 접근 1건이 있으면 EF Core `DbSet` 100곳이 있어도 notes가 없고 규칙은 전부 "검사함"이다.
- `DataAccessScanner.cs:191-196`: 스캔 단위가 `Method`/`Constructor` 선언뿐이라 프로퍼티 getter·식 본문 프로퍼티·인덱서 안의 쿼리/캐시 호출이 누락된다.
- 기본 캐시 패턴 `*Cache.*`·`*Redis*.*`(`DataAccessOptions.cs:35`)가 넓어 외부 패키지의 임의 `…Cache` 타입을 캐시로 잡는다(솔루션 안 타입은 T13에서 제외했다).

**변경할 파일.** `src/TraceLens.Analyzer/DataRuleEngine.cs`, `DataAccessScanner.cs`, `DataAccessOptions.cs`, `tests/…/FixturesData`·`FixturesExternal`, `client/src/components/DataSidePanels.tsx`(notes 표시 위치).

**구현 방법.**
1. notes 조건을 "ORM 힌트(EF Core·Mongo·NHibernate 타입 참조)가 있으면 **항상**"으로 바꾸고 문구에 개수를 넣는다: "EF Core 호출 N곳은 접근으로 세지 않았다(외부 네임스페이스 집계 참고)". 규칙 카탈로그에도 "이 규칙은 SQL 리터럴·설정한 API만 본다"를 각 규칙 설명에 한 줄로 붙인다.
2. 스캔 단위에 `PropertyDeclaration`(getter/setter/식 본문)·`IndexerDeclaration`을 추가한다. 노드 ID는 기존 프로퍼티 접근자 규칙(`SymbolNames`)을 쓴다.
3. 기본 캐시 패턴을 유지하되 매치된 타입이 알려진 캐시 클라이언트(StackExchange·`IDistributedCache`·`IMemoryCache`·설정한 `cacheWrappers`)가 아니면 접근을 **추정**으로 낮추고 근거에 "이름 패턴 매치"를 적는다.

**검증 방법.** `FixturesExternal`에 Dapper 1건 + EF Core 사용을 섞은 프로젝트를 넣고 notes가 나오는지 확인한다. `FixturesData`에 식 본문 프로퍼티 안의 `Query(...)`를 넣고 접근이 잡히는지 확인한다.

**완료 기준.** 접근이 1건 이상이어도 ORM 힌트가 있으면 notes가 남는다. 프로퍼티 본문의 접근이 잡힌다. 이름 패턴만으로 잡은 캐시는 추정이다.

<a id="t56"></a>
### T56. 트랜잭션 범위·호출자 전파 — COMP 오탐

**목적.** T32와 같은 이유다: "확실"로 틀리는 COMP 판정이 도구 신뢰를 직접 깎는다. 항상 트랜잭션 안에서만 불리는 리포지터리가 자체 쓰기 2개 때문에 COMP-001/002(확실)로 나오고, `if` 안의 `Commit`이 그 뒤 코드까지 닫는다.

**현재 상태(근거).**
- `DataAccessScanner.cs:354-397`: `transactionDepth`가 구문 범위가 아니라 **선형 카운터**다. `if (ok) tx.Commit();` 뒤의 쓰기는 트랜잭션 밖으로, `Complete()` 없는 `TransactionScope`는 메서드 끝까지 안으로 본다.
- `CompensationAnalyzer.cs:113-120`: 접근 요약을 피호출자→호출자로만 전파하고, 호출자의 트랜잭션 문맥을 피호출자에게 내리지 않는다. `:160-188`: `if/else` 배타 분기의 쓰기 2개가 순차 2단계로 집계된다.

**변경할 파일.** `src/TraceLens.Analyzer/DataAccessScanner.cs`, `CompensationAnalyzer.cs`, `Models/DataAccess.cs`(`TransactionContext: Always | Sometimes | Never` 부가 필드), `tests/…/FixturesData`, `client/src/components/DataDetailPanel.tsx`(근거 문구).

**구현 방법.**
1. 트랜잭션 경계를 **구문 범위**로 잡는다: `using var tx = …`/`using (…) {}`/`TransactionScope`는 그 블록 끝까지, `Commit`/`Complete`는 그 블록 안에서 "완료됨" 표시만 한다. `if` 안의 `Commit`은 조건부 완료로 남기고 판정은 "추정"으로 낮춘다.
2. 호출자 전파: 쓰기 경로를 만들 때 대상 메서드의 **모든** 호출자 경로가 트랜잭션 안이면 `Always`, 일부만이면 `Sometimes`, 없으면 `Never`. `Always`면 COMP 규칙을 내지 않고 근거에 "호출자 K개 모두 트랜잭션 안"을 적는다. `Sometimes`면 확실도를 **추정**으로 낮추고 트랜잭션 밖 호출자를 근거에 나열한다. 호출자가 진입점까지 이어지지 않는 메서드(고립)는 `Never`가 아니라 "호출자 없음"으로 따로 적는다.
3. `if/else` 배타 분기는 같은 단계로 센다(분기 트리를 단순화: 형제 분기의 쓰기는 순차가 아님). 루프 안의 쓰기는 지금처럼 둔다.

**검증 방법.** `FixturesData`에 (a) 조건부 Commit, (b) 항상 트랜잭션 래퍼 안에서만 불리는 리포지터리, (c) `if/else` 배타 쓰기를 넣고 COMP 결과 0건 또는 추정을 확인한다. 실제 사내 솔루션에서 COMP-001/002 개수 전후를 CHANGELOG에 적는다(T13처럼 측정값으로).

**완료 기준.** 위 세 경우가 "확실" 오탐을 내지 않는다. 판정 근거에 트랜잭션 문맥(`Always/Sometimes/Never`)과 호출자가 보인다.

<a id="t57"></a>
### T57. 시작 흐름·미들웨어 표시의 정보 손실

**목적.** 시작 흐름의 `UseMiddleware()`가 무엇을 등록했는지 안 보이고(E8), 미들웨어 보기가 같은 문장을 여덟 번 반복하며(E9), 단계 수가 두 곳에서 다르다.

**현재 상태(근거).**
- `FormatCall`(`StartupFlowScanner.cs:375-382`)은 **리터럴 인자만** 남긴다. 제네릭 타입 인자와 변수 인자는 버려져 `UseMiddleware<ApiKeyMiddleware>()` → `UseMiddleware()`, `MapGroup(prefix)` → `MapGroup()`이 된다.
- `app.UseMiddleware(typeof(X))`(비제네릭)는 타입을 못 읽어 Framework로 떨어지고 "본문은 어셈블리 안에 있어 열어 볼 수 없다"는 틀린 설명이 남는다(`MiddlewareScanner.cs:187-194,235`). T31이 없애려던 문구가 이 경로로 남아 있다.
- 미들웨어 파이프라인에서 `EndpointRegistration` 단계 8개가 각각 전체 카드로 그려지고 설명이 같다(`MiddlewareView.tsx:62,92`). 50개 `MapXxx`가 있는 프로젝트면 50장이다. 단락 배지의 조건식이 배지 밖으로 넘친다.
- 좌측 "전체 단계 22개"(`SidePanels.tsx:385`, `countSteps` 재귀)와 가운데 "19단계"(`StartupFlowView.tsx:78`, 최상위만)가 다르다.
- `Startup` 후보가 여럿이면 `startups[0]`만 쓴다(`HostDocuments.cs:85-90`). 두 번째 Startup의 파이프라인이 사유 없이 빠진다.

**변경할 파일.** `src/TraceLens.Analyzer/StartupFlowScanner.cs`, `MiddlewareScanner.cs`, `HostDocuments.cs`, `Models/StartupFlow.cs`(`label`에 타입 인자 포함), `client/src/components/MiddlewareView.tsx`, `StartupFlowView.tsx`, `SidePanels.tsx`, `styles.css`, `tests/…/FixturesData`.

**구현 방법.**
1. `FormatCall`이 제네릭 타입 인자(`<ApiKeyMiddleware>`)와 비리터럴 인자(`prefix` → `prefix`(변수))를 남기게 한다. `typeof(X)` 인자는 타입 이름으로 바꾼다.
2. `UseMiddleware(typeof(X))`·`UseMiddleware(Type)`를 제네릭과 같은 경로로 해석한다. "어셈블리 안" 문구는 `DeclaringSyntaxReferences.Length == 0`일 때만 쓴다(T31 규칙을 이 경로에도 적용).
3. 미들웨어 보기에서 **연속된** `EndpointRegistration` 단계를 한 장으로 접는다: "엔드포인트 등록 8개(`MapControllers`, `MapGroup("/api/v1")` …) — 실제 실행은 파이프라인 맨 끝". 펼치면 개별 줄이 보인다. 단락 배지는 두 줄까지 줄바꿈하고 나머지는 툴팁.
4. 단계 수를 한 정의로 통일한다: 상단은 "최상위 N단계(펼치면 M)", 좌측도 같은 문구.
5. `Startup` 후보가 여럿이면 프로젝트별로 나눠 `startupFlows`/`middlewarePipelines`에 넣고(T65와 연결) 첫 것만 그렸다는 사실을 `limitations`에 남긴다.

**검증 방법.** `FixturesData`에 `UseMiddleware(typeof(...))`·변수 인자 `MapGroup`을 넣고 라벨 기대값을 테스트한다. 수동: 시작 흐름에서 `UseMiddleware<ApiKeyMiddleware>()`가 보인다. 미들웨어 보기가 등록 8개를 한 장으로 접는다.

**완료 기준.** 시작 흐름 라벨에 타입·변수 인자가 있다. 비제네릭 `UseMiddleware`가 본문까지 열린다. 등록 단계가 접힌다. 단계 수가 한 가지다.

<a id="t58"></a>
### T58. 진단에 남지 않는 절단 다섯 가지

**목적.** 상한에 걸려 잘린 것이 로그에만 남으면 화면과 에이전트는 "그게 전부"라고 읽는다. 제약 5의 문제다.

**현재 상태(근거).**

| 절단 | 지금 | 근거 |
|---|---|---|
| 핸들러 노드가 없어 진입점이 목록에서 빠짐(제외 네임스페이스·`obj/` 아래 생성 컨트롤러) | 로그의 "진입점 N개"와 `graph.entryPoints` 수가 어긋나는데 사유 없음 | `TraceLensAnalyzer.cs:184-188` |
| 시작 흐름 400단계 초과 | 로그만 | `StartupFlowScanner.cs:103,133` |
| External 네임스페이스 키 200개 초과 `(기타)` 합산, 상위 20 절단 | 없음 | `CallSiteCollector.cs:33,36` |
| `maxCallDepth`·`depthLimitReached`가 `analysisScope=solution`(기본)에서도 진단에 실림 | "깊이 8까지만 봤나" 오해 | `TraceLensAnalyzer.cs:432-441`, `CallGraphWalker.cs:58` |
| 해석률 분자가 노드 없는 대상으로 버린 엣지·근거를 포함 | 분자 부풀림, 버린 수 미기록 | `CallSiteCollector.cs:48-62,120-125`, `EdgeCollector.cs:25-26` |

**변경할 파일.** `src/TraceLens.Analyzer/TraceLensAnalyzer.cs`, `StartupFlowScanner.cs`, `CallSiteCollector.cs`, `EdgeCollector.cs`, `Models/AnalysisDiagnostics.cs`(`droppedEntryPoints`, `droppedEdges` 부가 필드), `client/src/components/DiagnosticsPanel.tsx`, `src/TraceLens.Cli/CommandRouter.cs`(요약), `docs/data-contract.md`.

**구현 방법.**
1. 각 절단을 `limitations` 한 줄 + 진단 필드로 승격한다. 문구는 "무엇이 몇 개, 왜, 어떻게 늘리나"의 순서(예: "시작 흐름 단계 400개 초과 — 뒤 37개를 그리지 않았다. `startupFlow.maxSteps`로 늘린다").
2. `maxCallDepth`·`depthLimitReached`는 `analysisScope=entryPoints`일 때만 진단에 넣는다.
3. 해석률 분자는 **실제로 엣지가 된 것**만 센다. 버린 수를 `droppedEdges`에 넣고 해석률 문장에 "그래프에 못 넣은 K건 제외"를 붙인다(UI·CLI·문맥·MCP 네 곳 문구 통일 — T28의 문장 규칙 유지).

**검증 방법.** `AnalyzerDiagnosticsTests`에 각 절단을 인위적으로 일으키는 테스트(상한을 작게 주는 설정)를 넣는다. 진단 패널 스크린샷.

**완료 기준.** 다섯 절단이 모두 `limitations`와 진단 패널에 보인다. 해석률 분자가 실제 엣지 수와 일치한다.

<a id="t59"></a>
### T59. 테스트 프로젝트의 구현체가 유일 구현체 판정을 흔든다

**목적.** 테스트 더블(`FakeOrderRepository`)이 솔루션 안 구현체로 세어지면 "구현체 2개"가 되어 DI 유일 구현체 추정이 꺼지고 해석률·엣지가 **조용히** 줄어든다. `excludeProjectNamePatterns` 기본값이 빈 배열이라 대부분의 사용자가 이 상태로 쓴다.

**현재 상태(근거).** `DiResolver.cs:181-190`은 후보 구현체를 프로젝트 구분 없이 센다. `analyze`가 "테스트로 보이는 프로젝트 N개"를 알리지만(T36) 설정을 손대야 한다.

**변경할 파일.** `src/TraceLens.Analyzer/SolutionLoader.cs`(프로젝트에 `IsTestProject` 표시: xunit/nunit/MSTest 패키지 참조 또는 `IsTestProject` MSBuild 속성), `DiResolver.cs`, `Models/AnalysisDiagnostics.cs`(`projects[].isTest`), `tests/…/Fixtures`(테스트 프로젝트 포함 픽스처), `client/src/components/DiagnosticsPanel.tsx`.

**구현 방법.**
1. 로드 시 테스트 프로젝트를 표시한다(제외하지 않는다 — 사용자가 테스트 코드의 호출도 보고 싶을 수 있다).
2. 유일 구현체 판정에서 테스트 프로젝트의 타입을 후보에서 뺀다. 뺀 사실을 근거에 적는다: "테스트 프로젝트의 `FakeOrderRepository`는 후보에서 제외". 후보가 테스트 프로젝트에만 있으면 지금처럼 경계로 남긴다.
3. 진단 패널의 프로젝트 표에 "테스트" 열을 추가한다.

**검증 방법.** 픽스처에 테스트 프로젝트(인터페이스 구현 1개)를 넣고 DI 등록이 없는 인터페이스가 **주 프로젝트의 유일 구현체**로 이어지는지 확인한다. 근거 문구 테스트.

**완료 기준.** 테스트 더블이 유일 구현체 판정을 깨지 않고, 뺀 사실이 근거에 남는다.

<a id="t60"></a>
### T60. `get_findings` 규칙 접두사 버그와 MCP 오류 계약·로그

**목적.** 에이전트 안내문이 시키는 대로 `get_findings(ruleId=COMP-*)`를 부르면 **0건**이 온다. 에이전트는 "보상 처리 문제 없음"이라고 답한다 — 정직성 제약을 어기는 침묵 오답이다. MCP의 오류가 예외 문자열이라 서버와 계약이 다르고 파일 로그가 없다.

**현재 상태(근거).**
- `TraceLensTools.cs:226`은 `string.Equals(item.RuleId, ruleId, OrdinalIgnoreCase)`. 도구 설명(`:210`)은 "접두사(COMP-, TX-, MW-, SHARD-)로 거른다", `McpConfigWriter.cs:59`는 `ruleId=COMP-*`. `McpToolAllowListTests`·`GetFindings_ReturnsDisabledRuleIds`만 있어 잡히지 않았다.
- MCP는 `KeyNotFoundException`(`:144,286,465`)·`ArgumentException`(`:108,448`)·`InvalidDataException`(`GraphFileStore.cs:69`)을 그대로 던진다. `SNAPSHOT_MISMATCH`도 메시지 문자열로만 나간다. 서버는 `{code,message,details}`(`ApiResults.cs`)다.
- `McpHost.cs:18-20`: `ClearProviders` + stderr뿐, 파일 로그 없음. 서버는 `%LOCALAPPDATA%\TraceLens\logs`에 남긴다.

**변경할 파일.** `src/TraceLens.Mcp/TraceLensTools.cs`, `McpHost.cs`, `src/TraceLens.Context/ContextErrors.cs`(신규 — Server와 공유하는 오류 코드 + 메시지), `src/TraceLens.Server/ApiResults.cs`(같은 코드 표 사용), `tests/TraceLens.Mcp.Tests`, `docs/data-contract.md`.

**구현 방법.**
1. `ruleId`를 접두사·와일드카드(`COMP-`, `COMP-*`, `COMP-001`) 모두 받게 한다. 응답에 `matchedRuleIds`를 넣어 무엇에 매치됐는지 보이게 한다. 0건이면 `note`에 "규칙 N개 중 M개는 꺼짐"을 붙인다.
2. MCP 도구는 예외 대신 `{ error: { code, message, details } }`를 돌려준다(MCP 규약의 `isError`도 켠다). 코드 표는 Context에 두고 서버와 MCP가 같은 상수를 쓴다. `ContextException`은 코드를 갖도록 바꾼다.
3. MCP에도 서버와 같은 `FileLoggerProvider`를 붙인다(`logs/tracelens-mcp-YYYYMMDD.log`). stdio를 오염시키지 않게 stderr는 유지한다.
4. 테스트: `get_findings(ruleId:"COMP-")`가 접두사 매치를 돌려준다. 없는 노드 → `NODE_NOT_FOUND` 코드. 서버-MCP 오류 코드 동치 테스트(같은 입력에 같은 code).

**검증 방법.** `dotnet test`의 Mcp 테스트. 수동: Claude Code에서 안내문의 4번 질문("쓰기 경로가 안전한가")을 그대로 던져 결과가 오는지 본다.

**완료 기준.** 안내문의 호출 예가 그대로 동작한다. MCP 오류가 서버와 같은 코드다. MCP 실패가 파일 로그에 남는다.

<a id="t61"></a>
### T61. MCP 호출 비용과 응답 크기

**목적.** 안내문은 "무엇을 묻든 `get_analysis_status`를 먼저"라고 하는데 그 호출이 **솔루션 전체 파일을 다시 해시**한다. `get_context`는 같은 내용을 두 번(구조+Markdown) 보내 에이전트 문맥을 두 배로 쓴다. `compare_snapshots`·`get_impact`·`get_source`에는 상한이 없다.

**현재 상태(근거).**
- `TraceLensTools.cs:33,113`: 도구마다 `new GraphFreshnessEvaluator()`. 해시 캐시는 인스턴스 필드(`GraphFreshnessEvaluator.cs:59`). 서버의 `GraphFreshnessService`(5초 TTL + in-flight 병합)가 MCP에는 없다.
- `ContextBuilder.cs:20-25,195`: 예산은 Markdown 길이만 재고 응답에는 구조 필드 전체도 들어간다(`ContextModels.cs:146-190`).
- `SnapshotComparer.cs:84-96`: 결과에 페이지 없음. `ImpactAnalyzer.cs:164-177`: 결과 endpoints 수 상한 없음. `SourceAccessPolicy.cs:41-44`: 4000줄/512KB까지 한 번에.

**변경할 파일.** `src/TraceLens.Context/GraphFreshnessCache.cs`(신규 — `GraphFreshnessService`의 캐시 부분을 Context로 내리고 Server·MCP가 공유), `src/TraceLens.Server/GraphFreshnessService.cs`, `src/TraceLens.Mcp/TraceLensTools.cs`, `GraphFileStore.cs`, `ContextBuilder.cs`, `SnapshotComparer.cs`, `ImpactAnalyzer.cs`, `docs/data-contract.md`.

**구현 방법.**
1. 신선도 캐시를 Context로 옮기고 MCP 세션이 하나를 재사용한다. 파일 mtime+size가 같으면 해시를 재계산하지 않는다(이미 있으면 그대로). `get_analysis_status`에 `freshnessCheckedAt`·`checkDurationMs`를 넣어 비용을 보이게 한다.
2. `get_context`에 `format: "markdown" | "json" | "both"`(기본 `markdown`)를 추가한다. 예산은 **실제로 돌려주는 바이트** 기준으로 잰다.
3. `compare_snapshots`·`get_impact`에 `limit`/`cursor`(기존 `Page` 헬퍼 재사용)를 붙이고 `total`을 함께 준다. `get_source`는 기본 `maxLines=400`, 넘으면 `truncated: true`와 다음 시작 줄.
4. 절삭이 일어나면 응답에 항상 `truncated`·`omitted`가 있어야 한다는 규칙을 계약 문서에 적는다.

**검증 방법.** Mcp 테스트: 같은 그래프로 `get_analysis_status` 두 번 → 두 번째는 해시 재계산 0회(카운터로 확인). `get_context(format:"markdown")` 응답에 구조 필드가 없다. 페이징 테스트.

**완료 기준.** 연속 호출에서 파일 해시를 다시 계산하지 않는다. `get_context` 기본 응답이 Markdown 하나다. 상한 없는 도구가 없다.

<a id="t62"></a>
### T62. UI에는 있고 MCP에는 없는 것 + 도구 계약 문서

**목적.** 여섯 보기 중 셋(미들웨어·시작 흐름·클래스 다이어그램)과 쓰기 경로·스냅샷 목록을 에이전트가 볼 수 없다. `get_context`가 받는 selectionKind 8종 중 5종은 id를 찾을 도구가 없다. 도구 12개의 계약이 정본 문서에 없다.

**현재 상태(근거).**
- `ContextModels.cs:6-14`의 `class`·`namespace`·`writePath`·`middlewareStep`·`startupStep`은 발견 도구가 없다. `compare_snapshots`(`TraceLensTools.cs:273`)는 `baselineId`를 요구하는데 목록 도구가 없다(서버에는 `GET /api/snapshots`).
- `data-contract.md`는 "HTTP·MCP 계약을 한곳에"라고 선언(`:3`)하지만 MCP는 `get_analysis_status` 부가 필드 둘(`:64-65`)뿐이다. 유일한 목록은 `mcp-setup_kr.md:153-195`(산문).

**변경할 파일.** `src/TraceLens.Mcp/TraceLensTools.cs`, `src/TraceLens.Context/GraphIndex.cs`(조회 함수는 여기 — Server 엔드포인트도 같은 것을 쓴다), `src/TraceLens.Server/Endpoints/*`(대응 엔드포인트가 없으면 추가), `docs/data-contract.md`, `docs/mcp-setup*.md`, `tests/TraceLens.Mcp.Tests`, `tests/TraceLens.Server.Tests`(문서-코드 동치 테스트).

**구현 방법.**
1. 도구 추가(각각 페이징·상한 포함): `list_snapshots`, `get_middleware_pipeline(projectKey?)`, `get_startup_flow(projectKey?)`, `get_class(classId)`(멤버·관계·노드 ID), `list_classes(query, namespace?)`, `list_write_paths(entryPointId?)`. 각 결과 항목에 `get_context`에 넣을 `selectionKind`·`id`를 그대로 담는다.
2. `get_entrypoints`에 `method`(동사)·`pathPrefix`·`projectKey` 필터를 분리한다.
3. `data-contract.md`에 "§6 MCP 도구" 절을 만들어 도구별 이름·파라미터·페이징·상한·오류 코드를 표로 적는다. `[McpServerTool]` 리플렉션으로 도구 이름·파라미터 목록을 뽑아 문서의 표와 비교하는 테스트를 추가한다(`McpToolAllowListTests`와 같은 방식). 허용 목록(`AskEndpoints`)은 리플렉션이라 자동으로 따라온다.
4. 안내문(`McpConfigWriter`)의 라우팅 힌트에 새 도구를 넣는다("미들웨어 순서" → `get_middleware_pipeline`).

**검증 방법.** 문서-코드 동치 테스트 통과. Claude Code에서 "이 API 요청이 어떤 미들웨어를 지나는가"를 물어 답에 파일:줄이 나오는지 수동 확인.

**완료 기준.** UI의 여섯 보기가 모두 MCP에서 열거된다. `get_context`의 8종 selectionKind 모두 id를 찾는 도구가 있다. 도구 계약이 문서에 있고 테스트가 지킨다.

<a id="t63"></a>
### T63. 스크립트용 CLI 명령(`impact`·`context`·`compare`)과 종료 코드

**목적.** 서버를 띄우지 않으면 영향 분석·문맥 생성·비교를 자동화할 수 없다. "PR에서 바뀐 메서드의 영향 진입점을 코멘트로 단다" 같은 가장 실용적인 흐름이 막혀 있다. 종료 코드가 문서에 없다.

**현재 상태(근거).** `CommandRouter.cs:24-36`의 명령은 `doctor|analyze|serve|mcp|demo|init|config`. 종료 코드는 코드상 0/1/2(`:64,77,87,135,187,388`)인데 README·계약 문서에 없다. `analyze`에 `--output`·`--config`가 없다.

**변경할 파일.** `src/TraceLens.Cli/CommandRouter.cs`, `Commands/ImpactCommand.cs`·`ContextCommand.cs`·`CompareCommand.cs`(신규, 모두 Context 라이브러리만 사용), `README_kr.md`·`README.md`, `docs/data-contract.md`, `tests/TraceLens.Server.Tests`(CLI 테스트 프로젝트가 없으면 Cli 테스트 프로젝트 신설).

**구현 방법.**
1. `tracelens impact <대상> --node <id|"Class.Method"> [--json|--markdown]` — `ImpactAnalyzer` 직접 호출. `--changed-files a.cs,b.cs`를 주면 그 파일의 메서드 전부를 대상으로(다중 대상 API는 이미 있다).
2. `tracelens context <대상> --node … --intent … --budget … [--markdown]` — 클립보드 대신 stdout.
3. `tracelens compare <대상> --baseline <snapshotId|latest> [--json]` — `SnapshotComparer`. 전제 조건 불일치는 종료 코드 3.
4. `analyze --output <폴더>`·`--config <파일>`. 종료 코드 표(0 성공 / 1 실패 / 2 인자 오류 / 3 전제 조건 불일치 / 4 stale 그래프)를 `--help`와 README·계약 문서에 적는다. `--fail-on` 류 게이트는 넣지 않는다(정확도 수치를 임계값으로 쓰는 것은 제약 5와 충돌할 여지가 있어 사용자 판단으로 남긴다).

**검증 방법.** Cli 테스트: 픽스처 그래프로 세 명령의 JSON 출력과 종료 코드. README의 CLI 표 갱신.

**완료 기준.** 서버 없이 세 명령이 동작한다. 종료 코드가 문서와 `--help`에 있다.

<a id="t64"></a>
### T64. 문맥·영향·비교의 내용 공백

**목적.** 문맥에 "이 API가 어느 테이블을 읽고 쓰는가"와 "어떤 미들웨어를 지나는가"가 없다. 절삭되면 "피호출자 없음"과 구분되지 않는다. 영향에 DB·캐시 쓰기가 없고 비교는 미들웨어 순서 변경을 "차이 없음"이라고 한다.

**현재 상태(근거).**
- `ContextBuilder.cs:387-396`: DB·캐시는 해당 노드의 `Finding`만. `dataAccess.accesses`는 문맥에 없다. 미들웨어는 `middlewareStep`을 직접 고를 때만(`:541-554`). 인접은 1홉(`:361-385`).
- 예산 초과 시 `callees`/`callers`를 통째로 비우고 `omitted.sections`에 이름만 남긴다(`:205-257`). 몇 개가 빠졌는지 없다.
- `ImpactAnalyzer.cs:10`: `TimeoutMs 5000` 고정, API·MCP에 노출 안 됨. 결과는 진입점 + 테스트 후보뿐.
- `SnapshotComparer`: 미들웨어 파이프라인·startupFlow·클래스 구조·writePaths·진단 변화는 비교하지 않는다.
- stale이면 소스 본문을 뺀다(`:57-66`) — 정직하지만 대안 문구가 없다.

**변경할 파일.** `src/TraceLens.Context/ContextBuilder.cs`, `ContextModels.cs`, `ContextMarkdown.cs`, `ImpactAnalyzer.cs`, `SnapshotComparer.cs`, `src/TraceLens.Server/Endpoints/ImpactEndpoints.cs`·`ContextEndpoints.cs`, `src/TraceLens.Mcp/TraceLensTools.cs`, `client/src/components/ContextPreview.tsx`·`SnapshotDiffPanel.tsx`, `docs/data-contract.md`.

**구현 방법.**
1. 문맥에 "저장소 접근" 절: 대상 노드에서 도달하는 접근(테이블·연산·확실도, 최대 20, 넘치면 개수). endpoint 문맥에는 "요청 경로의 미들웨어" 절(파이프라인 순서 + 단락 지점, 최대 10). `neighborDepth: 1|2` 옵션.
2. 절삭 시 `omitted.counts: { callers: n, callees: m, snippets: k }`를 남기고 Markdown에도 "호출자 12개 중 8개 생략"으로 적는다. stale이면 "재분석 후 다시 요청하면 소스가 포함된다"를 `limitations`에 넣는다(재분석 도구는 만들지 않는다).
3. `ImpactApiRequest`·MCP `get_impact`에 `timeoutMs`(상한 60000). 결과에 `dataWrites`(대상에서 전방 도달하는 쓰기 접근 — 접근 요약 전파를 재사용)를 추가한다.
4. `SnapshotComparer`에 미들웨어 순서·단락 변화, 시작 흐름 등록 변화, writePaths 변화를 추가한다. 비교하지 않는 것은 `notCompared` 목록으로 응답에 남긴다.

**검증 방법.** `ContextLibraryTests`에 접근 절·미들웨어 절·절삭 개수 테스트. `SnapshotComparerTests`에 미들웨어 순서 변경 → 차이 1건. 서버-MCP 동치 테스트에 새 절을 포함.

**완료 기준.** endpoint 문맥에 접근·미들웨어 절이 있다. 절삭이 개수로 남는다. 영향에 쓰기 접근이 있고 타임아웃을 조정할 수 있다. 비교가 미들웨어·시작 흐름 변화를 잡고 비교하지 않는 것을 말한다.

<a id="t65"></a>
### T65. 실행 프로젝트가 여럿인 솔루션

**목적.** Api + Worker + Gateway처럼 실행 프로젝트가 여럿이면 시작 흐름·미들웨어가 **첫 프로젝트 하나만** 보이고 진입점은 프로젝트 구분 없이 섞인다.

**현재 상태(근거).** `api/client.ts:130-135`가 `startupFlows`/`middlewarePipelines`를 채우지만 화면은 `graph.startupFlow`/`graph.middleware` 단수만 본다(`App.tsx:296,1836,1929,1950`). `EntryPoint.projectKey`는 참조 0회(`api/client.ts:177`). `manifest`(`inputs`·`sourceRoots`·`excludedDirectories`)도 화면에 없다(`api/client.ts:142`가 유일 참조).

**변경할 파일.** `client/src/App.tsx`, `components/StartupFlowView.tsx`, `MiddlewareView.tsx`, `SidePanels.tsx`, `DiagnosticsPanel.tsx`, `state/viewState.ts`, `i18n`.

**구현 방법.**
1. 시작 흐름·미들웨어 보기 상단에 프로젝트 선택(드롭다운, 하나면 숨김). 선택은 `PersistedState.projectKey`와 URL 해시에 저장한다.
2. 진입점 목록에 프로젝트 그룹 레벨을 추가한다(프로젝트가 둘 이상일 때만, T43의 `groupBy`에 `project` 추가).
3. 진단 패널에 `manifest` 절: 입력 파일 수·소스 루트·제외 폴더·링크된 소스. "왜 이 프로젝트가 빠졌나"에 답한다.

**검증 방법.** 실행 프로젝트 2개짜리 픽스처(`FixturesClassic` 확장)로 `startupFlows.length === 2`를 확인하고 UI에서 전환한다.

**완료 기준.** 프로젝트가 여럿이면 시작 흐름·미들웨어를 골라 볼 수 있고 진입점이 프로젝트로 묶인다. 매니페스트가 진단에 보인다.

<a id="t66"></a>
### T66. stale·서버 다운·취소를 화면이 말하게 한다

**목적.** 코드를 고친 직후가 도구를 가장 많이 쓰는 순간인데, 지금 보는 소스가 분석 당시 것인지 현재 파일인지 표시가 없다. 서버가 죽어도 배지만 바뀌고, 분석 취소는 성공인지 실패인지 말하지 않는다.

**현재 상태(근거).**
- `SourceResponse.sourceVersion`/`truncated`(`types/graph.ts:400-402`)를 `CodePanel.tsx`가 쓰지 않는다. `FreshnessResponse.changes`(`Modified/Added/Deleted/ConfigChanged`)는 참조 0회. `FreshnessBadge.tsx:70-79`는 바뀐 파일 8개를 `title`에만 넣는다.
- stale 전용 문구가 보기 안에 없다(헤더 배지뿐). 서버가 죽으면 `FreshnessBadge.tsx:40-41`이 `unavailable`로 배지만 바꾸고 `error.NETWORK`(`ko.ts:749`)는 이 경로에서 쓰이지 않는다.
- 분석 취소는 `App.tsx:644-649`에서 조용히 `return`. 완료 문구 키가 없다.

**변경할 파일.** `client/src/components/CodePanel.tsx`, `FreshnessBadge.tsx`, `components/FreshnessDetail.tsx`(신규), `App.tsx`, `i18n`.

**구현 방법.**
1. 코드 패널 머리에 "분석 당시 스냅샷 / 현재 파일(분석 뒤 바뀜)"을 표시한다. stale 파일이면 줄 번호가 어긋날 수 있다는 한 줄.
2. 신선도 배지 클릭 → 상세 팝오버: 바뀐 파일을 종류별(수정·추가·삭제·설정)로 나열하고 각 항목이 `파일:줄` 링크다. "재분석" 버튼을 여기에도 둔다(사용자 실행, 제약 3).
3. stale이면 각 보기 상단에 얇은 배너 "옛 결과를 보고 있다 — N개 파일이 바뀜 · [재분석]". 배너 스택(T41)에 넣는다.
4. 서버 응답 실패(네트워크)를 한곳에서 잡아 "서버에 연결할 수 없다 — `tracelens serve`가 실행 중인지 확인" 배너를 띄우고 복구되면 자동으로 지운다(재시도 버튼, 폴링은 신선도 틱에 얹는다).
5. 취소 완료 시 "분석을 취소했다. 이전 결과를 그대로 본다" 토스트.

**검증 방법.** 수동: 분석 → 파일 수정 → 배지 클릭 → 파일 목록 → 코드 패널이 "현재 파일"을 표시. 서버 종료 → 배너 → 재시작 → 배너 사라짐. 분석 중 취소 → 토스트.

**완료 기준.** 보고 있는 코드의 버전이 화면에 있다. 바뀐 파일 목록을 눌러 볼 수 있다. 서버 다운과 취소가 문구로 보인다.

<a id="t67"></a>
### T67. 성능 — 드래그 리렌더·타이핑마다 재요청·전량 스캔·가상화

**목적.** 300개 엔드포인트·수천 노드 솔루션에서 체감되는 네 곳이다. 측정 먼저(T18 원칙).

**현재 상태(근거).**
- `Splitter`가 `pointermove`마다 `setColumns`(`App.tsx:1868,1980`) → 저장 effect(`App.tsx:315-337`, 전체 상태 `JSON.stringify`) + `measureShell`→`shellWidth`→`diagramOptions`(`:261-272`)→`buildClassDiagram`. `Splitter.tsx:11-14` 주석("드래그 중에는 CSS 변수만")과 다르다.
- `AskPanel.tsx:167-199` effect 의존에 `question` → 200ms 디바운스로 타이핑마다 `POST /api/context`(서버가 소스를 읽는다).
- `DetailPanel.tsx:294-295,326-329`가 선택마다 edges·callSites를 비메모로 2회씩 훑고 `:299-323`이 incoming 맵을 다시 만든다(App에 `incomingByNode` `App.tsx:203-212`가 이미 있다).
- 가상화 없음: 접근 지도 매트릭스(`DataAccessView.tsx:283-314`, rows×targets 전량), 미들웨어 좌측 목록, 상세의 callers/callees pill 전량(`DetailPanel.tsx:431-438`), 근거 목록 전량(`:524-551`).
- `getDiagnostics`를 App과 AskPanel이 각각 호출(`App.tsx:760`, `AskPanel.tsx:148`).

**변경할 파일.** `client/src/components/Splitter.tsx`, `App.tsx`, `AskPanel.tsx`, `DetailPanel.tsx`, `DataAccessView.tsx`, `package.json`(가상화는 의존성 없이 "처음 50개 + 더 보기"로 한다).

**구현 방법.**
1. 측정: React Profiler로 스플리터 드래그 1초·노드 선택·질문 타이핑의 커밋 수/시간을 기록해 CHANGELOG에 전후를 적는다.
2. 스플리터는 드래그 중 CSS 변수만 바꾸고 `pointerup`에서 한 번 `setColumns`. 저장 effect는 300ms 디바운스.
3. 질문 텍스트는 문맥 요청에서 빼고(문맥은 선택·의도·예산에만 의존) 보내는 시점에 합친다.
4. `DetailPanel`이 App의 `incomingByNode`·`outgoingByNode`·`callSitesByEdge`를 props로 받고 `useMemo`로 감싼다.
5. 큰 목록은 "처음 50개 + 더 보기(N)"로 자르고 검색칸을 붙인다. 접근 지도는 행·열 각각 50 상한 + 필터.

**검증 방법.** 측정값이 CHANGELOG에 있다. `DetailPanel.test.tsx`(신규)로 pill 상한. 수동: 드래그가 부드럽고 타이핑 중 네트워크 요청이 없다.

**완료 기준.** 드래그 중 앱 전체 리렌더가 없다. 타이핑이 요청을 만들지 않는다. 상세가 App의 색인을 재사용한다. 어떤 목록도 한 번에 50개를 넘게 그리지 않는다.

<a id="t68"></a>
### T68. 접근성 — 포커스 표시·모달 트랩·단축키 목록

**목적.** T14가 기본을 깔았지만 포커스 표시가 브라우저 기본에만 의존하고 모달이 포커스를 가두지 않는다.

**현재 상태(근거).** `styles.css`에 `:focus`/`:focus-visible` 규칙 0건, `@media` 0건. `role="button" tabIndex={0}` 커스텀 요소가 많다(`UmlClassBox.tsx:58-59`, `FlowDiagram.tsx:83-84`, `DataAccessView.tsx:628`, `Splitter.tsx:65-66`). `HelpDialog`·`QuickFind`·`McpSetupDialog`·`DiagnosticsPanel`에 포커스 트랩이 없고 복원은 `PathPicker`·`ShareReportDialog`만 한다. `window.alert()`(`GuidedWalkthrough.tsx:57`). 단축키 목록(`HelpDialog.tsx:28-34`)에 Shift+클릭(선택만)·Shift/우클릭(에디터)·Splitter Shift+화살표가 없다. 진입점·클래스 목록에 화살표 이동이 없다.

**변경할 파일.** `client/src/hooks/useModal.ts`(신규 — Esc·포커스 트랩·복원·`aria-modal`), 모든 대화상자 컴포넌트, `styles.css`, `HelpDialog.tsx`, `GuidedWalkthrough.tsx`, `SidePanels.tsx`.

**구현 방법.**
1. `useModal` 훅 하나로 대화상자 6개를 통일한다(T48의 환경 점검 포함).
2. `:focus-visible` 공통 스타일(2px 링, 두 테마 대비 확인). `@media (prefers-reduced-motion)`으로 펄스·전환 제거.
3. `window.alert`를 배너 스택 토스트로 바꾼다.
4. 단축키 표를 실제 구현에서 뽑는다: `shortcuts.ts`에 정의를 두고 각 컴포넌트가 그 정의를 읽어 바인딩·툴팁·도움말 표를 모두 생성한다(어긋날 수 없게).
5. 목록에 `↑/↓/Enter/Home/End` 이동(roving tabindex).

**검증 방법.** 키보드만으로 진입점 선택 → 노드 상세 → 문맥 복사 → 도움말 열고 닫기까지 간다. 각 모달에서 Tab이 뒤 화면으로 새지 않는다.

**완료 기준.** 모든 모달이 같은 훅을 쓴다. 포커스 링이 두 테마에서 보인다. 단축키 표가 정의에서 생성된다.

<a id="t69"></a>
### T69. 내보내기·북마크·다중 대상 영향

**목적.** 회의·PR에 붙일 그림과 목록, 어제 보던 자리, "이 3개를 같이 고치면"이 없다.

**현재 상태(근거).** 그래프 JSON 저장(`App.tsx:703-714`)과 공유 Markdown(`ShareReportDialog.tsx:59-68`)만 있다. 클래스/흐름도는 DOM+SVG라 SVG 내보내기가 가능하다. 저장되는 최근 항목은 대상 경로 5개뿐(`state/viewState.ts:68,83`). 영향 API는 `nodeIds: string[]`(`api/client.ts:250`)인데 UI는 1개(`ImpactPanel.tsx:32`); `ImpactResult.targets`·`unreachableTargets`는 참조 0회.

**변경할 파일.** `client/src/components/DiagramCanvas.tsx`, `ImpactPanel.tsx`, `DataAccessView.tsx`, `App.tsx`, `state/viewState.ts`, `graph/exportSvg.ts`(신규), `i18n`.

**구현 방법.**
1. 클래스 다이어그램·전체 흐름도에 "SVG 저장"(현재 필터·토글 상태 그대로, 인라인 스타일 포함). 호출 흐름은 Cytoscape `png()`. 점검 결과 표·영향 목록·쓰기 경로에 "Markdown 복사".
2. 북마크: 선택 항목에 ★ → `PersistedState.bookmarks`(대상별 최대 50). 통합 찾기 빈 입력에 북마크·최근 본 노드 10개.
3. 영향 패널에 "대상 추가"(현재 선택을 목록에 쌓는다) → `getImpact(nodeIds)`. 결과에 `targets`별 도달 여부와 `unreachableTargets`를 표시한다.

**검증 방법.** SVG를 브라우저와 VS Code에서 열어 깨지지 않는지 본다. `viewState.test.ts`에 북마크 상한. 수동: 대상 3개 영향.

**완료 기준.** 세 캔버스에서 그림을 저장할 수 있다. 목록을 Markdown으로 복사할 수 있다. 북마크와 다중 대상 영향이 동작한다.

<a id="t70"></a>
### T70. 인앱 실행 에이전트 확장과 MCP 호스트 추가

**목적.** `doctor`는 `codex`를 찾아 놓고도(`EnvironmentDiagnostics.cs:57`) 인앱 실행은 `claude` 고정이라 Codex·Cursor 사용자는 "AI에게 질문"의 실행 버튼을 못 쓴다. 모델을 고를 수 없고 CLI 인자 검증이 없다. MCP 설정 호스트가 4종·원라이너 2종이다.

**현재 상태(근거).** `ServerHost.cs:79`에 `ClaudeCodeAgentRunner` 싱글턴. `AgentRunner.cs:38-49`의 인자(`--permission-mode dontAsk`, `--no-session-persistence` 등)는 `AskEndpointsTests.cs:47`가 `--strict-mcp-config` 하나만 검증한다. `McpConfigWriter.BuildPreview`(`:216-241`)는 Claude Code·Codex CLI·VS Code·Cursor, 원라이너(`:257-261`)는 Claude Code·Codex뿐이다.

**변경할 파일.** `src/TraceLens.Server/AgentRunner.cs`(`IAgentRunner` 구현 추가: `CodexAgentRunner`), `ServerHost.cs`(등록된 러너 목록), `Endpoints/AskEndpoints.cs`(`agent`·`model` 파라미터), `McpConfigWriter.cs`, `client/src/components/AskPanel.tsx`·`McpSetupDialog.tsx`, `docs/mcp-setup*.md`, `tests`.

**구현 방법.**
1. `IAgentRunner`를 이름 있는 러너 집합으로 바꾸고 `doctor`가 찾은 CLI만 UI 드롭다운에 보인다. Codex는 `codex exec` + MCP 설정 파일로 같은 허용 목록을 전달한다(Codex의 실제 플래그는 `codex --help`로 확인해 테스트에 박는다).
2. `model` 선택(빈 값 = CLI 기본). 러너별 인자 조립을 순수 함수로 빼고 **실제 CLI의 `--help` 출력과 대조하는 테스트**를 추가한다(CLI가 없으면 skip, 있으면 인자가 도움말에 존재해야 통과). 이것으로 §2.4의 "`--permission-mode dontAsk` 확인 필요"를 닫는다.
3. MCP 설정 호스트에 Windsurf(`~/.codeium/windsurf/mcp_config.json`)·Gemini CLI(`~/.gemini/settings.json`)·Claude Desktop을 추가하고 VS Code(`code --add-mcp`)·Cursor 원라이너를 붙인다. 경로는 항상 따옴표.

**검증 방법.** 러너 인자 테스트. 수동: Codex CLI가 설치된 환경에서 "Codex로 실행" → 답변 스트리밍. 새 호스트 조각을 실제 파일에 붙여 MCP가 뜨는지 확인(설치된 것만).

**완료 기준.** 설치된 에이전트 CLI를 골라 실행할 수 있다. 인자가 CLI 도움말과 대조된다. 호스트 7종의 설정 조각이 있다.

<a id="t71"></a>
### T71. MVC 필터·`[Authorize]` 정책을 미들웨어 보기에 표시한다

**목적.** 2차 문서 §2.4가 열거하고 손대지 않은, 실행 순서 보기의 가장 큰 빈칸이다. 요청이 컨트롤러에 닿기 전에 **필터**와 **인가 정책**이 무엇을 하는지가 보이지 않는다. 미들웨어 보기는 별도 분석(제약 2 예외)이므로 이 확장은 범위 안이지만, 표시 방식은 시작 전에 확정한다.

**현재 상태(근거).** 저장소에 `IActionFilter`·`IAsyncActionFilter`·`IExceptionFilter`·`AuthorizationPolicy` 문자열 0건. `MiddlewareScanner.cs:832-855`는 `MapGroup` 단위 `AddEndpointFilter`·`RequireAuthorization`를 칩으로만 보인다. `AddAuthorization(o => o.AddPolicy("Admin", …))`의 정책 정의는 시작 흐름의 등록 줄로만 보인다.

**변경할 파일.** `src/TraceLens.Analyzer/FilterScanner.cs`(신규), `MiddlewareScanner.cs`, `Models/MiddlewarePipeline.cs`(`filters` 부가 절), `Models/EntryPoint.cs`(`authorization: { policy, roles, allowAnonymous, confidence }` 부가 필드), `client/src/components/MiddlewareView.tsx`·`DetailPanel.tsx`, `docs/data-contract.md`, `tests/…/FixturesData`.

**구현 방법.**
1. **범위 확정(사용자와 합의 후 진행).** 제안: (a) 전역 필터(`AddControllers(o => o.Filters.Add<T>())`)와 컨트롤러/액션 특성 필터(`[ServiceFilter]`·`[TypeFilter]`·필터 특성 파생)를 **"필터" 절**로 파이프라인 아래에 순서대로 표시 — 미들웨어 단계와 섞지 않는다. (b) `[Authorize(Policy/Roles)]`·`[AllowAnonymous]`·`RequireAuthorization()`을 진입점 상세와 목록 배지에 표시하고, 정책 이름은 `AddPolicy` 등록 줄로 링크. (c) 필터 본문은 `InvokeAsync`처럼 요청/응답 구간·단락(`context.Result = …`)을 읽는다.
2. 상속·인터페이스로 판정하며 확정 못 하면 추정. 정책 이름이 상수가 아니면 `?`에 추정.
3. README "지금 읽지 않는 것" 표에서 해당 행을 옮긴다.

**검증 방법.** 픽스처에 전역 필터 1·액션 필터 1·`[Authorize(Policy="Admin")]`·`[AllowAnonymous]`를 넣고 기대 순서·정책을 테스트한다.

**완료 기준.** 미들웨어 보기에 필터 절이 있고 진입점에 인가 배지가 있다. 확정 못 한 것은 추정으로 표시된다. 지원 표가 갱신됐다.

<a id="t72"></a>
### T72. 정합성 묶음 — 버전·오류 형태·죽은 계약·i18n 검사 범위·문서

**목적.** 하나하나는 작지만 남겨 두면 다음 점검에서 또 나오는 것들이다.

**현재 상태(근거)와 할 일.**
1. **버전.** `Directory.Build.props`가 `0.8.0`인데 CHANGELOG는 `0.9.0 (Unreleased)`. 헤더·`graph.json`·스냅샷 전제에 `0.8.0`이 찍힌다. → `0.9.0`으로 올리고 CHANGELOG의 "확인한 것" 절에 이 문서(T43~)를 링크한다.
2. **오류 형태.** 잘못된 JSON 본문 → 본문 없는 400(E12). `ApiResults.Error`(신형)와 `LegacyError`(구형)가 같은 엔드포인트 안에서도 갈린다(`AnalyzeEndpoints.cs:107` vs `:116`). → 모델 바인딩 실패를 `{code: "INVALID_REQUEST"}`로 감싸는 필터, 파일 단위로 신형 통일(구형은 `error` 필드를 유지하는 규칙대로).
3. **죽은 계약.** 클라이언트가 안 부르는 `GET /api/graph/classes`·`GET /api/analysis-status`·`GET /api/analysis-jobs`(목록·단건). `GraphClassSummary`·`AnalysisStatus`·`AnalysisCapabilities`·`GraphDocument.fileHashes`·`TransactionScopeInfo.accessIds`·`MiddlewareStepInfo.isTerminal/implementationLine`·`EndpointFilterInfo.statusCode`·`ContextDocument.contextVersion` 참조 0회. → 쓸 곳이 있으면(예: `analysis-jobs` 목록으로 새로고침 뒤 진행 중 작업 재접속 — T66에 넣을 만함) 쓰고, 없으면 지우고 계약 문서에서도 뺀다. MCP와 공유하는 것은 남긴다.
4. **i18n 검사 범위.** `noHardcodedKorean.test.ts:20`의 glob이 `components`·`content`·`App.tsx`뿐이라 `graph/flowMap.ts:115,140,232`·`graph/classDiagram.ts:69`의 한국어와 `App.tsx:1298`의 영어가 새어 나간다. `DataDetailPanel.tsx:288`은 `rule?.name`(한국어 원문), 같은 화면의 목록은 `ruleText(rule, lang)`. 흐름도의 `Method · 메서드 4`(종류 폴백 라벨)도 같은 부류. → glob을 `src/**`로 넓히고 `graph/`는 i18n 키만 돌려주게 한다(`dataView.ts` 방식). 규칙 이름은 한곳에서.
5. **문서.** `data-contract.md`에 `GET /api/analysis-jobs`·`GET /manual/{lang}`·`MANUAL_NOT_FOUND`가 없다. `pack.ps1`이 `docs/`(매뉴얼·스키마)를 검증하지 않아 빠져도 조용히 통과한다(`:120-135`). → 표 갱신, 검증 목록에 추가.
6. **App.tsx 2112줄.** 상태 관리 원칙(App에서 관리, 라이브러리 도입 안 함)은 지키되 보기별 상태와 핸들러를 `hooks/useCallFlowState.ts` 등으로 나눈다. 기능 변경 없음, 테스트 통과가 기준.

**검증 방법.** `dotnet test`·`npm test` 통과. 계약 문서의 엔드포인트 표를 `Endpoints/*.cs`의 `Map*`과 대조하는 테스트(문자열 grep 수준이면 충분).

**완료 기준.** 버전이 맞는다. 모든 400이 `{code,message}`를 갖는다. 참조 0회 타입·엔드포인트가 없다(또는 쓰인다). i18n 검사가 `src/**`를 본다. 계약 문서가 코드와 같다.

---

## 4. 기록

- 스크린샷 스크립트(puppeteer-core + 로컬 Chrome)는 이번에는 임시 폴더에서만 썼다. T44에서 저장소에 넣는다.
- 이번 점검에서 분석기 결과 수치(자기 순환 엣지·COMP 오탐 개수)는 실제 사내 솔루션으로 재지 않았다. T53·T56 완료 시 전후를 CHANGELOG에 적는다.
