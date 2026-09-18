# TraceLens 신입 개발자 사용성 점검과 다음 작업 (2차)

작성 기준: 2026-09-17 16:57 KST, 커밋 `01c199c` (T1~T19 완료 직후).
이 문서는 [`next-tasks_kr.md`](next-tasks_kr.md)의 T1~T19를 모두 끝낸 상태에서 **"신입 개발자가 이 도구를 혼자 설치하고, 분석하고, 결과를 읽고, AI 에이전트에게 도움을 받을 수 있는가"**를 기준으로 다시 점검한 결과다.
점검 방법은 세 갈래다. (1) 클라이언트(`client/src`) 전 컴포넌트·i18n 문구를 신입 관점으로 읽기, (2) 설치·CLI·MCP·문서의 첫 사용 경로 추적, (3) 분석기가 인식하지 못하는 실전 ASP.NET Core 패턴 조사. 근거는 모두 `파일:줄`로 적었고, 실행으로 확인한 것과 코드 읽기로 추론한 것을 구분했다.

> **사용 규칙.** 태스크 하나를 끝낼 때마다 아래 목록의 체크박스를 즉시 `[x]`로 바꾸고 완료 일시(KST, 시스템 명령으로 확인)를 덧붙인다.
> 여러 개를 몰아서 체크하지 않는다. 상세 절의 "완료 기준"을 모두 만족했을 때만 체크한다.
> 번호는 1차 문서(T1~T19)에 이어 **T20부터** 시작한다.

---

## 태스크 목록

### P0 — 신입이 첫 30분 안에 막히는 것

- [x] [T20. 첫 사용 안내를 분석 전에도 보이게 하고 첫 방문에 자동으로 연다](#t20) — 완료 2026-09-17 19:25 KST
- [x] [T21. 용어집·단축키·사용 설명서를 앱 안에서 연다](#t21) — 완료 2026-09-17 19:36 KST
- [x] [T22. 설명 없는 기호·규칙 ID·영어 원문을 정리한다](#t22) — 완료 2026-09-17 19:42 KST
- [x] [T23. 실패했을 때 다음 행동으로 가는 길을 만든다](#t23) — 완료 2026-09-17 19:48 KST
- [x] [T24. 분석 진단 패널 — 무엇을 보고 무엇을 못 봤는지 한 화면에](#t24) — 완료 2026-09-17 19:51 KST (T28·T29 필드는 그 태스크에서 채운다)
- [x] [T25. 질문 패널에 프리셋 질문·목적 설명·답변 보관을 넣는다](#t25) — 완료 2026-09-17 19:53 KST
- [x] [T26. MCP 연동을 붙여넣기 한 번으로 끝낸다](#t26) — 완료 2026-09-17 19:58 KST
- [x] [T27. `demo`와 예제 안내를 바로잡는다](#t27) — 완료 2026-09-17 20:06 KST

### P1 — 분석기가 "못 본 것"을 말하게 한다 (정직성)

- [x] [T28. 외부(External) 호출 집계와 DB·캐시 notes — 인식하지 않은 입력을 진단에 남긴다](#t28) — 완료 2026-09-17 20:13 KST
- [x] [T29. HTTP 밖 진입점 후보를 세어 알린다](#t29) — 완료 2026-09-17 20:18 KST
- [x] [T30. 클래식 `Startup.cs` 호스트를 인식한다](#t30) — 완료 2026-09-17 20:24 KST
- [x] [T31. 솔루션 안 미들웨어 확장 메서드를 펼치고 틀린 설명을 없앤다](#t31) — 완료 2026-09-17 20:28 KST
- [x] [T32. "확실"로 틀리는 DB 판정 두 가지(`TransactionScope`·Dapper 저장 프로시저)](#t32) — 완료 2026-09-17 20:34 KST
- [x] [T33. 진입점 정확도(기반 클래스 액션·`[ApiVersion]`)](#t33) — 완료 2026-09-18 12:09 KST

### P1 — 설치·환경 점검·설정

- [x] [T34. `doctor` 확장과 UI의 버전·로그·신고 정보](#t34) — 완료 2026-09-18 12:20 KST
- [ ] [T35. 설정 파일 진입 장벽(`init`·JSON Schema·유효 설정 보기)](#t35)
- [ ] [T36. 첫 분석의 작은 함정 묶음(`.slnx`·긴 경로·포트·큰 솔루션·테스트 프로젝트)](#t36)
- [ ] [T37. 문서 정합성(배포물 입수 경로·퀵스타트·지원 범위 표)](#t37)

### P2 — 탐색·가독성

- [ ] [T38. 메서드 이름 검색과 통합 찾기, 진입점 그룹](#t38)
- [ ] [T39. 상세 패널의 `파일:줄` 클릭·호출 지점 마커·코드→노드 이동](#t39)
- [ ] [T40. 내비게이션(브레드크럼·URL 상태·최근 대상)](#t40)
- [ ] [T41. 레이아웃(라이트 테마·글꼴 크기·패널 접기·배너 스택)](#t41)
- [ ] [T42. MCP 도구 설명의 라우팅 힌트와 허용 목록 동기화](#t42)

권장 순서: **T20 → T21 → T23 → T24 → T22** (첫 30분) → **T25 → T26 → T27** (AI 연동까지) → **T28 → T29 → T30** (빈 화면의 원인을 말하게) → 나머지 P1 → P2.
T24는 T28·T29가 만드는 진단 데이터를 그리는 자리이므로, T24를 먼저 만들되 T28·T29 완료 후 필드를 추가한다.

---

## 1. 판정: 신입이 혼자 쓸 수 있는가

**아직 어렵다.** 기능과 정직한 문구는 갖춰졌지만, **그 문구를 읽을 배경지식이 없는 사람을 위한 장치**가 거의 없다.
분석 전에는 첫 사용 안내가 아예 그려지지 않고, 용어집·설명서 링크가 UI에 0개이며, 분석에 실패하면 원문 예외만 뜨고 다음 행동으로 가는 버튼이 없다.
분석기 쪽에서는 EF Core·MediatR·BackgroundService·클래식 `Startup.cs`처럼 흔한 패턴이 **틀리게 나오는 것이 아니라 조용히 사라진다**. 그 결과 "DB·캐시 보기가 비었는데 규칙은 전부 검사함으로 표시"되는, CLAUDE.md §5(정직한 판정)에 어긋나는 화면이 나온다.

| 신입의 질문 | 지금 상태 | 근거 |
|---|---|---|
| "처음에 뭘 누르지?" | **부분**. 첫 화면(예제·내 프로젝트·레시피)은 좋다. 그러나 "안내 열기"는 분석 전에는 아무것도 보여 주지 않는다 | `App.tsx:1215`가 `!graph`면 `WelcomePanel`만 그리고, `GuidedWalkthrough`는 `App.tsx:1448` workspace 안에만 있다 |
| "진입점·추정·신선도·해석률이 뭐지?" | **미달**. 용어 정의가 화면 어디에도 없다. 매뉴얼은 있지만 앱에서 도달할 수 없다 | `client/src`에 `manual`·`href=` 참조 0건. 해석률 설명은 `title` 속성에만(`App.tsx:1177`) |
| "분석이 실패했다. 어떻게 하지?" | **미달**. MSBuild/Roslyn 예외 원문 + 오류 ID + 닫기뿐 | `App.tsx:476-501`, `AnalysisJobService.cs`가 `exception.Message`를 그대로 전달 |
| "DB·캐시 보기가 비었다. 없는 건가, 못 본 건가?" | **미달**. 둘을 구분할 수 없다. EF Core 호출은 접근 site를 만들지 않고, External 호출은 근거도 남기지 않는다 | `DataAccessScanner.cs:484-487`, `CallSiteCollector.cs:29-31` |
| "AI에게 뭐라고 물어보지?" | **부분**. 문맥 복사는 2클릭으로 훌륭하다. 그러나 질문 예시가 placeholder 한 줄뿐이고, `recipes.ts`의 프롬프트 4개는 질문 패널과 연결돼 있지 않다 | `AskPanel.tsx:279-285`, `recipes.ts:45-119` |
| "내 AI 도구에 어떻게 붙이지?" | **부분**. 설정 조각은 만들어 주지만 복사 버튼·원라이너·검증 질문이 없고, 대상 저장소용 CLAUDE.md 안내가 없다 | `AskPanel.tsx:339-355`, `McpConfigWriter.cs:180-199` |
| "내 프로젝트는 워커·컨슈머가 본체인데?" | **미달**. HTTP 진입점만 세고, 나머지는 고립 노드로 보인다. "HTTP만 본다"를 숫자로 말하지 않는다 | `EntryPointScanner.cs:61-69,115-120` |

## 2. 이번 점검에서 확인한 사실

### 2.1 이미 잘 돼 있어 손대지 않는 것

- 정직한 문구 원칙이 UI 전반에 실제로 구현돼 있다: `diff.noDiffNote`, `detail.boundaryHintFull`, `impact.empty`, `summary.resolutionHint`, `context.tokensNote`, `freshness.unknownHint`.
- 꺼진 규칙을 카탈로그에 남기는 것(`DataSidePanels.tsx:225-241`), 환경 점검의 code → 설명 → 조치 3단 구조(`WelcomePanel.tsx:112-124`, `ko.ts:297-327`), 점검 상세의 "무엇이 걸렸나 → 근거 → 왜 → 제안" 서사(`DataDetailPanel.tsx:278-291`).
- 문맥 복사까지 2클릭, 파일별 제외, "외부 AI에 보내면 전송된다" 경고(`ContextPreview.tsx`).
- 상태 영속화와 서버 재시작 시 저장 그래프 복원(`App.tsx:262-300, 384-406`), 접근성 기본(탭·모달 포커스·스플리터 키보드).
- 분석 진행 표시가 가짜 퍼센트가 아니다. 포트 충돌·`.slnx`·설정 파일 오류에 이유와 다음 행동이 붙어 있다.
- 공백 포함 경로: 프로세스 실행이 인자 리스트를 쓰고(`ProcessRunner.cs`) 설정 조각도 이스케이프한다.

### 2.2 검증으로 기각한 주장

- **"`demo`로 복사한 예제는 `obj/`가 없어 restore 없이는 분석에 실패한다"** — 실행으로 확인한 결과 **틀렸다.** `samples/OrderApi`를 `bin`·`obj`·`.tracelens` 없이 임시 폴더에 복사하고 `tracelens analyze`를 돌리면 진입점 8개(컨트롤러 4·Minimal API 4), 클래스 23, 컴파일 오류 0으로 정상 분석되고 `obj/Debug`가 생긴다. MSBuildWorkspace의 디자인타임 빌드가 복원을 수행한다. 따라서 "restore 안내"는 태스크로 넣지 않는다. 단, NuGet 소스에 접근할 수 없는 폐쇄망에서는 다른 결과가 나올 수 있으므로 T34의 `doctor`가 그 경우를 구분하도록 한다.

### 2.3 저장소 상태

- `dotnet build` 경고 0. 테스트: .NET 171개(Analyzer 72 · Server 90 · Mcp 9), 클라이언트 71개.
- 클라이언트 i18n에 정의됐지만 참조가 0회인 키가 19개다(`recipes.copyPrompt`, `guide.reopen`, `detail.confidence.*`, `summary.snapshot` 등). 이 키들은 T20·T22·T25가 쓰려던 기능의 흔적이고, `i18n.test.ts`는 미사용 키를 잡지 않는다.
- `graph.diagnostics`의 `projects`·`limitations`·`excludedNamespacePrefixes`·`depthLimitReached`·`changedDuringAnalysisFiles`는 클라이언트가 **어디에서도 그리지 않는다**(`client/src`에서 `limitations`를 쓰는 곳은 문맥·영향·비교 패널의 자기 결과뿐). 호출 해석률 한 줄(`App.tsx:1176-1183`)과 서버가 합쳐 준 한 줄 공지(`AnalyzeEndpoints.cs:BuildNotice`)만 보인다.
- `SampleView.learningSteps`와 `DiagnosticsReport.toolVersion`은 서버가 내려보내지만 렌더하는 컴포넌트가 없다(`types/context.ts:289,374`가 유일한 참조).

### 2.4 분석기가 인식하지 않는 실전 패턴 (T28~T33의 근거)

"미지원·조용히 없음"은 결과에 아무 흔적도 남지 않는다는 뜻이다. "부분·오해 유발"은 남긴 결과가 틀리게 읽힌다는 뜻이다.

| 영역 | 패턴 | 상태 | 근거 |
|---|---|---|---|
| 진입점 | gRPC 서비스, SignalR Hub, `BackgroundService`/`IHostedService`, Hangfire/Quartz, MassTransit/Kafka `IConsumer<T>`, Azure Functions, `MapHealthChecks`, 전통적 라우팅(`MapControllerRoute`), `[Route]`만 있는 액션 | 미지원·조용히 없음 | `EntryPointScanner.cs:45,61-69`, `MinimalApiEntryPointScanner.cs:325-336` |
| 진입점 | 기반 클래스에 선언된 `[HttpGet]` 액션 | 부분·오해 유발 — 파생 컨트롤러 수만큼 나와야 하는데 기반 클래스 하나로만, `[controller]`가 기반 클래스 이름으로 치환돼 존재하지 않는 라우트 | `EntryPointScanner.cs:77,184` |
| 진입점 | `[ApiVersion]` | 부분 — 라우트 제약만 지우고 특성은 읽지 않아 v1/v2가 같은 라우트로 보인다 | `EndpointIdentity.cs:99-143` |
| 호출 | MediatR `Send()`→Handler, 델리게이트/`Func<>` 필드 호출, 이벤트 `Invoke` | 미지원·조용히 없음 — External로 분류돼 근거조차 남지 않는다 | `InvocationResolver.cs:202-218`, `CallSiteCollector.cs:29-31` |
| 호출 | 인터페이스 기본 구현, 주 생성자 인자 안의 호출, source generator 산출물 | 미지원 | `SolutionScanner.cs:102-106,258`, `CallGraphWalker.cs:126-130`, `SourcePaths.cs:33-37` |
| DB | EF Core(`DbSet`·`SaveChangesAsync`·`ExecuteUpdate`), MongoDB, NHibernate | 미지원·조용히 없음 — 접근 0건인데 규칙은 전부 "검사함" | `DataAccessScanner.cs:398-487`, `Models/DataAccess.cs:152-159`(notes 필드 없음) |
| DB | `TransactionScope` | 미지원 — `ObjectCreationExpression`을 보지 않아 트랜잭션이 있는데 없다고 "확실"로 판정, COMP 규칙 오탐 | `DataAccessScanner.cs:302`(`InvocationExpressionSyntax`만 순회) |
| DB | Dapper 저장 프로시저 | 부분·오해 유발 — `QueryAsync("usp_Update…")`를 Read로 분류, `CommandType.StoredProcedure` 미인식 | `DataAccessScanner.cs:471,991-1014` |
| 미들웨어 | 클래식 `Program.cs`+`Startup.cs` | 부분·실패 모드가 나쁨 — `Program.cs`를 먼저 잡고 `Startup.cs`는 버려 시작 흐름·미들웨어 보기가 통째로 빈다 | `HostDocuments.cs:FindInProjectAsync` |
| 미들웨어 | 솔루션 안 확장 메서드 `app.UseCompanyAuth()` | 부분·오해 유발 — Framework로 분류되고 "본문은 어셈블리 안에 있어 열어 볼 수 없다"는 틀린 설명 | `MiddlewareScanner.cs:195-210` |
| 미들웨어 | MVC 액션·예외 필터, `[Authorize]` 정책, `MapGroup`에 건 엔드포인트 필터 | 미지원 | `MiddlewareScanner.cs:726-750` |
| 진단 | External 호출의 네임스페이스별 집계, "감지했지만 분석하지 않은 프레임워크" 안내 | 없음 — `CallResolutionStats.External`은 정수 하나 | `Models/AnalysisDiagnostics.cs:15-23` |

MediatR·EF Core는 1차 문서(`next-tasks_kr.md` §T17 비고)와 로드맵 §K에서 "실패 사례를 받은 뒤 설계"로 보류한 **인지된 공백**이다. 이 문서도 그것을 구현하자고 하지 않는다. 대신 **보류한 사실을 분석 결과에 노출**하는 것(T28·T29)을 요구한다. gRPC·SignalR·BackgroundService·컨슈머·클래식 Startup·MVC 필터는 어느 문서에도 없던 **미인지 공백**이다.

### 2.5 하지 않기로 한 것

CLAUDE.md의 설계 제약과 "사람이 할 일" 원칙에 따라 이 문서에 넣지 않는다.

- 파일 트리 탐색기, 저장 감지 자동 재분석, 원격 바인딩, DI·이벤트 엣지 — 제약 1~4.
- EF Core·MediatR의 **해석**(엣지·접근 생성) — 실패 사례 수집 후 별도 설계. 이 문서는 "못 봤다"를 말하는 것까지만.
- 새 버전 자동 확인(네트워크 호출) — 로컬 전용 제약과 충돌. 명시적 옵트인도 이번 범위 밖.
- GitHub Release·nuget.org 게시, macOS·Linux 검증, 사용자 관찰 — 사람이 하는 일(로드맵 §16). README가 그 부재를 정직하게 말하도록 고치는 것(T37)까지만 한다.
- 모바일·소형 화면 대응 — `styles.css:19`의 `min-width: 1040px` 주석대로 로컬 개발 도구의 범위 밖. 1366px 노트북에서의 배너 누적(T41)만 다룬다.

---

## 3. 태스크 상세

<a id="t20"></a>
### T20. 첫 사용 안내를 분석 전에도 보이게 하고 첫 방문에 자동으로 연다

**목적.** 가장 안내가 필요한 순간(분석 전)에 안내가 보이지 않고, 예제를 골라도 "무엇을 먼저 볼지"가 화면에 없다. 신입이 첫 5분 안에 "다음에 뭘 누르지"를 화면에서 알 수 있게 한다.

**현재 상태(근거).**
- 헤더의 "안내 열기"(`App.tsx:1091-1093`)는 항상 보이지만 `GuidedWalkthrough`는 `App.tsx:1448`의 workspace 안에만 있다. `App.tsx:1215`가 `!graph`면 `WelcomePanel`만 그리므로 분석 전에는 버튼을 눌러도 라벨만 "안내 닫기"로 바뀐다.
- `guideOpen`은 `useState(false)`(`App.tsx:124`)라 첫 방문에도 닫혀 있고, 완료 단계 `walkthroughDone`(`App.tsx:125`)은 저장되지 않는다. 반대로 localStorage에서 `solutionPath`·`selection`이 복원되면 `App.tsx:308-311`이 `pick`·`entry`·`evidence`를 사용자가 아무것도 안 했는데 완료로 채운다.
- 5단계를 다 끝내면 `GuidedWalkthrough.tsx:37,66`이 마지막 단계로 폴백하고 본문을 숨긴다. "다음엔 이걸 해 보라"가 없다. `guide.reopen`·`guide.next` 키(`ko.ts:267,269`)는 미사용이다.
- `PathPicker`에서 대상을 고르면 경로만 입력칸에 들어가고(`App.tsx:1102-1108`) 분석은 시작되지 않는다. 화면이 그대로라 성공했는지 알 수 없다.
- 예제 카드의 "이 예제에서 보이는 것"은 항상 `available[0].expected`(`WelcomePanel.tsx:76-81`)다. `samples.json`의 `learningSteps`는 서버가 `SampleView`로 내려보내지만 렌더하는 곳이 없다.

**변경할 파일.** `client/src/App.tsx`, `components/GuidedWalkthrough.tsx`, `components/WelcomePanel.tsx`, `components/PathPicker.tsx`, `state/viewState.ts`, `i18n/ko.ts`·`en.ts`, `styles.css`.

**구현 방법.**
1. `GuidedWalkthrough`를 workspace 밖(툴바 아래 오른쪽 고정 영역)으로 옮겨 `graph === null`일 때도 렌더한다. 단계의 `targetElementId`가 아직 없는 요소(상세 패널 등)면 포커스 대신 "분석 뒤에 보인다" 문구를 띄운다.
2. `PersistedState`에 `guide: { seen: boolean; completed: WalkthroughStepId[] }`를 추가한다. `seen`이 false면 첫 렌더에서 자동으로 연다. 완료 단계는 저장하되, **복원 직후 첫 렌더에서 발생하는 자동 완료는 무시**한다(복원 플래그를 두고 그 사이클에서는 `completeStep`을 건너뛴다).
3. 5단계 완료 시 본문을 "다음에 해 볼 것" 블록으로 바꾼다: 레시피 4개 링크(T21의 상시 레시피와 연결), "변경 영향", "분석 전후 비교". `guide.reopen`을 헤더 버튼 라벨로 쓴다.
4. `PathPicker` `mode='target'`에서 선택 직후 `WelcomePanel`에 "경로를 골랐다. 위의 [분석]을 누르면 시작된다" 배너를 띄우고 분석 버튼에 `tracelens-attention` 클래스(1회 펄스)를 준다. 자동 분석은 하지 않는다(사용자가 실행한다는 제약 3과 일관).
5. 예제 카드를 예제별 `<details>`로 바꿔 각 예제의 `expected`와 `learningSteps`(번호 목록)를 보여 준다. 걷기 안내의 `expected` 표시도 선택한 예제의 것을 쓴다.

**검증 방법.** `npm test`에 `viewState.test.ts`로 `guide` 저장·복원과 "복원 직후 자동 완료 무시"를 확인하는 테스트를 추가한다. 수동: localStorage를 비운 뒤 접속 → 안내가 열려 있고 0/5 → 예제 분석 → 단계가 실제 조작으로만 올라간다 → 새로고침 → 진행이 유지되고 늘어나지 않는다.

**완료 기준.** 분석 전 "안내 열기"가 실제로 안내를 보여 준다. 첫 방문에 자동으로 열리고 두 번째 방문부터는 닫힌 상태를 기억한다. 복원만으로 단계가 완료되지 않는다. 예제 카드에 예제별 `expected`·`learningSteps`가 보인다. 대상 선택 직후 다음 행동이 화면에 있다.

<a id="t21"></a>
### T21. 용어집·단축키·사용 설명서를 앱 안에서 연다

**목적.** 진입점·확실/추정·신선도·스냅샷(기준)·해석률·DI 등록·MCP·홉·단락·보상 처리 같은 용어가 정의 없이 화면에 나온다. 92KB짜리 `docs/manual_kr.html`은 앱에서 도달할 수 없다. 설명이 필요한 자리에서 한 번의 클릭으로 정의와 설명서에 닿게 한다.

**현재 상태(근거).**
- `client/src`에 도움말 아이콘·`<abbr>`·`aria-describedby`·매뉴얼 링크가 0개. 서버도 `docs/`를 호스팅하지 않는다.
- 용어가 노출되는 자리: 진입점(`ko.ts:89,113,121`), 해석률(`ko.ts:133`, 설명은 `App.tsx:1177`의 `title`뿐이라 키보드로 접근 불가), 신선도 사유(`FreshnessBadge.tsx:67-79`의 `title`), DI 등록·유일 구현체(`ko.ts:343-344`), MCP(`ko.ts:181-182`), 홉(`ko.ts:62,207`), 단락(`ko.ts:81,496`), 보상 처리(`ko.ts:73`), 레벨(`ko.ts:48`).
- 단축키는 존재하지만(`DiagramCanvas.tsx:97-127`의 Ctrl+휠·Ctrl +/-/0, `Splitter.tsx:41-59`의 ←/→/Home, `PathPicker.tsx:65-72`의 Esc) `title` 툴팁 외에 목록이 없다. 특히 "캔버스 위에 마우스가 있을 때만 동작"(`hoveringRef`)은 문서 없이 발견할 수 없다.
- 레시피 4개는 `WelcomePanel`에만 있어(`App.tsx:1225`) 분석 후에는 사라진다. 분석 전에 "AI 수정을 검토한다"를 누르면 `setDiffOpen(true)`(`App.tsx:739`)가 되지만 비교 패널도 workspace 안에만 있어 아무것도 뜨지 않는다.
- `recipes.ts:6`은 "앱 안내와 매뉴얼이 같은 `recipeId`를 가리킨다"고 적었지만 실제 링크는 없다.

**변경할 파일.** `src/TraceLens.Server/ServerHost.cs`(또는 `Program.cs`) — `/manual` 정적 라우트, `src/TraceLens.Cli/TraceLens.Cli.csproj`·`src/TraceLens.Server/TraceLens.Server.csproj` — `docs/manual*.html`을 출력에 포함, `client/src/components/HelpDialog.tsx`(신규), `App.tsx`, `ViewToolbar.tsx`, `i18n/ko.ts`·`en.ts`, `content/recipes.ts`, `docs/manual_kr.html`·`manual.html`(앵커 ID 정리).

**구현 방법.**
1. 서버가 `docs/manual_kr.html`·`manual.html`을 `/manual/ko`·`/manual/en`으로 서빙한다. 배포물에도 들어가도록 csproj의 samples 글롭과 같은 방식으로 복사한다. 루프백 전용이므로 제약 4와 무관하다.
2. 헤더에 `?` 버튼과 "사용 설명서" 링크(새 탭, 현재 언어)를 둔다. `?`는 `HelpDialog`를 연다. 탭 3개: **용어집**(12항목, 각 2줄, i18n 키 `glossary.<term>.title/body`), **단축키**(동작 조건 포함), **실무 절차**(레시피 4개 — 분석 전이면 "먼저 분석이 필요하다"를 붙이고, 분석 후에는 기존 `startRecipe`를 호출).
3. 용어가 처음 나오는 라벨 옆에 작은 `?` 링크(버튼, `aria-label`)를 달아 같은 대화상자의 해당 항목으로 연다. 최소 적용 자리: 분석 요약의 해석률, 신선도 배지, 상세 패널의 호출 근거 제목, DB·캐시의 보상 처리 탭 제목, 미들웨어의 단락 지점 탭 제목, 질문 패널의 MCP 버튼.
4. 매뉴얼의 보기·레시피 절에 안정적인 앵커(`#view-callflow`, `#recipe-understand-api` 등)를 붙이고, `recipes.ts`에 `manualAnchor` 필드를 더해 대화상자와 첫 화면에서 "설명서에서 보기"를 연다.
5. 레시피 배너(`App.tsx:1200-1212`)의 요청 예시에 "요청 예시 복사" 버튼(`recipes.copyPrompt` 키 사용)을 붙이고, 질문 패널 textarea에 바로 넣는 경로도 둔다(T25와 연결).

**검증 방법.** `TraceLens.Server.Tests`에 `/manual/ko`가 200과 `text/html`을 돌려주는 테스트를 추가한다. `i18n.test.ts`가 용어집 키 12개의 한·영 존재를 확인한다. 수동: `pack.ps1`로 만든 ZIP에서 `tracelens serve`를 띄워 "사용 설명서"가 열리는지 확인한다(소스 없이). 키보드만으로 `?` → 용어집 → 닫기.

**완료 기준.** 헤더에서 한 번의 클릭으로 용어집·단축키·레시피·설명서에 닿는다. 배포물에서도 설명서가 열린다. 위 6곳의 `?` 링크가 해당 항목으로 간다. 레시피가 분석 후에도 열리고, 분석 전에는 선행 조건을 말한다.

<a id="t22"></a>
### T22. 설명 없는 기호·규칙 ID·영어 원문을 정리한다

**목적.** `~`·`min` 칩, `COMP-002` 단독 표기, `Direct/Certain` 같은 영어 enum, 이유 없는 기본값(의존 관계 기본 꺼짐)이 신입에게는 오타나 미완성으로 읽힌다. 화면의 모든 기호·ID에 뜻을 붙인다.

**현재 상태(근거).**
- 추정 진입점의 `~` 칩: `SidePanels.tsx:64-66`은 `title`로 notes를 주지만 `DetailPanel.tsx:306`의 같은 `~`는 `title`이 없다. `min` 칩(`SidePanels.tsx:63`)은 툴팁이 없다. `detail.confidence.certain/estimated` 키(`ko.ts:336-337`)는 존재하지만 미사용이다.
- 규칙 ID 단독 노출: 점검 표의 "규칙" 열(`DataAccessView.tsx:577`), 미들웨어 카드 칩(`MiddlewareView.tsx:72`), 쓰기 경로 버튼 "COMP-002 상세"(`DataAccessView.tsx:526`). 반면 사이드 규칙 목록은 ID+이름을 함께 보여 준다(`DataSidePanels.tsx:225-241`) — 이 방식을 따르면 된다.
- "확실/추정" 열(`DataAccessView.tsx:560,581`)에 범례·툴팁이 없다.
- 영어 enum 누출: `ContextPreview.tsx:31`(`fresh/stale/unknown`), `:86`(`Direct/Certain`), `:102`(`kind`), `ImpactPanel.tsx:71,78`, `DataAccessView.tsx:48`(`Read/Insert`). `DetailPanel.tsx:369-376`에 이미 한국어 사전이 있다.
- 클래스 다이어그램 관계 4종·필드/메서드 토글(`ViewToolbar.tsx:117-183`)에 `title`이 없다. 의존(호출)이 기본 꺼짐인 이유(`viewState.ts:70`)가 화면에 없고, 150개 초과 시 자동 튜닝(`App.tsx:412-425`)도 알리지 않는다.
- DB·캐시 보기의 기본 하위 탭이 가장 어려운 "보상 처리"다(`viewState.ts:80`). `changeMode('data')`는 `entryNodeId`를 채워 두어(`App.tsx:731-733`) 실행 경로를 염두에 둔 코드인데 기본 탭이 다르다.
- 미사용 i18n 키 19개. `i18n.test.ts`는 미사용을 잡지 않는다.

**변경할 파일.** `SidePanels.tsx`, `DetailPanel.tsx`, `DataAccessView.tsx`, `MiddlewareView.tsx`, `ContextPreview.tsx`, `ImpactPanel.tsx`, `ViewToolbar.tsx`, `App.tsx`, `state/viewState.ts`, `i18n/labels.ts`(신규 공용 매핑), `i18n/ko.ts`·`en.ts`, `i18n/i18n.test.ts`.

**구현 방법.**
1. `i18n/labels.ts`에 `resolutionLabel`·`confidenceLabel`·`freshnessLabel`·`operationLabel`·`selectionKindLabel` 함수를 두고 위 5곳과 `DetailPanel`이 같은 것을 쓴다.
2. `~` 칩을 텍스트 칩 "추정"으로, `min`을 "Minimal API"로 바꾸고 `title`에 무엇이 추정인지(notes)를 넣는다.
3. 규칙 ID가 단독으로 나오는 3곳에 `ruleText(rule, lang).name`을 ID 아래 작은 줄로 붙이고 `title`에도 넣는다. "확실도" 열 헤더에 `?` 툴팁("확실 = 심볼로 확정. 추정 = DI 등록을 못 찾아 유일 구현체로 이었거나 람다 본문에서 추론. 근거는 상세에서 확인").
4. 관계·멤버 토글에 `title`을 붙인다. 의존 칩: "의존 = 메서드 호출 관계. 선이 크게 늘어 기본은 꺼 둔다". 자동 튜닝이 일어나면 캔버스 공지에 "클래스가 N개라 멤버 접기·의존 끄기·맞춤 줌으로 시작했다 [되돌리기]"를 띄운다.
5. `defaultState.dataView.sub`를 `'route'`로 바꾸고, 보상 처리 탭 상단에 "여기서 보는 것" 3줄(무엇을 세는지, 빨간 칸의 뜻, 판정 6종 링크→용어집)을 둔다. 기존 저장값이 `comp`인 사용자는 그대로 둔다(마이그레이션 불필요).
6. 미사용 키를 이 태스크와 T20·T25에서 실제로 쓰거나 지우고, `i18n.test.ts`에 "ko.ts의 모든 키가 `client/src`에서 한 번 이상 참조된다"를 검사하는 테스트를 추가한다(동적 키는 접두사 허용 목록으로).

**검증 방법.** `npm test`(새 미사용 키 테스트 포함), `npm run build`. 수동: 영어 UI로 전환해 상세·문맥 미리보기·영향·DB 표에 영어 enum 원문이 남지 않는지, 규칙 ID 옆에 이름이 보이는지 확인.

**완료 기준.** 화면에 뜻 없는 한 글자 칩이 없다. 규칙 ID는 항상 이름과 함께 나온다. enum 원문 누출 5곳이 사라진다. 관계·멤버 토글에 설명이 있고 자동 튜닝을 알린다. DB·캐시 기본 탭이 실행 경로다. 미사용 i18n 키 0개이고 테스트가 이를 지킨다.

<a id="t23"></a>
### T23. 실패했을 때 다음 행동으로 가는 길을 만든다

**목적.** 분석 실패·AI CLI 부재·에디터 부재에서 신입이 "그래서 뭘 하지"를 화면에서 알 수 있게 한다. 이미 잘 만든 환경 점검(code → 설명 → 조치)을 실패 지점에 연결한다.

**현재 상태(근거).**
- 분석 실패: `AnalysisJobService.cs`가 `exception.Message`를 SSE `error`로 보내고, `App.tsx:476-501`이 `ApiError`가 아니므로 원문을 배너에 그대로 띄운다. 배너에는 메시지·오류 ID·닫기뿐이고 환경 점검으로 가는 버튼이 없다.
- 환경 점검은 `WelcomePanel`에만 연결돼(`App.tsx:598-604,1224`) 분석에 한 번 성공하면 다시 갈 수 없다.
- Claude CLI 부재: 서버가 `"AGENT_NOT_FOUND: claude CLI를 찾을 수 없다(...)"`를 보내고(`AskEndpoints.cs:220-229`) `AskPanel.tsx:162`가 코드 접두사·Win32 예외 원문까지 그대로 표시한다. "Claude Code로 실행" 탭은 CLI 유무와 관계없이 활성이다(`AskPanel.tsx:230-243`).
- 에디터: 실패 시 메시지를 띄운 뒤 곧바로 `window.location.href = result.fallbackUri`(`AskPanel.tsx:196-198`)로 이동한다. `vscode://` 핸들러가 없으면 브라우저가 조용히 무시하거나 낯선 대화상자를 띄운다.
- 컴파일 오류 공지는 `summary.notice` 한 줄(`App.tsx:1160`, 12px)이라 요약 지표와 구분되지 않는다. 신입에게는 "그래프를 믿어도 되는가"를 좌우하는 가장 중요한 한 줄이다.

**변경할 파일.** `App.tsx`, `AskPanel.tsx`, `i18n/ko.ts`·`en.ts`, `styles.css`, `src/TraceLens.Server/AnalysisJobService.cs`, `Endpoints/AskEndpoints.cs`, `Endpoints/AnalyzeEndpoints.cs`, `tests/TraceLens.Server.Tests/AnalysisJobServiceTests.cs`·`AskEndpointsTests.cs`.

**구현 방법.**
1. 서버의 분석 실패 이벤트에 `code`를 붙인다. `AnalysisJobService`가 예외 메시지를 분류한다: `SDK_NOT_FOUND`(MSBuildLocator·SDK 관련), `RESTORE_FAILED`(`NETSDK1004`·`project.assets.json`·NuGet), `FILE_LOCKED`(`IOException`·sharing violation), `TARGET_NOT_FOUND`, 그 밖에는 `ANALYZE_FAILED`. 원문은 `details`로 유지한다. `AnalysisJobEvent`에 `Code` 필드를 추가한다.
2. 클라이언트 오류 배너를 재구성한다: 한 줄 요약(`error.<code>` 번역) + `[환경 점검 실행]` + `[대상 다시 고르기]` + 원문은 `<details>`로 접기 + 오류 ID.
3. 헤더(신선도 배지 옆)에 상시 "환경 점검" 버튼을 두고 결과를 모달로 보여 준다. `WelcomePanel`의 진단 카드와 같은 컴포넌트를 재사용한다(`DiagnosticsCard.tsx`로 추출).
4. `AskPanel`: SSE `error` 텍스트의 `CODE:` 접두사를 파싱해 `translateError`로 번역하고 원문은 접는다. 서버는 `AskEvent`에 `code` 필드를 추가해 파싱 의존을 없앤다. 탭을 열 때 `/api/diagnostics`의 `CLI_CLAUDE_*`를 확인해 미설치면 탭에 "설치 필요" 배지와 설치 안내 링크를 먼저 띄운다(진단 결과는 세션 동안 캐시).
5. 에디터 실패 시 자동 이동을 없애고 "VS Code를 찾지 못했다. 경로를 복사해 직접 열 수 있다: `파일:줄` [복사] [vscode:// 로 시도]"로 폴백한다.
6. 컴파일 오류·분석 중 변경 공지를 요약 줄과 분리된 경고 배너(아이콘·굵게)로 올리고 "[분석 진단 보기]"(T24)를 붙인다.

**검증 방법.** `AnalysisJobServiceTests`에 예외 메시지 → 코드 분류 테스트(4종). `AskEndpointsTests`에 `AGENT_NOT_FOUND` 이벤트가 `code`를 담는지. 수동: PATH에서 `claude`를 뺀 셸로 서버를 띄워 탭 배지를 확인, 존재하지 않는 경로로 분석해 배너의 두 버튼이 동작하는지 확인.

**완료 기준.** 분석 실패 배너에 한 줄 요약·환경 점검 버튼·대상 다시 고르기가 있고 원문은 접혀 있다. 환경 점검을 분석 후에도 열 수 있다. AI CLI가 없으면 보내기 전에 안다. 에디터 실패가 조용히 지나가지 않는다. 컴파일 오류 공지가 요약 지표와 구분된다.

<a id="t24"></a>
### T24. 분석 진단 패널 — 무엇을 보고 무엇을 못 봤는지 한 화면에

**목적.** 분석기는 프로젝트별 로드 결과·컴파일 오류 샘플·한계 목록·제외 네임스페이스·깊이 제한·분석 중 변경을 이미 기록하지만 UI는 그리지 않는다. 신입이 "이 그래프를 어디까지 믿을 수 있나"를 한 화면에서 보게 하고, 빈 보기에서 "없는 것"과 "못 본 것"을 구분하게 한다. T28·T29가 만드는 새 진단 필드도 여기에 그린다.

**현재 상태(근거).**
- `graph.diagnostics`(`types/graph.ts:194-208`)의 `projects`·`limitations`·`excludedNamespacePrefixes`·`depthLimitReached`·`changedDuringAnalysisFiles`·`analysisScope`를 클라이언트가 어디에서도 렌더하지 않는다. 요약 줄의 해석률(`App.tsx:1176-1183`)과 서버가 합친 한 줄 공지만 보인다.
- 빈 상태 문구가 전부 "솔루션을 분석하면 ~ 표시된다"(`ko.ts:112-119`, `App.tsx:1430-1436`)라 이미 분석한 사용자에게 "실패했나?"로 읽힌다. Minimal API 개수는 0이면 아예 표시하지 않아(`App.tsx:1164`) 없음과 미탐지를 구분할 수 없다.
- CLI도 한계 목록을 5개만 `- {limitation}`으로 흘리고(`CommandRouter.cs:233-236`) 컴파일 오류가 있는 프로젝트 이름·첫 오류를 출력하지 않는다(`AnalyzedProject`에 데이터는 있다).

**변경할 파일.** `client/src/components/DiagnosticsPanel.tsx`(신규), `App.tsx`, `i18n/ko.ts`·`en.ts`, `styles.css`, `src/TraceLens.Cli/CommandRouter.cs`(`PrintAnalyzeSummary`), `Endpoints/AnalyzeEndpoints.cs`(`BuildNotice`).

**구현 방법.**
1. 요약 줄 끝에 "[분석 진단]" 버튼을 두고 `DiagnosticsPanel`을 모달/서랍으로 연다. 절 구성: **프로젝트**(이름·로드 여부·컴파일 오류 수·첫 오류 최대 3건·"`dotnet build`로 먼저 확인" 안내), **호출 해석**(직접/DI/유일 구현체/모호/미해석 수와 **외부 호출 수를 분모에서 뺐다는 문장**), **범위**(`analysisScope`·제외 네임스페이스·깊이 제한 도달), **한계**(전체 목록), **분석 중 변경된 파일**, T28·T29 완료 후 **인식하지 않은 입력**(External 상위 네임스페이스, DB notes, HTTP 밖 진입점 후보).
2. 빈 상태를 `graph !== null`일 때 분기한다. 진입점 0 → "컨트롤러 특성과 Map* 등록을 찾지 못했다. Web API 프로젝트가 맞는지·컴파일 오류가 없는지 [분석 진단]에서 확인". DB·캐시 0 → "SQL 리터럴·Dapper·SqlKata·캐시 호출을 찾지 못했다. EF Core만 쓰는 프로젝트는 지금 버전이 읽지 않는다 [지원 범위]". 미들웨어 0 → "`Program.cs`에서 `app.Use*`를 찾지 못했다. `Startup.cs`만 있는 프로젝트는 T30 전까지 읽지 않는다". Minimal API는 0이어도 "Minimal API 0"으로 표시한다.
3. 요약 줄의 해석률을 `해석률 28/28 (100%) · 외부 호출 1168은 분모에서 뺌`처럼 외부 호출 수를 함께 적는다.
4. CLI `PrintAnalyzeSummary`에 컴파일 오류 프로젝트 최대 3개(이름·건수·첫 오류)와 "→ `dotnet build <대상>`으로 먼저 확인"을 추가하고, 한계는 전부 출력한다(`--json`은 이미 전체를 담는다).

**검증 방법.** `client`에 `DiagnosticsPanel.test.tsx`(dom)로 컴파일 오류 프로젝트·한계·외부 호출 문장이 렌더되는지. 수동: 일부러 컴파일 오류를 넣은 픽스처(`tests/TraceLens.Analyzer.Tests/Fixtures`를 복사해 한 줄 깨뜨림)를 분석해 패널에 프로젝트 이름과 첫 오류가 보이는지, 빈 보기 문구가 "분석하면"이 아니라 "찾지 못했다"로 나오는지.

**완료 기준.** `diagnostics`의 모든 필드가 화면 어딘가에 나온다. 분석 후 빈 보기가 원인 후보와 다음 행동을 말한다. Minimal API 0이 표시된다. CLI 요약이 컴파일 오류 프로젝트를 이름으로 말한다.

<a id="t25"></a>
### T25. 질문 패널에 프리셋 질문·목적 설명·답변 보관을 넣는다

**목적.** 문맥 전달은 2클릭으로 훌륭하지만 "뭐라고 물어보지"의 답이 없다. `recipes.ts`에 이미 있는 프롬프트 4개를 질문 패널에 연결하고, 작업 목적·길이가 문맥을 어떻게 바꾸는지 한 줄로 알려 주며, 답을 읽다 다른 노드를 눌러도 답이 사라지지 않게 한다.

**현재 상태(근거).**
- 질문 입력은 빈 textarea + placeholder 한 줄(`AskPanel.tsx:279-285`, `ko.ts:159`). `recipes.ts:45-119`의 `promptKo/En`을 `AskPanel`이 참조하지 않는다.
- 작업 목적 4개(`AskPanel.tsx:32-37`)와 길이 3개(`:39-43`)에 설명이 없다.
- 선택이 바뀌면 `useEffect([contextKey])`가 `answer`·`logs`·`message`·`preview`를 모두 비운다(`AskPanel.tsx:73-82`). 경고도 복구 수단도 없고 "답변 복사"도 없다.
- MCP 설정 대화상자(`AskPanel.tsx:339-355`)에 복사 버튼이 없다(T26에서 다룬다).

**변경할 파일.** `AskPanel.tsx`, `content/recipes.ts`, `i18n/ko.ts`·`en.ts`, `styles.css`, `App.tsx`(레시피 배너 → 질문 채우기).

**구현 방법.**
1. `recipes.ts`의 각 레시피에 `presetQuestionsKo/En: string[]`(2개씩)을 추가한다. `AskPanel`은 현재 `intent`에 맞는 레시피의 프리셋을 칩으로 textarea 위에 놓는다. 최소 세트: "이 엔드포인트가 입력부터 저장까지 무엇을 하는지 근거 ID와 함께 설명해 줘", "이 메서드를 바꾸면 어떤 API가 영향을 받고 어떤 테스트가 필요한가", "이 쓰기 경로에서 2단계가 실패하면 DB·캐시에 무엇이 남는가", "이 수정에서 의도하지 않은 호출 경로 변화가 있는가". 칩을 누르면 textarea를 채우고 편집 가능하다.
2. 목적 선택 아래에 현재 선택의 설명 한 줄을 띄운다(예: "코드 이해 — 진입점에서 이 지점까지의 경로와 소스 조각을 먼저 담는다"). 길이도 같은 방식으로("짧게 — 약 N자, 소스 줄 상한 M").
3. 답변 영역에 "답변 복사" 버튼을 두고, 최근 5개 `{selectionLabel, question, answer, at}`를 세션 상태에 보관해 접힌 목록으로 보여 준다. 선택이 바뀌어도 목록은 남긴다. 새로고침에는 남기지 않는다(소스 본문이 섞일 수 있어 localStorage에 쓰지 않는다).
4. 레시피 배너의 "요청 예시 복사"가 클립보드 복사와 함께 질문 textarea도 채운다.

**검증 방법.** `AskPanel.dom.test.tsx`(신규): 칩 클릭이 textarea를 채우는지, 선택 변경 후 최근 답변 목록이 남는지. 수동: 예제에서 진입점 → 칩 → 문맥 복사 → 다른 노드 클릭 → 이전 답이 목록에 있는지.

**완료 기준.** 질문 패널에 목적별 프리셋 칩이 있고 편집 가능하다. 목적·길이 설명이 보인다. 답변 복사가 되고 최근 5개가 남는다. 레시피 배너에서 질문을 채울 수 있다.

<a id="t26"></a>
### T26. MCP 연동을 붙여넣기 한 번으로 끝낸다

**목적.** 설정 조각은 만들어 주지만 신입은 여전히 4단계(분석 → graph 경로 확인 → 등록 → 검증)를 손으로 한다. 복사 한 번으로 등록되게 하고, 연결한 뒤 에이전트가 실제로 TraceLens를 먼저 쓰도록 대상 저장소용 안내 문단을 제공한다.

**현재 상태(근거).**
- `McpConfigWriter.cs:180-199`의 호스트는 Claude Code·Codex CLI·VS Code 3개다. Cursor(`.cursor/mcp.json`, 형식은 Claude Code와 같다)가 없다.
- 대화상자(`AskPanel.tsx:339-355`)는 호스트별 `<details>` + `<pre>`뿐이다. 복사 버튼·`claude mcp add` 원라이너·검증 질문이 없다. `McpSetupPreview`에 `Command`·`Args`는 이미 있다(`McpConfigWriter.cs:11-16`).
- 대상 저장소의 CLAUDE.md/AGENTS.md에 "TraceLens 도구를 먼저 쓰라"고 적으라는 안내가 저장소 어디에도 없다. 이게 없으면 에이전트는 평소처럼 grep으로 헤맨다.
- `.mcp.json`을 "분석 대상 루트"에 두라고 하지만(`docs/mcp-setup_kr.md:62`, `McpConfigWriter.cs:183`) Claude Code는 **실행한 작업 폴더** 기준으로 읽는다. 둘이 다를 수 있다는 주의가 없다.
- `docs/mcp-setup_kr.md:58`의 원라이너는 실행 파일 경로를 따옴표로 감싸지 않아 `C:\Program Files\…`에서 깨진다.

**변경할 파일.** `src/TraceLens.Server/McpConfigWriter.cs`, `Endpoints/ContextEndpoints.cs`(또는 mcp-setup 엔드포인트), `client/src/components/McpSetupDialog.tsx`(신규, `AskPanel`에서 분리), `types/context.ts`, `i18n/ko.ts`·`en.ts`, `docs/mcp-setup_kr.md`·`mcp-setup.md`, `README*.md`, `tests/TraceLens.Server.Tests/McpConfigWriterTests.cs`.

**구현 방법.**
1. `McpSetupPreview`에 `oneLiners: { host, command }[]`(Claude Code: `claude mcp add --scope local tracelens -- "<exe>" mcp --graph "<graph>"`, Codex: `codex mcp add …`), `verifyPrompt`(문서의 "확인해 보기" 질문), `agentGuide`(아래 4번의 문단)를 추가한다. 경로는 항상 따옴표로 감싼다.
2. 호스트에 Cursor를 추가한다(`.cursor/mcp.json`, JSON, Claude Code와 같은 내용).
3. `McpSetupDialog`: 호스트 탭 4개 + "에이전트 안내문" 탭. 모든 코드 블록에 복사 버튼. 각 탭 상단에 "왜 하는가" 한 줄, 하단에 "적용 후 해당 도구를 다시 시작한다"와 `.mcp.json` 위치 주의("Claude Code를 **실행하는 폴더**에 있어야 한다").
4. 대상 저장소용 안내문(한·영)을 `docs/mcp-setup*.md`와 대화상자에 같은 내용으로 둔다. 요지: (1) 호출 관계를 물으면 grep 전에 tracelens MCP를 먼저 쓴다, (2) `get_analysis_status`로 신선도를 먼저 확인하고 stale이면 답에 적는다, (3) "이 API가 뭘 하나" → `get_entrypoints` → `get_entrypoint_flow` → `get_context(intent=explain)`, (4) "고치면 뭐가 깨지나" → `search_symbol` → `get_impact`, (5) "쓰기 경로 안전한가" → `get_findings(ruleId=COMP-*)` → `get_context(intent=investigateData)`, (6) 빈 호출자 목록을 "호출자 없음"으로 읽지 않고 boundaries를 본다, (7) 재분석은 도구가 하지 않으므로 사용자에게 `tracelens analyze`를 요청한다.
5. 문서의 원라이너 예시를 공백이 든 경로(`"C:\Program Files\tracelens\tracelens.exe"`, `"C:\repos\My App\…"`)로 바꾼다. 안전한 예시가 곧 문서다.

**검증 방법.** `McpConfigWriterTests`에 원라이너가 경로를 따옴표로 감싸는지, Cursor 스니펫이 Claude Code와 같은 JSON인지. 수동: 대화상자에서 Claude Code 원라이너를 복사해 실제 등록 → `claude mcp get tracelens` → 검증 질문으로 파일·줄이 인용되는지 확인. 대상 저장소 CLAUDE.md에 안내문을 붙인 뒤 "이 API가 뭘 하나"를 물어 에이전트가 `get_analysis_status`를 먼저 부르는지 확인한다.

**완료 기준.** 대화상자에서 복사 → 붙여넣기 한 번으로 4개 호스트 중 하나에 등록된다. 검증 질문과 에이전트 안내문이 대화상자와 문서에 같은 내용으로 있다. 문서 예시가 공백 경로에서도 동작한다.

<a id="t27"></a>
### T27. `demo`와 예제 안내를 바로잡는다

**목적.** CLI로 시작하는 신입의 첫 경로(`demo` → `analyze` → `serve`)에 있는 작은 함정과 틀린 정답지를 없애고, 예제가 "먼저 볼 것"과 AI 연동까지 안내하게 한다.

**현재 상태(근거).**
- `demo`의 기본 복사 위치가 현재 폴더의 `tracelens-demo`(`CommandRouter.cs:305`)다. ZIP을 `C:\Program Files\tracelens`에 풀고 그 폴더에서 실행하면 설치 폴더에 쓰려 해 권한 오류가 난다. UI는 `%LOCALAPPDATA%\TraceLens\samples\{id}`(`DemoSamples.cs:82-86`)를 쓴다 — 정답이 코드에 이미 있다.
- `samples.json`의 두 항목이 같은 `sourceDirectory`(`OrderApi`)라 `DemoSamples.CopyAsync`가 같은 폴더를 두 번 복사하고, CLI 목록은 프로젝트가 두 개인 것처럼 보인다.
- `samples.json:5`는 "컨트롤러 **3개** 라우트"인데 `:9`와 실제 코드(`OrdersController.cs`의 라우트 특성 4개)는 4개다. `:10`은 Minimal API 라우트 3개만 나열하지만 실제는 4개(`/api/v1/reports/{customerId}/short` 누락). `expected`는 사용자가 결과를 대조하는 정답지라 여기가 틀리면 신뢰가 깎인다.
- `demo` 출력(`CommandRouter.cs:313-327`)은 Title·Description만 찍고 `expected`·`learningSteps`를 출력하지 않는다. 3단계가 "문맥 복사"에서 끝나 AI 연동으로 이어지지 않고, `doctor`를 권하지 않는다.
- `.tracelens/`가 분석 대상 저장소 아래 생기는데 `.gitignore`에 넣으라는 안내가 어디에도 없다.

**변경할 파일.** `src/TraceLens.Cli/CommandRouter.cs`, `src/TraceLens.Context/DemoSamples.cs`, `samples/samples.json`, `README*.md`, `tests/TraceLens.Server.Tests/SampleEndpointsTests.cs`.

**구현 방법.**
1. `DemoAsync`의 기본 목적지를 `DemoSamples.DefaultDestination(...)`로 바꾼다(한 줄). `--dir`은 그대로 존중한다.
2. `CopyAsync`에서 `SourceDirectory` 기준으로 중복을 제거한다. CLI 목록은 같은 폴더를 쓰는 항목을 "같은 프로젝트의 학습 경로"로 들여쓴다.
3. `samples.json:5`를 "컨트롤러 4개 라우트"로, `:10`에 `/api/v1/reports/{customerId}/short`를 추가한다.
4. `demo` 출력에 각 예제의 `expected`와 `learningSteps`를 그대로 찍는다. 단계를 `0. tracelens doctor --target "<경로>"` → 1 analyze → 2 serve → 3 진입점·근거·문맥 → `4. AI 에이전트 연결: UI의 "MCP 연동 설정 보기" 또는 docs/mcp-setup_kr.md`로 늘린다.
5. `analyze` 성공 요약 마지막에 `팁: .gitignore에 .tracelens/ 를 추가하면 저장소가 깨끗하다.` 한 줄을 넣는다(이미 `.gitignore`에 있으면 생략).

**검증 방법.** `SampleEndpointsTests`(또는 Context 테스트)에 "같은 sourceDirectory는 한 번만 복사한다"를 추가한다. `samples.json`의 `expected` 숫자와 실제 분석 결과(진입점 8 = 컨트롤러 4 + Minimal API 4)를 대조하는 테스트를 `SampleEndpointsTests`에 둔다(예제를 분석해 세는 통합 테스트). 수동: 읽기 전용 폴더에서 `tracelens demo`를 실행해 LOCALAPPDATA에 복사되는지.

**완료 기준.** `demo`가 쓰기 가능한 사용자 폴더에 한 번만 복사한다. `samples.json`의 숫자가 실제 결과와 일치하고 테스트가 지킨다. `demo` 출력에 정답지·학습 순서·doctor·MCP 단계가 있다. `.gitignore` 팁이 나온다.

<a id="t28"></a>
### T28. 외부(External) 호출 집계와 DB·캐시 notes — 인식하지 않은 입력을 진단에 남긴다

**목적.** CLAUDE.md §5는 "검사하지 않은 규칙은 꺼짐으로 남긴다"고 하지만 그 원칙이 **규칙 카탈로그에만** 적용되고 **입력 인식**에는 적용되지 않았다. EF Core 프로젝트에서 DB 접근 0건인데 규칙은 전부 "검사함"으로 보이는 것이 대표 사례다. 엣지·접근을 늘리지 않고 "무엇을 보고도 해석하지 않았는지"를 숫자와 이름으로 남긴다.

**현재 상태(근거).**
- `CallSiteCollector.cs:29-31`: `External`이면 `external++` 후 즉시 `return`. 근거 레코드가 없어 사후에 graph.json으로도 "이 프로젝트는 EF Core를 쓴다"를 알 수 없다.
- `CallResolutionStats.External`은 정수 하나(`Models/AnalysisDiagnostics.cs:15-23`). 해석률 분모에서 빼므로 프레임워크에 위임이 많을수록 100%에 수렴한다.
- `DataAccessScanner.cs:398-487`의 `BuildSite`는 5갈래(SQL 리터럴·queryApi·쿼리 빌더·캐시·커넥션)에 안 걸리고 솔루션 밖 호출이면 `null`을 돌려준다. `DataAccessDocument`(`Models/DataAccess.cs:152-159`)에 notes 필드가 없다.
- `BuildDiagnostics`(`TraceLensAnalyzer.cs:396-413`)의 한계 출처는 매니페스트 3종·설정·근거 상한·깊이·스코프뿐이다.

**변경할 파일.** `src/TraceLens.Analyzer/CallSiteCollector.cs`, `Models/AnalysisDiagnostics.cs`, `Models/DataAccess.cs`, `DataAccessScanner.cs`, `TraceLensAnalyzer.cs`, `docs/data-contract.md`, `client/src/types/graph.ts`, `components/DiagnosticsPanel.tsx`(T24), `src/TraceLens.Context/ContextBuilder.cs`(한계를 문맥에 반영), `src/TraceLens.Mcp/TraceLensTools.cs`(`get_analysis_status`에 포함), `tests/TraceLens.Analyzer.Tests/`(새 픽스처 `FixturesExternal` 또는 기존 픽스처 확장).

**구현 방법.**
1. `CallSiteCollector`가 External 호출을 `(containingNamespace, containingType)` 키로 센다(상한 200개 키, 초과분은 "기타"). `CallResolutionStats`에 `ExternalByNamespace: IReadOnlyList<ExternalCallGroup(Namespace, Count, SampleType)>` 상위 20개를 추가한다. 개별 호출 지점은 저장하지 않는다(파일 크기).
2. 알려진 프레임워크 접두사 표(`Microsoft.EntityFrameworkCore`, `MediatR`, `MongoDB.Driver`, `NHibernate`, `MassTransit`, `Confluent.Kafka`, `Hangfire`, `Quartz`, `Grpc`, `Microsoft.AspNetCore.SignalR`, `System.Transactions`)와 대조해 매칭되면 `limitations`에 **한 줄씩** 승격한다. 문구 예: "`Microsoft.EntityFrameworkCore` 호출 412건은 DB 접근으로 해석하지 않았다(지금 버전은 SQL 리터럴·Dapper·SqlKata·설정한 queryApis만 읽는다)". 표에 없는 네임스페이스는 진단 패널의 목록에만 나온다.
3. `DataAccessDocument`에 `Notes: IReadOnlyList<string>`을 추가한다. 스캐너가 (a) 접근 0건이면서 `DbContext` 파생 타입·`IMongoCollection<>`·`ISession`(NHibernate)이 솔루션에 있으면, (b) `db.queryApis`가 기본값인데 접근 0건이면 각각 사실을 적는다. 규칙 결과에는 손대지 않는다(규칙은 실제로 돌았다).
4. 해석률 문장을 UI·CLI·문맥에서 "해석 N/M · 외부 호출 K는 분모에서 뺌"으로 통일한다(T24 3항과 동일).
5. `docs/data-contract.md`에 부가 필드로 적는다(schema 번호는 올리지 않는다 — 추가 필드만이고 옛 그래프는 빈 값으로 읽는다).

**검증 방법.** 새 픽스처에 EF Core 스타일 코드(패키지 참조 없이 `DbContext` 이름의 클래스와 `Set<T>()` 호출을 흉내 낸 로컬 타입)와 `System.Transactions` 호출을 넣고, `ExternalByNamespace`에 해당 네임스페이스가 잡히고 `limitations`·`dataAccess.notes`에 문장이 생기는지 확인한다. 실제 프로젝트(1차 문서 §2.2의 GameServer)를 다시 분석해 `SqlKata`·`CloudStructures`가 상위 네임스페이스에 나오는지, 결과물(노드·엣지·접근 수)이 **변하지 않는지** 전후 수치를 기록한다.

**완료 기준.** `diagnostics.calls.externalByNamespace`가 채워진다. 알려진 프레임워크는 한계 문장으로 승격된다. DB 접근 0건 + ORM 타입 존재 시 `dataAccess.notes`에 사실이 남는다. 해석률 옆에 외부 호출 수가 함께 나온다. 기존 그래프 수치는 그대로다.

<a id="t29"></a>
### T29. HTTP 밖 진입점 후보를 세어 알린다

**목적.** 게임 서버·백엔드는 워커·컨슈머·gRPC가 본체인 경우가 많다. 지금은 HTTP 진입점만 세고 나머지는 고립 노드로 보인다. 진입점·엣지를 만들지 않고 **"HTTP 밖 실행 단위가 N개 있는데 이번 분석은 HTTP만 본다"**를 숫자로 말한다.

**현재 상태(근거).** `EntryPointScanner.cs:61-69`는 `entryPointAttributes`+`AcceptVerbs`만 본다. 안내는 진입점 0개일 때의 문구 하나(`:115-120`)뿐이다. `BackgroundService`·`IHostedService`·`IConsumer<>`·`Hub`·gRPC `*Base`·`[Function]`·`MapHealthChecks`·전통적 라우팅(`MapControllerRoute`)은 어디에도 세지 않는다. 픽스처의 `Jobs/CleanupJob.cs`는 "HTTP에서 도달 못 하는 클래스" 용도로만 쓰인다.

**변경할 파일.** `src/TraceLens.Analyzer/EntryPointCandidateScanner.cs`(신규), `EntryPointScanner.cs`, `Models/AnalysisDiagnostics.cs`(`EntryPointCandidates: IReadOnlyList<EntryPointCandidateGroup(Kind, Count, Samples)>`), `TraceLensAnalyzer.cs`, `docs/data-contract.md`, `client/src/types/graph.ts`, `DiagnosticsPanel.tsx`(T24), `SidePanels.tsx`(진입점 목록 하단 한 줄), `tests/TraceLens.Analyzer.Tests/Fixtures/FixtureApp/`(후보 타입 몇 개 추가).

**구현 방법.**
1. 타입 심볼을 훑어 기반 타입·구현 인터페이스·특성으로 후보를 분류한다: `HostedService`(`BackgroundService`·`IHostedService`), `MessageConsumer`(`IConsumer<T>`·`IHandleMessages<T>`·Kafka 소비 루프는 이름 휴리스틱 없이 인터페이스만), `SignalRHub`(`Hub`·`Hub<T>`), `GrpcService`(`*.*Base` 중 `Grpc.Core.BindServiceMethod` 특성이 있는 기반), `ScheduledJob`(`IJob`·Hangfire `[AutomaticRetry]`류 특성), `AzureFunction`(`[Function]`/`[FunctionName]`), `ConventionalRouting`(`Program.cs`/`Startup.cs`에 `MapControllerRoute`·`MapDefaultControllerRoute`가 있고 특성 없는 public 컨트롤러 액션이 있을 때). 각 그룹은 개수와 이름 3개까지만 남긴다.
2. `EntryPointNotes`에 그룹별 한 줄을 추가한다. 예: "`BackgroundService` 구현 3개(`RankBatchWorker`, …)는 HTTP 진입점이 아니라 호출 흐름의 출발점으로 잡지 않았다. 변경 영향에서 '도달하는 진입점 없음'은 이들을 세지 않은 결과다".
3. 진입점 목록(`SidePanels.tsx`) 하단에 "HTTP 밖 실행 단위 N개 [분석 진단]" 한 줄을 둔다. 영향 분석 결과의 한계 목록(`ImpactAnalyzer`)에도 같은 문장을 넣는다.
4. `MapHealthChecks`는 진입점으로 만들지 않고 후보 그룹 `HealthCheck`로 센다.

**검증 방법.** 픽스처에 `BackgroundService` 파생 1개·`Hub` 파생 1개·`IConsumer<T>` 구현 1개를 추가하고 `EntryPointCandidates`에 각 1개가 잡히고 진입점 수는 변하지 않는지 테스트한다. 실제 프로젝트 재분석으로 후보 그룹 수를 기록한다.

**완료 기준.** HTTP 밖 실행 단위가 종류별 개수와 이름으로 진단·진입점 목록·영향 한계에 나온다. 진입점·엣지 수는 변하지 않는다.

<a id="t30"></a>
### T30. 클래식 `Startup.cs` 호스트를 인식한다

**목적.** .NET Core 3.1~5 구조(`Program.cs`의 `CreateHostBuilder` + `Startup.cs`의 `ConfigureServices`/`Configure`)에서 시작 흐름·미들웨어 두 보기가 통째로 비고 이유도 나오지 않는다. 사내 레거시 프로젝트에서 매우 흔하다.

**현재 상태(근거).** `HostDocuments.cs:FindInProjectAsync`는 `Program.cs`에 `CreateBuilder`·`CreateHostBuilder`·`CreateDefaultBuilder`가 있으면 즉시 그것을 돌려주고, `Startup.cs`(`Configure(IApplicationBuilder`)는 **Program.cs를 못 찾았을 때만** 쓴다. 클래식 `Program.cs`에는 `app.Use*`가 없으므로 미들웨어 0단계, DI 등록 0단계가 된다. `MiddlewareScanner.cs:58`에 로그만 남는다.

**변경할 파일.** `src/TraceLens.Analyzer/HostDocuments.cs`, `StartupFlowScanner.cs`, `MiddlewareScanner.cs`, `tests/TraceLens.Analyzer.Tests/FixturesClassic/`(신규 미니 솔루션) 또는 기존 픽스처에 프로젝트 추가, `docs/manual*.html`(지원 범위 문구).

**구현 방법.**
1. `HostDocument`를 단일 문서에서 **문서 목록**으로 바꾼다. `Program.cs`에 `UseStartup<T>()`가 있으면 `T`의 선언 문서를 함께 포함하고, 없더라도 같은 프로젝트에 `Configure(IApplicationBuilder`가 있는 `Startup.cs`가 있으면 포함한다(순서: Program.cs → Startup.cs).
2. `StartupFlowScanner`는 `ConfigureServices(IServiceCollection services)` 본문을 서비스 등록 구간으로, `Configure(IApplicationBuilder app, …)` 본문을 파이프라인 구간으로 읽는다. 수신자 이름이 `builder.Services`가 아니라 매개변수 이름(`services`, `app`)이므로 수신자 판별을 매개변수 타입 기준으로 바꾼다.
3. `MiddlewareScanner`도 `Configure` 본문을 파이프라인으로 읽는다. `IApplicationBuilder` 매개변수 이름이 `app`이 아닐 수 있으므로 타입으로 판별한다.
4. 호스트를 하나도 못 찾으면 `limitations`에 "Program.cs/Startup.cs에서 호스트 구성을 찾지 못했다"를 남긴다(지금은 로그만).

**검증 방법.** 클래식 구조 픽스처(`Program.cs` + `Startup.cs`, `services.AddScoped<I,Impl>()`, `app.UseAuthentication()` 등)를 만들어 시작 흐름의 등록 단계와 미들웨어 단계가 잡히는지, 기존 최소 호스팅 픽스처 결과는 그대로인지 테스트한다.

**완료 기준.** 클래식 구조에서 두 보기가 채워진다. 호스트를 못 찾으면 한계에 남는다. 기존 픽스처의 결과가 변하지 않는다.

<a id="t31"></a>
### T31. 솔루션 안 미들웨어 확장 메서드를 펼치고 틀린 설명을 없앤다

**목적.** 사내 파이프라인은 대부분 `app.UseCompanyAuth()` 같은 확장 메서드로 등록한다. 지금은 이를 프레임워크 미들웨어로 분류하고 "본문은 어셈블리 안에 있어 열어 볼 수 없다"는 **틀린** 설명을 붙인다. 시작 흐름 스캐너는 같은 상황에서 본문을 펼치므로 일관성도 어긋난다.

**현재 상태(근거).** `MiddlewareScanner.cs:195-210`: `Known`에 없고 `UseMiddleware<T>`·`Use(lambda)`가 아니면 `MiddlewareType.Framework` + 위 설명. 확장 메서드의 선언 문서를 여는 코드가 없다. `StartupFlowScanner.cs:145-163`은 솔루션 안 확장 메서드를 `ExtensionCall`로 두고 본문을 펼친다.

**변경할 파일.** `src/TraceLens.Analyzer/MiddlewareScanner.cs`, `Models/MiddlewarePipeline.cs`(`MiddlewareType.Extension` 추가), `docs/data-contract.md`, `client/src/components/MiddlewareView.tsx`(펼침 표시), `i18n/ko.ts`·`en.ts`, `tests/TraceLens.Analyzer.Tests/FixturesData/`(확장 메서드 미들웨어 추가), `MiddlewarePipelineTests.cs`.

**구현 방법.**
1. 호출 심볼이 솔루션 안 확장 메서드면 `MiddlewareType.Extension`으로 분류하고, 본문에서 `app.UseMiddleware<T>()`·`app.Use(...)`·다른 `Use*` 호출을 찾아 **하위 단계**로 펼친다(깊이 2, `StartupFlowScanner`의 전개 로직을 공용 헬퍼로 뽑아 재사용). 단락·요청/응답 구간 판정은 펼친 하위의 실제 미들웨어 타입에 대해 수행한다.
2. 펼치지 못하면(본문이 없거나 깊이 초과) 설명을 "솔루션 안 확장 메서드다. 본문에서 미들웨어 등록을 찾지 못했다"로 바꾼다. "어셈블리 안" 문구는 실제로 솔루션 밖일 때만 쓴다.
3. `MiddlewareView`에서 Extension 단계는 접힌 채로 시작하고 누르면 하위 단계가 펼쳐진다(시작 흐름 보기와 같은 조작).

**검증 방법.** 픽스처에 `public static IApplicationBuilder UseFixtureAuth(this IApplicationBuilder app) => app.UseMiddleware<ApiKeyMiddleware>();`를 추가하고 파이프라인에 Extension 단계 + 하위 `ApiKeyMiddleware`(401 단락 포함)가 잡히는지, MW 규칙(순서 검사)이 펼친 하위 기준으로 동작하는지 테스트한다.

**완료 기준.** 솔루션 안 확장 메서드가 Extension으로 분류되고 하위 미들웨어가 펼쳐진다. "어셈블리 안" 설명이 솔루션 안 코드에 붙지 않는다.

<a id="t32"></a>
### T32. "확실"로 틀리는 DB 판정 두 가지(`TransactionScope`·Dapper 저장 프로시저)

**목적.** 추정 판정이 틀리는 것은 감수하지만, **확실도 "확실"로 틀리는** 판정은 도구의 신뢰를 직접 깎는다. `TransactionScope`가 있는데 "트랜잭션 없는 쓰기"로 COMP 규칙이 오탐하고, 저장 프로시저 호출을 메서드 이름만 보고 Read로 분류하는 두 경우를 고친다.

**현재 상태(근거).**
- `DataAccessScanner.cs:302`의 `BuildFacts`는 `InvocationExpressionSyntax`만 순회한다. `new TransactionScope(...)`는 `ObjectCreationExpressionSyntax`라 트랜잭션 경계로 잡히지 않는다. 결과: 그 안의 쓰기가 "트랜잭션 없음"으로 COMP-001/002에 걸린다.
- Dapper: 문자열 인자를 대상으로 잡지만(`:1064-1110`) 연산은 Dapper 메서드 이름으로 정한다(`:471,991-1014`). `QueryAsync("usp_UpdateUser", commandType: CommandType.StoredProcedure)`가 Read가 된다. `CommandType.StoredProcedure`는 어디에서도 읽지 않는다. 관련 규칙 MAP-001은 꺼짐(`DataRuleEngine.cs:580-581`).

**변경할 파일.** `src/TraceLens.Analyzer/DataAccessScanner.cs`, `Models/DataAccess.cs`(`DataOperation.Unknown` 또는 `StoredProcedure` 추가, `TransactionKind.Scope`), `DataRuleEngine.cs`(Unknown 연산은 RW·COMP 판정에서 "확정 못 함"으로), `docs/data-contract.md`, `client/src/i18n/labels.ts`(T22), `tests/TraceLens.Analyzer.Tests/FixturesData/`, `DataAccessAnalysisTests.cs`.

**구현 방법.**
1. `BuildFacts`가 `ObjectCreationExpressionSyntax`도 순회해 타입이 `System.Transactions.TransactionScope`면 트랜잭션 경계(`kind = Scope`)를 만든다. `using` 선언·블록의 범위를 경계 범위로 쓰고, `scope.Complete()` 호출을 커밋으로, 없으면 "Complete 호출 없음"을 근거에 남긴다.
2. 저장 프로시저: 인자에 `CommandType.StoredProcedure`가 있거나 `commandType:` 명명 인자가 그 값이면 대상을 프로시저 이름으로, 연산을 `Unknown`(근거: "저장 프로시저는 본문을 읽지 않아 읽기/쓰기를 확정하지 못했다")으로 둔다. Dapper `Execute*`는 쓰기 후보로 남기되 확실도를 추정으로 낮춘다.
3. `DataRuleEngine`은 `Unknown` 연산을 쓰기 경로 단계에 넣지 않고 "확정하지 못한 접근"으로 별도 집계해 점검 결과 상단에 개수를 표시한다.

**검증 방법.** 픽스처에 `using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled); … scope.Complete();` 경로와 `QueryAsync("usp_UpdateOrder", …, commandType: CommandType.StoredProcedure)` 호출을 추가한다. 전자는 트랜잭션 경계로 잡혀 COMP-002가 나지 않고, 후자는 Read가 아니라 Unknown+근거로 나오는지 테스트한다.

**완료 기준.** `TransactionScope` 경로가 트랜잭션으로 판정된다. 저장 프로시저 호출이 Read로 단정되지 않는다. 두 경우 모두 근거 문장이 남는다.

<a id="t33"></a>
### T33. 진입점 정확도(기반 클래스 액션·`[ApiVersion]`)

**목적.** 존재하지 않는 라우트를 "확실"로 보여 주거나 버전이 다른 두 API를 하나로 뭉개는 것을 막는다.

**현재 상태(근거).**
- `EntryPointScanner.cs:77,184`: 액션의 `ContainingType`(선언한 기반 클래스)으로 클래스 라우트와 `[controller]` 치환을 하므로, `CrudControllerBase<T>`에 선언한 `[HttpGet]`은 파생 컨트롤러 수만큼 나오지 않고 `/api/CrudControllerBase/...` 하나로 나온다.
- `[ApiVersion]`은 읽지 않는다(`EndpointIdentity.cs:99-143`는 라우트 제약 `{version:apiVersion}`만 정규화). v1/v2 컨트롤러가 같은 라우트면 `#2` 접미사로만 구분된다.

**변경할 파일.** `src/TraceLens.Analyzer/EntryPointScanner.cs`, `EndpointIdentity.cs`, `Models/EntryPoint.cs`(`ApiVersions: IReadOnlyList<string>`), `docs/data-contract.md`, `client/src/components/SidePanels.tsx`·`DetailPanel.tsx`(버전 표시), `tests/TraceLens.Analyzer.Tests/Fixtures/FixtureApp/Controllers/`(기반 클래스·버전 컨트롤러 추가), `AnalyzerScopeTests.cs`.

**구현 방법.**
1. 액션이 선언된 타입이 `abstract`이거나 컨트롤러 접미사 규약에 맞지 않으면, 솔루션 안 **비추상 파생 컨트롤러**마다 진입점을 하나씩 만든다(`nodeId`는 기반 메서드, `endpointId`는 파생 타입 이름 기준). 파생이 없으면 진입점을 만들지 않고 `notes`에 "기반 클래스 선언이라 파생 라우트를 확정하지 못했다"를 남긴다. 파생이 있는 경우 확실도는 확실, 클래스 라우트는 파생 타입의 `[Route]`를 우선한다.
2. `[ApiVersion("1.0")]`(클래스·메서드)을 읽어 `EntryPoint.ApiVersions`에 담고, 라우트에 `{version:apiVersion}` 토큰이 있으면 각 버전으로 치환한 진입점을 만든다. 토큰이 없으면(헤더·쿼리 버전) 라우트는 그대로 두고 `endpointId`에 버전을 포함해 구분한다.
3. 진입점 목록과 상세에 버전을 칩으로 표시한다.

**검증 방법.** 픽스처에 `abstract class CrudControllerBase<T> : ControllerBase { [HttpGet("{id}")] … }`와 이를 상속한 컨트롤러 2개, `[ApiVersion("1.0")]`·`[ApiVersion("2.0")]` 컨트롤러 쌍을 추가한다. 진입점이 파생별·버전별로 나뉘고 `CrudControllerBase` 라우트가 사라지는지 테스트한다.

**완료 기준.** 기반 클래스 이름이 든 라우트가 나오지 않는다. 파생별·버전별 진입점이 구분된다. 확정 못 한 경우 notes에 사유가 남는다.

<a id="t34"></a>
### T34. `doctor` 확장과 UI의 버전·로그·신고 정보

**목적.** `doctor`가 "분석할 수 있다"고 했는데 실제로는 실패하는 경우(오래된 SDK, 대상 프레임워크 미설치, MSBuild 등록 문제)를 잡고, UI 사용자가 버전·로그 위치·최근 오류를 보고 신고 정보를 한 번에 복사할 수 있게 한다.

**현재 상태(근거).**
- `SolutionLoader.FindLatestSdkDirectory()`(`SolutionLoader.cs:130-148`)는 가장 최신 SDK 하나를 고를 뿐 8.0 이상인지 보지 않는다. SDK 6.0만 있어도 `SDK_FOUND`(Ok)·`CanAnalyze=true`가 된다. `SDK_MISMATCH`는 `global.json`이 있을 때만(`EnvironmentDiagnostics.cs:213-236`).
- 대상 `.csproj`의 `TargetFramework`와 설치 SDK를 대조하지 않는다. `EnsureMsBuildRegistered()`(`SolutionLoader.cs:109-127`)가 어느 MSBuild를 골랐는지 `doctor`가 말하지 않는다.
- `/api/diagnostics`는 항상 `ServerHost.DefaultPort`를 검사한다(`DiagnosticsEndpoints.cs:30`). `--port 25099`로 띄운 서버가 25011에 대해 `PORT_IN_USE`를 말할 수 있다 — 틀린 정보.
- `PORT_IN_USE` 조치(`EnvironmentDiagnostics.cs:255`)는 "종료해야 한다"만 있고 방법이 없다. CLI `doctor`의 `로그:` 라벨은 서버가 없을 때 폴더를 파일처럼 보여 준다(`CommandRouter.cs:263`).
- UI: `DiagnosticsReport.toolVersion`·`runtimeVersion`·`log`를 렌더하지 않는다(`WelcomePanel.tsx:106-125`는 `items`만). 500 오류의 `errorId`로 무엇을 하라는 안내와 로그 경로가 UI에 없다.
- 비Windows에서 실행하면 "검증하지 않았다"는 말 없이 진행된다. NuGet에 접근할 수 없는 환경(폐쇄망)에서 복원이 실패하는 경우를 구분하지 않는다(§2.2).

**변경할 파일.** `src/TraceLens.Context/EnvironmentDiagnostics.cs`, `src/TraceLens.Analyzer/SolutionLoader.cs`(SDK 버전·MSBuild 선택 결과 노출), `src/TraceLens.Server/Endpoints/DiagnosticsEndpoints.cs`, `ServerHost.cs`(실제 포트 보관), `src/TraceLens.Cli/CommandRouter.cs`, `client/src/components/DiagnosticsCard.tsx`(T23에서 추출), `i18n/ko.ts`·`en.ts`, `tests/TraceLens.Server.Tests/`(EnvironmentDiagnostics 테스트 추가).

**구현 방법.**
1. 새 진단 코드: `SDK_TOO_OLD`(Error, 최신 SDK < 8.0), `TFM_NOT_INSTALLED`(Warning, 대상 `.csproj`/`.sln` 하위 프로젝트의 `TargetFramework(s)`를 정규식으로 읽어 설치 SDK 메이저와 대조), `MSBUILD_REGISTERED`(Ok, 선택된 인스턴스 이름·버전·경로를 Detail로), `OS_UNVERIFIED`(Warning, Windows가 아니면 "이 OS는 아직 검증하지 않았다. CONTRIBUTING 형식으로 알려 달라"), `NUGET_UNREACHABLE`(Warning, `obj/project.assets.json`이 없고 NuGet 소스 호스트에 DNS 해석이 안 될 때 — 네트워크 요청은 하지 않고 이름 해석만).
2. `PORT_IN_USE` 조치에 `Get-Process tracelens | Stop-Process`(Windows)와 `--port` 안내를 넣는다. CLI 라벨을 `로그 파일:`/`로그 폴더:`로 구분한다.
3. `DiagnosticsEndpoints`가 `ServerHost`에 보관한 실제 바인딩 포트를 쓴다.
4. UI 진단 카드에 `TraceLens {toolVersion} · .NET {runtimeVersion} · {operatingSystem}`, 로그 파일 경로, 최근 오류 3건을 표시하고 "신고 정보 복사" 버튼(버전·OS·SDK·대상 형식·로그 경로·최근 오류·errorId를 CONTRIBUTING의 신고 템플릿 형식으로)을 둔다. 헤더 오른쪽에도 버전을 작게 표시한다. 오류 배너의 errorId 옆에 "로그: <경로>"를 덧붙인다.

**검증 방법.** `EnvironmentDiagnostics` 테스트에 SDK 버전 문자열 → `SDK_TOO_OLD` 판정, TFM 대조 판정을 추가한다(파일 시스템은 임시 폴더로). 수동: `tracelens serve --port 25099`에서 진단이 25099를 검사하는지, 신고 정보 복사 결과가 템플릿 형식인지.

**완료 기준.** 오래된 SDK·미설치 TFM·비Windows가 진단에 나온다. 진단이 실제 포트를 본다. UI에 버전·로그·최근 오류가 보이고 신고 정보를 한 번에 복사한다.

<a id="t35"></a>
### T35. 설정 파일 진입 장벽(`init`·JSON Schema·유효 설정 보기)

**목적.** 사내 DB 래퍼를 쓰는 프로젝트에서 "DB·캐시 보기가 비었다"의 해결책은 `tracelens.config.json`의 `db.queryApis`인데, 신입은 그 파일을 만들 방법도 자동완성도 "내 설정이 먹었는지" 확인할 방법도 없다.

**현재 상태(근거).** CLI 명령은 `help|version|serve|analyze|mcp|doctor|demo`(`CommandRouter.cs:24-34`)로 `init`이 없다. 클라이언트에서 설정 파일을 언급하는 곳은 문구 2개(`ko.ts:76,584`)뿐. `$schema` 키는 경고만 면제될 뿐(`TraceLensConfig.cs:200-201`) 실제 스키마 파일이 없다. 기본값은 `TraceLensConfig.Default`와 매뉴얼에만 있다.

**변경할 파일.** `src/TraceLens.Cli/CommandRouter.cs`(`init`, `config --show`), `src/TraceLens.Analyzer/TraceLensConfig.cs`(템플릿 생성·유효 설정 직렬화), `docs/tracelens.config.schema.json`(신규), `src/TraceLens.Cli/TraceLens.Cli.csproj`(스키마를 출력에 포함), `tests/TraceLens.Analyzer.Tests/`(스키마 ↔ `KnownKeys` 대조 테스트), `client/src/components/DiagnosticsPanel.tsx`(T24, DB 0건 안내), `docs/manual*.html`, `README*.md`.

**구현 방법.**
1. `tracelens init [--target <경로>] [--force]`: 대상 루트에 `_comment`와 `$schema`가 든 기본 설정을 쓴다. `samples/OrderApi/tracelens.config.json`을 템플릿으로 하되 값은 기본값과 빈 배열로 둔다. 이미 있으면 덮어쓰지 않고 "이미 있다. 모르는 키: …, 기본값과 다른 키: …"를 출력한다.
2. `docs/tracelens.config.schema.json`을 `KnownKeys`·`DataAccessOptions`·`QueryBuilderOption`에서 손으로 작성하고, 테스트가 스키마의 최상위 `properties`와 `TraceLensConfig.KnownKeys`가 같은 집합인지, `db` 아래 키가 `DataAccessOptions`의 알려진 키와 같은지 대조한다(`ruleText.test.ts`와 같은 방식). 스키마 URL은 배포물 안의 파일 경로(`file://`)와 저장소 raw URL 둘 다 문서에 적는다.
3. `tracelens config --show [--target <경로>]`: 기본값 + 파일을 병합한 **유효 설정 전체**를 JSON으로 출력하고, 설정 파일에서 읽은 키를 표시한다. `doctor --json`에도 같은 것을 `effectiveConfig`로 넣는다.
4. DB 접근 0건 + `db.queryApis`가 기본값이면(T28의 notes) UI 진단 패널과 CLI 요약에 "사내 데이터 래퍼를 쓴다면 `tracelens init`으로 설정 파일을 만들고 `db.queryApis`에 적어야 한다"를 띄운다.

**검증 방법.** 스키마 대조 테스트. `init` 두 번 실행 시 두 번째가 덮어쓰지 않는지 테스트. 수동: VS Code에서 생성된 파일을 열어 자동완성이 뜨는지.

**완료 기준.** `init`·`config --show`가 동작한다. 스키마 파일이 있고 코드와 어긋나면 테스트가 실패한다. DB 0건일 때 설정 파일로 가는 안내가 나온다.

<a id="t36"></a>
### T36. 첫 분석의 작은 함정 묶음(`.slnx`·긴 경로·포트·큰 솔루션·테스트 프로젝트)

**목적.** 각각은 작지만 신입이 "왜 안 되지"에서 10분씩 잃는 지점들이다.

**현재 상태(근거).**
- `.slnx`: 미지원 사유 문구는 훌륭하지만(`SolutionLoader.cs:20-27`) 찾아보기 목록에는 아예 나오지 않는다(`BrowseEndpoints.cs:91-96`의 `GetTargetKind`가 `.sln`·`.csproj`만 반환). `.slnx`만 있는 폴더는 빈 폴더처럼 보여 그 문구에 도달할 수 없다.
- 긴 경로(OneDrive 등): `PathTooLongException`이 `BrowseEndpoints.cs:69-76`·`DemoSamples.cs:118-127`에서 조용히 `null`이 되어 "찾아보기가 갑자기 드라이브 목록으로 돌아간다"로 보인다.
- 포트: `run.ps1`에 `-Port`가 없고 25011이 하드코딩(`run.ps1:38`). 매뉴얼의 게시 절은 "포트는 소스에 고정"이라고 적어 `serve --port`와 모순된다.
- 큰 솔루션: 단계별 진행·취소·`--profile`은 좋다. 그러나 오래 걸릴 때 "대상을 `.csproj`로 좁히거나 `analysisScope=entryPoints`" 같은 처방이 진행 화면에 없다.
- 테스트 프로젝트: 기본 `analysisScope=solution`이라 테스트 프로젝트의 타입이 클래스 다이어그램에 섞인다. 제외 수단은 `excludeNamespacePrefixes`뿐이고 프로젝트 단위 제외 키가 없다(`TraceLensConfig.cs:194-202`).

**변경할 파일.** `src/TraceLens.Server/Endpoints/BrowseEndpoints.cs`, `client/src/components/PathPicker.tsx`, `types/graph.ts`(`BrowseEntryKind.Unsupported`), `src/TraceLens.Context/DemoSamples.cs`, `run.ps1`, `docs/manual*.html`, `App.tsx`(진행 60초 힌트), `src/TraceLens.Analyzer/TraceLensConfig.cs`·`SolutionLoader.cs`(`excludeProjectNamePatterns`), `src/TraceLens.Cli/CommandRouter.cs`(테스트 프로젝트 개수 힌트), `docs/data-contract.md`, 관련 테스트.

**구현 방법.**
1. `GetTargetKind`에 `.slnx → Unsupported`를 추가하고 `PathPicker`가 회색 비활성 항목 + `title`에 `UnsupportedExtensions[".slnx"]` 문구를 보여 준다.
2. `ResolveDirectory`가 `PathTooLongException`을 잡으면 `400 PATH_TOO_LONG`("경로가 너무 길다. Windows 긴 경로 지원을 켜거나 더 짧은 위치로 옮겨야 한다")을 돌려주고 클라이언트가 번역해 보여 준다. `DemoSamples`도 같은 예외를 로그에 남긴다.
3. `run.ps1 -Port <n>`을 추가해 서버에 `--port`로 넘기고 브라우저 URL도 맞춘다. 매뉴얼 문구를 `serve --port` 기준으로 고친다.
4. 분석 진행이 60초를 넘으면 진행 줄 아래에 "오래 걸린다면: 대상을 `.csproj`로 좁히거나 설정에 `analysisScope: "entryPoints"`를 둔다 [설명서]" 힌트를 띄운다(취소 버튼 옆).
5. 설정에 `excludeProjectNamePatterns: string[]`(기본 `[]`)를 추가하고 `SolutionLoader`가 매칭 프로젝트를 로드 목록에서 뺀다(진단의 `projects`에는 `Loaded=false, Reason="excluded"`로 남긴다). CLI 요약에 이름이 `*.Tests`/`*.Test`이거나 `xunit`/`nunit`/`mstest` 패키지를 참조하는 프로젝트 수를 "테스트로 보이는 프로젝트 N개 포함 → 빼려면 `excludeProjectNamePatterns`"로 안내한다.

**검증 방법.** `BrowseEndpointsTests`에 `.slnx` 항목이 `Unsupported`로 나오는지, `PATH_TOO_LONG` 응답. `AnalyzerScopeTests`에 `excludeProjectNamePatterns`로 픽스처 테스트 프로젝트가 빠지는지. 수동: `.\run.ps1 -Port 25099`.

**완료 기준.** `.slnx`가 목록에 보이고 이유가 붙는다. 긴 경로가 오류 코드로 나온다. `run.ps1`로 포트를 바꿀 수 있다. 오래 걸릴 때 힌트가 뜬다. 프로젝트 단위 제외가 되고 테스트 프로젝트 개수가 안내된다.

<a id="t37"></a>
### T37. 문서 정합성(배포물 입수 경로·퀵스타트·지원 범위 표)

**목적.** 문서가 약속하지 않은 것을 약속하지 않게 한다. 특히 "배포물로 시작하기(소스·Node.js 없이)"는 지금 배포 채널이 없어 순환 논리다. 게시 자체는 사람이 할 일이므로 하지 않고, **그 부재를 정직하게 적고 대안을 준다.**

**현재 상태(근거).**
- `dotnet tool install`·nuget.org·GitHub Release·다운로드 링크가 저장소 어디에도 없다. `TraceLens.Cli.csproj`의 주석은 "게시 주소는 공개 전에 확정", `pack.ps1`은 "게시는 하지 않는다". 그런데 `README_kr.md`의 "배포물로 시작하기(소스·Node.js 없이)" 절은 ZIP이 있다는 전제이고, ZIP을 만드는 `pack.ps1`은 npm을 요구한다.
- `README_kr.md` 최상단 퀵스타트는 `tracelens analyze …`를 아무 설명 없이 제시하지만 이 시점의 독자는 `tracelens`가 PATH에 없다(소스는 `dotnet run --project src/TraceLens.Cli --`, ZIP은 `.\tracelens.exe`).
- 매뉴얼 게시 절의 포트 고정 문구(T36 3항), `.mcp.json` 위치(T26), 원라이너 공백 경로(T26).
- 지원 범위 표(`README_kr.md` 하단, `welcome.scope1~4`)가 §2.4의 미지원 패턴(EF Core·MediatR·gRPC·SignalR·BackgroundService·컨슈머·클래식 Startup·MVC 필터)을 이름으로 말하지 않는다.
- 같은 대상을 두 인스턴스(다른 포트)에서 동시에 분석하면 둘 다 같은 `.tracelens/`에 쓴다는 주의가 없다. 409 거절은 프로세스 안에서만 동작한다.

**변경할 파일.** `README.md`·`README_kr.md`, `docs/manual*.html`, `docs/mcp-setup*.md`, `client/src/i18n/ko.ts`·`en.ts`(`welcome.scope*`), `CONTRIBUTING.md`(pack.ps1 수동 검증에 "공백·한글 경로" 추가), `pack.ps1`(검증 절차 문구).

**구현 방법.**
1. README "배포물로 시작하기" 절 맨 앞에 "**배포물은 아직 공개 배포하지 않는다.** 소스가 있는 PC에서 `.\pack.ps1`로 만들어 옮기거나(만드는 PC에만 Node.js가 필요), 아래 '소스에서 실행하기'를 쓴다"를 넣는다. 게시 채널이 생기면 이 문단을 링크로 바꾼다.
2. 퀵스타트 코드 블록 위에 `# 배포물: .\tracelens.exe · 소스: dotnet run --project src/TraceLens.Cli --` 한 줄을 둔다.
3. 지원 범위(README 표·첫 화면 `welcome.scope*`·매뉴얼)에 "지금 읽지 않는 것" 항목을 추가한다: EF Core·MongoDB·NHibernate 접근, MediatR·델리게이트·이벤트 디스패치, gRPC·SignalR·BackgroundService·메시지 컨슈머·Hangfire 진입점, 클래식 `Startup.cs`(T30 전까지), MVC 필터·`[Authorize]` 정책. "읽지 않는 것은 결과에서 빠지고, T28·T29 이후에는 진단에 개수로 나온다"를 함께 적는다.
4. 명령줄 절에 "같은 대상을 두 인스턴스에서 동시에 분석하지 않는다"와 종료 방법(`Ctrl+C`, 백그라운드 실행 시 `Get-Process tracelens | Stop-Process`)을 적는다.
5. `pack.ps1` 수동 검증 절차 6번으로 "공백·한글이 든 경로(예: `C:\테스트 폴더\My App`)에서 doctor·analyze·serve·소스 보기를 확인한다"를 추가하고 CONTRIBUTING에도 반영한다.

**검증 방법.** 문서 대조: README·매뉴얼·첫 화면의 지원 범위 문구가 같은 항목을 말하는지 눈으로 확인. `noHardcodedKorean` 테스트 통과. 링크 깨짐 확인(`docs/` 상대 링크).

**완료 기준.** README가 존재하지 않는 배포 채널을 전제하지 않는다. 퀵스타트가 소스/배포물 두 경우를 말한다. 지원 범위가 미지원 패턴을 이름으로 말한다. 두 인스턴스·종료 방법·공백 경로 검증이 문서에 있다.

<a id="t38"></a>
### T38. 메서드 이름 검색과 통합 찾기, 진입점 그룹

**목적.** "`CreateOrderAsync`를 고쳐야 한다"는 티켓을 받은 신입이 그 메서드를 이름으로 찾을 방법이 없다. 파일 트리를 만들지 않고(제약 1) 기존 두 탐색 축 안에서 검색만 넓힌다.

**현재 상태(근거).** 검색은 보기별로 흩어져 있고 범위가 다르다: 진입점(라우트·동사·컨트롤러·핸들러, `SidePanels.tsx:28-36`), 클래스(**이름만**, `SidePanels.tsx:147-160`), DB·캐시 사이드(`DataSidePanels.tsx`). 진입점 목록은 평면 + 텍스트 필터뿐(`SidePanels.tsx:38-70`)인데 전체 흐름도 사이드에는 이미 컨트롤러별 그룹(`byController`, `SidePanels.tsx:229-239`)이 있다.

**변경할 파일.** `client/src/components/SidePanels.tsx`, `components/QuickFind.tsx`(신규), `App.tsx`, `graph/search.ts`(신규 순수 함수 + 테스트), `i18n/ko.ts`·`en.ts`, `styles.css`.

**구현 방법.**
1. `graph/search.ts`에 `searchGraph(graph, query, limit)` 순수 함수: 진입점(라우트·동사)·클래스(이름·네임스페이스)·메서드(`classes[].members[].name`·시그니처, 노드 `displayName`) 세 그룹으로 결과를 돌려준다. 대소문자 무시, 접두 일치를 우선 정렬.
2. `NamespacePanel` 검색을 멤버 이름까지 매칭하고, 매칭된 멤버를 클래스 아래 들여쓴 줄로 보여 준다(클릭 시 해당 멤버 선택).
3. 헤더에 `Ctrl+K`(포커스가 입력 요소가 아닐 때) 통합 찾기 팔레트. 결과 선택 시: 진입점 → 호출 흐름으로 이동해 선택, 클래스 → 클래스 다이어그램에서 표시, 메서드 → 호출 흐름의 해당 노드 선택(진입점에서 도달하지 못하면 상세만 열고 "이 메서드로 도달하는 진입점 없음" 표시 + 변경 영향 버튼).
4. 진입점 목록에 컨트롤러별 접기 그룹(기본 펼침, 20개 초과 컨트롤러는 접힘)을 `FlowSidePanel`의 `byController`를 공용으로 뽑아 재사용한다.

**검증 방법.** `graph/search.test.ts`(메서드 이름 매칭·정렬·상한). 수동: `Ctrl+K` → 메서드 이름 → 선택 → 상세 패널이 그 메서드를 보여 주는지. 진입점 200개짜리 실제 프로젝트에서 그룹이 접히는지.

**완료 기준.** 메서드 이름으로 찾아 상세까지 간다. 진입점이 컨트롤러별로 묶인다. `Ctrl+K`가 용어집 단축키 탭에 적혀 있다.

<a id="t39"></a>
### T39. 상세 패널의 `파일:줄` 클릭·호출 지점 마커·코드→노드 이동

**목적.** 안내 문구는 "파일·줄을 눌러 실제 코드를 확인할 수 있다"고 하는데 실제로는 눌리지 않는다. 문구와 동작을 맞추고, 코드에서 어느 줄이 호출 지점인지 보이게 하고, 코드 줄에서 대상 노드로 갈 수 있게 한다.

**현재 상태(근거).**
- `ko.ts:351 'detail.callEvidenceHint'`가 `DetailPanel.tsx:390`에 렌더되지만 근거 줄은 `<code>{site.file}:{site.line}</code>`(`DetailPanel.tsx:396,411`)로 버튼이 아니다. 같은 평문이 `DetailPanel.tsx:177,291,454`, `ImpactPanel.tsx:76`, `ContextPreview.tsx:85,102`, `DataDetailPanel.tsx:126`에도 있다.
- `CodePanel.tsx:38-47`의 데코레이션은 1행부터 끝까지 전체를 칠해 강조의 의미가 없다.
- Monaco는 읽기 전용이고 클릭 이벤트가 없다(`CodePanel.tsx:52-71`). 코드 → 노드 이동이 없다.

**변경할 파일.** `client/src/components/CodePanel.tsx`, `DetailPanel.tsx`, `DataDetailPanel.tsx`, `ImpactPanel.tsx`, `ContextPreview.tsx`, `components/SourceLink.tsx`(신규), `App.tsx`(소스 위치 상태), `styles.css`.

**구현 방법.**
1. `SourceLink` 컴포넌트(`file`, `line`, `endLine?`): 버튼으로 렌더하고 클릭 시 `App`의 `sourceTarget` 상태를 바꿔 `SourceView`가 그 구간(호출 줄 ±15줄)을 연다. 위 8곳을 모두 교체한다. 보조 동작으로 "에디터에서 열기"(기존 `openInEditor`)를 컨텍스트 메뉴 또는 Shift+클릭에 둔다.
2. `SourceView`가 `highlightLines: number[]`와 `markers: { line, targetNodeId, label }[]`를 받는다. 선택 대상의 선언 줄만 강조하고, 호출 지점 줄에는 글리프 마커(여백 아이콘)와 줄 배경을 준다. 데이터는 `graph.callSites`에서 이미 `DetailPanel.tsx:269-272`가 걸러 쓰는 것을 재사용한다.
3. 마커가 있는 줄을 클릭하면(Monaco `onMouseDown` 여백/줄 이벤트) 그 호출의 대상 노드를 선택한다. 같은 줄에 호출이 여러 개면 작은 목록을 띄운다.

**검증 방법.** `SourceLink.dom.test.tsx`(클릭이 콜백을 부르는지). 수동: 예제에서 `OrdersController.Create` 선택 → 근거의 `파일:줄` 클릭 → 코드가 그 줄로 스크롤되고 마커가 보이는지 → 마커 클릭 → `OrderService.CreateAsync`가 선택되는지.

**완료 기준.** 화면의 모든 `파일:줄`이 눌린다. 코드에서 호출 지점 줄이 구분된다. 마커 클릭으로 대상 노드로 간다. 안내 문구와 동작이 일치한다.

<a id="t40"></a>
### T40. 내비게이션(브레드크럼·URL 상태·최근 대상)

**목적.** 눌리는데 반응 없는 브레드크럼, 앱을 벗어나는 브라우저 뒤로 가기, 매번 드라이브부터 시작하는 경로 선택을 고친다.

**현재 상태(근거).**
- `navigateBreadcrumb`(`App.tsx:816-832`)는 `startup`·`data`·`middleware`에서 그냥 `return`하고 `classmap`은 index 0에서만 동작한다. 그런데 `Breadcrumb.tsx:20-27`은 마지막 항목만 `disabled`라 나머지는 활성 버튼으로 보인다.
- URL을 읽거나 쓰는 코드가 없다(`api/client.ts`의 쿼리 생성뿐). 뒤로 가기가 그래프 창을 되돌리지 않고 앱을 벗어난다. 동료에게 "이 화면"을 링크로 줄 수 없다.
- 보관되는 대상은 `solutionPath` 하나(`viewState.ts:50,78`). `PathPicker`는 드라이브 목록부터 시작한다(`PathPicker.tsx:119`).

**변경할 파일.** `App.tsx`, `components/Breadcrumb.tsx`, `state/viewState.ts`, `state/urlState.ts`(신규, 순수 함수 + 테스트), `components/PathPicker.tsx`, `i18n/ko.ts`·`en.ts`.

**구현 방법.**
1. 브레드크럼: 각 보기에서 동작을 정의한다(DB·캐시: 하위 보기 → `entryNodeId`/`pathId` 해제, 미들웨어: 하위 보기 이동, 시작 흐름: 선택 해제, 클래스 다이어그램: 포커스 해제 → 네임스페이스 전체). 동작이 없는 항목은 `disabled`로 렌더한다.
2. `urlState.ts`: `serialize(state) → '#view=callflow&entry=…&focus=a,b&sel=method:…'`, `parse(hash)`. `App`은 주요 상태 변경 시 `history.pushState`(선택·포커스 변경)와 `replaceState`(필터)로 해시를 갱신하고, `popstate`에서 파싱해 되돌린다. 없는 ID는 무시한다(기존 복원 로직 재사용). 루프백 URL이라 제약 4와 무관하고, 팀 공유는 기존 공유 보고서가 맡는다.
3. `PersistedState.recentTargets: string[]`(최대 5, 최신 우선)를 두고 경로 입력칸 옆 드롭다운으로 제공한다. `PathPicker`는 `initialPath`가 비면 최근 대상의 폴더에서 시작한다.
4. 호출 흐름에서 분기 노드를 **Shift+클릭**하면 드릴다운 없이 선택만 한다(`GraphView.tsx:106-113`).

**검증 방법.** `urlState.test.ts`(직렬화·파싱 왕복, 알 수 없는 키 무시). `viewState.test.ts`에 최근 대상 상한·중복 제거. 수동: 노드를 세 번 파고든 뒤 뒤로 가기 세 번 → 창이 되돌아오고 앱을 벗어나지 않는다. URL을 복사해 새 탭에 붙여 넣으면 같은 화면.

**완료 기준.** 눌리는 브레드크럼은 모두 동작하고 나머지는 비활성이다. 뒤로 가기가 그래프 창을 되돌린다. URL로 화면을 재현할 수 있다. 최근 대상 5개가 드롭다운에 있다.

<a id="t41"></a>
### T41. 레이아웃(라이트 테마·글꼴 크기·패널 접기·배너 스택)

**목적.** 밝은 사무실·프로젝터에서 다크 고정은 읽기 어렵고, 10~12px 글자는 조절 수단이 없으며, 1280~1366px 노트북에서 세 패널을 접을 수 없어 캔버스가 좁다.

**현재 상태(근거).**
- `styles.css:1-15`의 `:root` 하드코딩, `index.html`의 `color-scheme: dark`, `@media` 규칙 0개. Monaco `theme="vs-dark"` 고정(`CodePanel.tsx:56`).
- font-size가 전부 `px`이고 11px 이하가 100곳 이상, 14px 이상은 7곳. 요약 12px(`styles.css:36`), 그래프 힌트 11px(`:124`), 사이드 그룹 라벨 10px(`:86`).
- 최소 폭 강제(`App.tsx:77-79`의 `minLeft=190`·`minRight=280`·`minCenter=360`), 접기 버튼 없음.
- 오류·진행·공지·요약·레시피 배너가 동시에 뜰 수 있고(`App.tsx:1146-1213`) `.workspace { height: calc(100vh - 190px) }`(`styles.css:66-73`)가 고정 상수라 배너가 쌓이면 페이지가 세로 스크롤된다.
- `ImpactPanel`은 메서드 선택이 아니면 `null`(`ImpactPanel.tsx:39`)이라 패널이 나타났다 사라진다.

**변경할 파일.** `client/src/styles.css`, `index.html`, `components/CodePanel.tsx`, `App.tsx`, `components/ViewToolbar.tsx`(테마·글꼴 토글), `components/ImpactPanel.tsx`, `state/viewState.ts`(`theme`·`fontScale`·`collapsed`), `i18n/ko.ts`·`en.ts`.

**구현 방법.**
1. `[data-theme="light"]`에서 CSS 변수를 덮어쓰고, 초기값은 `prefers-color-scheme`을 따르되 헤더 토글로 고정할 수 있게 한다. Monaco 테마도 연동(`vs`/`vs-dark`). Cytoscape 스타일의 색도 변수에서 읽도록 `GraphView`의 스타일 시트를 변수 기반으로 바꾼다.
2. 글꼴: `html { font-size: var(--fs-base, 13px) }`로 두고 컴포넌트는 `rem`으로 전환한다(한 번의 치환 작업). 헤더에 A-/A/A+ 토글(0.9/1/1.15)을 저장한다.
3. 좌·우 패널 헤더에 접기 토글. 접힌 상태는 세로 띠(아이콘)로 남기고 `PersistedState.collapsed`에 저장한다. `applyColumns`가 접힘을 0폭으로 다룬다.
4. 배너를 하나의 스택 컨테이너로 묶고 최대 높이(예: 30vh)와 내부 스크롤을 준다. `.workspace` 높이를 고정 상수 대신 flex 레이아웃으로 계산한다.
5. `ImpactPanel`을 항상 렌더하고 메서드 선택이 아니면 비활성 상태로 "메서드를 고르면 영향받는 API를 찾을 수 있다"를 표시한다.

**검증 방법.** `viewState.test.ts`에 `theme`·`fontScale`·`collapsed` 저장. 수동: 라이트 테마에서 여섯 보기와 코드 패널을 모두 열어 대비가 유지되는지, 1366x768 창에서 오류+진행+레시피 배너를 동시에 띄워 페이지가 스크롤되지 않는지, 패널을 접었을 때 캔버스가 넓어지는지.

**완료 기준.** 라이트/다크 전환이 코드 패널·그래프까지 적용된다. 글꼴 3단계가 저장된다. 좌·우 패널이 접힌다. 배너가 쌓여도 작업 영역이 밀리지 않는다. 변경 영향 패널이 사라지지 않는다.

<a id="t42"></a>
### T42. MCP 도구 설명의 라우팅 힌트와 허용 목록 동기화

**목적.** 에이전트가 "이 API가 뭘 하나"에 대해 `get_context`·`get_entrypoint_flow`·`get_node` 중 무엇을 부를지 설명만으로는 알기 어렵다. 인앱 실행의 도구 허용 목록은 손으로 맞추고 있어 새 도구를 빠뜨리면 UI에서만 조용히 안 불린다.

**현재 상태(근거).** `TraceLensTools.cs`의 `[Description]`은 오판을 막는 문장(빈 목록은 증명이 아니다 등)은 훌륭하지만, 도구 간 분기 힌트는 `get_analysis_status`("먼저 부를 것") 하나뿐이다. 클래스 XML 주석(`:8-13`)의 전역 규칙은 모델에게 전달되지 않는다. `AskEndpoints.AllowedMcpTools`(`AskEndpoints.cs:20-34`)는 12개 하드코딩이고 주석도 수동 동기화를 인정한다.

**변경할 파일.** `src/TraceLens.Mcp/TraceLensTools.cs`, `src/TraceLens.Server/Endpoints/AskEndpoints.cs`, `tests/TraceLens.Mcp.Tests/`, `docs/mcp-setup*.md`, `README*.md`(도구 표에 "이렇게 물어본다" 열).

**구현 방법.**
1. 각 도구 설명 끝에 한 문장 라우팅 힌트를 붙인다. `get_context`: "설명·변경 계획·검토처럼 **답을 쓰기 위한** 문맥이 필요할 때. 구조만 훑으려면 get_entrypoint_flow". `get_entrypoint_flow`: "한 진입점의 구조. 소스와 근거까지 필요하면 get_context". `search_symbol`: "nodeId를 모를 때 먼저. 결과 id를 get_node·get_impact에 넘긴다". `get_findings`: "규칙 ID 접두사(COMP-, MW-)로 거른다". 영어 한 문장을 병기한다(도구 이름은 이미 영어).
2. `get_analysis_status` 응답에 `suggestedNextTools: string[]`(신선도·진입점 수·점검 수에 따라)를 넣는다.
3. `AllowedMcpTools`를 `TraceLensTools`의 `[McpServerTool]` 특성을 리플렉션으로 열거해 만들거나, 최소한 두 목록이 같은지 검사하는 테스트를 `TraceLens.Mcp.Tests`에 둔다.
4. README·mcp-setup의 도구 표에 "이렇게 물어본다" 예시 열을 추가한다(T25의 프리셋 질문과 같은 문장).

**검증 방법.** 허용 목록 대조 테스트. 수동: Claude Code에서 "POST /api/orders가 뭘 하는지 설명해 줘"를 물어 `get_analysis_status` → `get_entrypoints` → `get_context` 순으로 부르는지 관찰(관찰 결과는 문서에 적되 보장으로 쓰지 않는다).

**완료 기준.** 모든 도구 설명에 분기 힌트가 있다. `suggestedNextTools`가 응답에 있다. 허용 목록 불일치가 테스트로 잡힌다. 문서 도구 표에 질문 예시가 있다.

---

## 4. 이 문서를 만든 방법과 한계

- 코드 감사는 읽기 전용으로 했고, 실행 검증은 §2.2(예제 복사본 분석)와 저장소 빌드·테스트뿐이다. 나머지 "현재 상태"는 코드 읽기에 근거한 것이며 줄 번호는 커밋 `01c199c` 기준이다.
- 실제 신입 개발자를 관찰하지 않았다. 여기 적은 "신입의 질문"은 화면과 문서를 배경지식 없이 읽는다는 가정에서 나온 추정이다. 로드맵 §16.1의 소규모 관찰이 이 가정을 검증한다.
- 분석기 공백(§2.4)은 픽스처와 `samples/OrderApi`가 모두 "특성 컨트롤러 + SQL 리터럴/Dapper + IMemoryCache" 조합이라는 사실에서 출발했다. EF Core·MediatR 픽스처가 0개이므로 그 패턴에서 실제로 어떤 결과가 나오는지는 T28의 새 픽스처로 처음 확인하게 된다.
