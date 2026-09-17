# TraceLens 목표 대비 점검과 다음 작업

작성 기준: 2026-09-17 12:08 KST, 커밋 `4894133`.
이 문서는 README.md·CLAUDE.md가 말하는 목표("데모가 아니라 실전에서 계속 쓰는 로컬 도구")에 현재 코드가 도달했는지 점검하고,
부족한 부분을 **다음 세션에서 바로 구현할 수 있는 태스크**로 나눈 것이다.
확인 방법은 이 저장소의 빌드·테스트 실행, 코드 대조, 그리고 실제 사내 ASP.NET Core 프로젝트 1개를 분석한 결과다(§2).

> **사용 규칙.** 태스크 하나를 끝낼 때마다 아래 목록의 체크박스를 즉시 `[x]`로 바꾸고 완료 일시(KST, 시스템 명령으로 확인)를 덧붙인다.
> 여러 개를 몰아서 체크하지 않는다. 상세 절의 "완료 기준"을 모두 만족했을 때만 체크한다.

---

## 태스크 목록

### P0 — 판정을 믿을 수 없게 만드는 것 (실제 프로젝트에서 확인됨)

- [x] [T1. 람다 본문 안의 호출을 호출 그래프에 포함하고 트랜잭션 래퍼를 인식한다](#t1) — 완료 2026-09-17 12:58 KST (실제 프로젝트 고립 노드 118/732 → 92/765, 람다 경유 근거 266건)
- [x] [T2. 쿼리 빌더(SqlKata 등)로 실행하는 DB 접근을 인식한다](#t2) — 완료 2026-09-17 12:58 KST (실제 프로젝트 DB 접근 0 → 248, 트랜잭션 2 → 32(래퍼 30))
- [x] [T3. 솔루션 내부 캐시 래퍼 호출에서 나는 CACHE-003 오탐을 없앤다](#t3) — 완료 2026-09-17 12:58 KST (실제 프로젝트 CACHE-003 36 → 10, COMP-002 22 → 19, 캐시 접근 50 → 17)
- [x] [T4. MW-012가 `Map*` 엔드포인트 등록을 터미널 미들웨어로 오판하는 것을 고친다](#t4) — 완료 2026-09-17 13:04 KST (실제 프로젝트 MW-012 3 → 0, 인증 미들웨어가 더 이상 "실행되지 않는다"로 나오지 않는다)
- [x] [T5. `.csproj` 대상의 저장된 분석 결과를 다시 못 찾는 경로 불일치를 고친다](#t5) — 완료 2026-09-17 12:31 KST

### P0 — 첫 사용 경로가 끊겨 있는 것

- [x] [T6. 첫 화면의 "예제 분석" 버튼을 살린다](#t6) — 완료 2026-09-17 13:19 KST (소스 빌드에서 예제 준비→분석 확인: 진입점 8개)
- [x] [T7. README가 약속했지만 UI에 없는 세 흐름을 연결한다(공유 보고서 · 원본 루트 연결 · 파일별 제외)](#t7) — 완료 2026-09-17 13:19 KST (공유 보고서 모달 · 원본 루트 연결 배너 · 파일별 제외(서버 전달 포함))
- [x] [T8. 영어 UI를 완성한다(하드코딩 한국어 약 270곳)와 회귀 테스트](#t8) — 완료 2026-09-17 14:14 KST (하드코딩 한국어 281줄 → 0, 회귀 테스트 noHardcodedKorean 추가(허용 목록은 recipes.ts 하나))

### P1 — 실전 안정성·성능

- [x] [T9. 신선도 검사 비용을 줄이고 공유 태스크의 취소 토큰 버그를 고친다](#t9) — 완료 2026-09-17 14:19 KST (GameServer(126파일) /api/freshness 65ms → 15ms, 이 저장소(107파일) 33ms → 19~25ms. 두 코드베이스 모두 작아 절대값은 작다)
- [x] [T10. 서버 견고성 묶음(작업 목록 상한 · mcp.json 덮어쓰기 · Claude 실행 타임아웃 · 파일 IO 오류 매핑 · 포트 충돌 안내)](#t10) — 완료 2026-09-17 14:33 KST (10개 항목 모두)
- [x] [T11. 현장 진단 가능성(로그 파일 · errorId · 제품 버전 · Origin 검사)](#t11) — 완료 2026-09-17 14:39 KST (롤링 파일 로그 · errorId · 버전 0.8.0 고정 · Origin 검사)
- [x] [T12. 클라이언트 성능·오류 처리 묶음](#t12) — 완료 2026-09-17 14:52 KST (13개 항목 모두. 린터는 ESLint 대신 oxlint를 쓴다(typescript-eslint가 이 저장소의 TS 버전을 아직 지원하지 않는다))
- [x] [T13. 분석기 정확도 버그(제네릭 · 절대 메서드 라우트 · Compile Remove 신선도 · 설정 파일 검증)](#t13) — 완료 2026-09-17 15:04 KST (5개 항목 모두. 실제 프로젝트 엣지 1109 → 1205(제네릭·확장 메서드 정규화로 되살아난 엣지))
- [x] [T14. 접근성 기본(키보드 탐색 · 모달 포커스 · 탭 패널)](#t14) — 완료 2026-09-17 15:09 KST (다이어그램 박스·멤버 줄·점검 표 행 키보드 조작, PathPicker 포커스·Escape, tab↔tabpanel과 aria-live, Splitter 화살표 키)
- [x] [T15. 테스트 공백 메우기](#t15) — 완료 2026-09-17 15:30 KST (.NET 93 → 163개, 클라이언트 41 → 62개. 표의 모든 행에 테스트 클래스가 생겼다)
- [ ] [T16. 문서 정합성(README · data-contract · 매뉴얼 · CHANGELOG)](#t16)

### P2 — 범위 확장·측정

- [x] [T17. 분석 범위 확장(DI 등록 패턴 · virtual/override 디스패치 · 다중 호스트 · 최상위 문)](#t17) — 완료 2026-09-17 15:44 KST (5개 항목 모두. 실제 프로젝트 노드 765 → 839, 엣지 1205 → 1291, 모호 0 → 2(확정 못 한 virtual 디스패치를 정직하게 남긴 것))
- [x] [T18. 큰 솔루션 성능(측정 먼저 → 단일 패스 · 병렬화 · 뷰포트 컬링)](#t18) — 완료 2026-09-17 16:09 KST (`analyze --profile` 추가. 시맨틱 모델을 문서마다 한 번만 만들어 수집기가 나눠 쓰게 하니 실제 프로젝트 analyze 9421~10694ms → 6849~7239ms, rules 2195~2370ms → 352~412ms. 결과물은 동일. 클래스 564개에서 `layoutTree` 평균 15.6ms라 배치는 병목이 아니고, 100% 줌 1600x900에서 564개 중 11개만 그린다. 스캐너 `Task.WhenAll` 병렬화는 그 자체로는 측정 가능한 개선이 없어 그대로 적었다)
- [ ] [T19. 다중 RID 패키지 빌드(win-x64 · linux-x64 · osx-arm64)](#t19)

권장 순서: **T5 → T1 → T2 → T3 → T4** (분석 신뢰성, 서로 의존) → **T6 → T7 → T8** (첫 사용) → P1 → P2.
T1·T2·T3은 같은 실제 프로젝트로 전후 수치를 기록하면서 진행한다(§2의 수치가 기준선이다).

---

## 1. 판정: 목표에 도달했는가

**아직 도달하지 못했다.** 로드맵 A~I의 기능 골격은 모두 있고 빌드(경고 0)·테스트(.NET 84개, 클라이언트 41개)가 통과한다.
그러나 "실전에서 계속 쓰는 도구"라는 기준으로 실제 프로젝트 하나를 분석해 보면, 여섯 보기 중 **DB·캐시 보기와 미들웨어 보기의 판정이 틀리거나 비어 있고**,
호출 흐름은 트랜잭션 경로에서 끊긴다. 첫 화면의 대표 버튼은 눌리지 않고, README가 설명하는 흐름 세 개는 UI에 없다.

| 목표(README·CLAUDE.md) | 상태 | 근거 |
|---|---|---|
| 진입점 기준 호출 흐름을 근거와 함께 보여준다 | **부분** | 컨트롤러 41개는 모두 잡힌다. 그러나 람다 본문 안의 호출이 그래프에서 빠져 732개 노드 중 118개가 고립된다(§2.2). 트랜잭션 람다(`TransactionAsync(async (db, tx) => …)`) 안의 저장소 호출이 전부 사라진다. |
| 모든 판정에 근거와 확실도를 붙이고 정직하게 말한다 | **부분** | 해석률 100%(1141/1141)는 람다 밖 호출만 세서 나온 값이라 실제보다 좋게 보인다. MW-012는 "확실"로 틀린 판정 3건을 냈다(§2.4). |
| DB·캐시 접근과 보상 처리 판정 | **미달** | 실제 프로젝트에서 DB 접근 0건. SqlKata 쿼리 빌더를 써서 SQL 리터럴이 없다(§2.3). 캐시 점검 36건은 내부 래퍼 호출에서 난 추정 판정이다. |
| 미들웨어 파이프라인과 단락 지점 | **부분** | 순서는 맞게 읽는다. `Map*`를 터미널로 보아 그 뒤 미들웨어(인증 포함)를 "실행되지 않는다"고 오판한다. |
| AI 문맥을 하나의 계약으로 전달 | **거의 도달** | `/api/context`가 단일 진입점이다. 다만 `/api/ask`가 `IncludeSource`·`ExcludeFiles`를 넘기지 않아 미리보기와 실제 전달 문맥이 다를 수 있다. |
| 변경 영향 · 전후 비교 | **도달** | 알고리즘·전제 조건 검사·비교 불가 표시가 코드에 있고 테스트가 있다. UI에서 영향→호출 흐름 이동 시 경로 강조가 약하다(T12). |
| 신선도 fresh/stale/unknown | **도달(비용 문제)** | 상태 판정은 맞다. 매 30초마다 모든 파일을 다시 해시한다(T9). `<Compile Remove>` 파일은 영원히 stale이 된다(T13). |
| 로컬 전용 | **도달** | 루프백 바인딩, stdio MCP, 소스 접근 정책에서 우회를 찾지 못했다. 변경 API에 Origin 검사가 없다(T11). |
| 처음 보는 사람이 예제로 시작한다 | **미달** | 첫 화면 예제 버튼이 항상 비활성이다(`App.tsx:982`가 `samplePath={null}`). |
| 영어 UI | **미달** | 사전 구조는 있으나 컴포넌트에 한국어 문자열 약 270줄이 남아 있다. |
| `.csproj` 대상 지원 | **부분** | 분석은 되지만 서버 재시작·`serve --target` 뒤 복원이 404가 난다(§2.5). |
| 사람이 할 일(§16 관찰·공개·macOS/Linux 검증) | 미착수 | 코드로 대신하지 않는다. 이 문서의 범위 밖. |

## 2. 이번 점검에서 확인한 사실

### 2.1 저장소 상태

- `dotnet build TraceLens.sln`: 경고 0, 오류 0. `dotnet test`: Server 31 · Mcp 9 · Analyzer 44 = 84개 통과. `npx vitest run`: 41개 통과.
- 코드에 `TODO`/`FIXME`가 없다. GitHub 이슈도 없다.
- CLI `--version`·`--help`가 있고 doctor 코드 7종이 모두 있다. `.slnx`는 doctor가 사유를 말해 준다.

### 2.2 실제 프로젝트 분석 결과(기준선)

대상: `C:\WorkingDev\com2us-taskbar-hero\GameServer\GameServer.csproj` (ASP.NET Core, .NET 10, 참조 프로젝트 1개가 netstandard2.0·net10.0 다중 타깃).
데이터 계층은 **SqlKata + MySqlConnector + Dapper**, 캐시는 **CloudStructures(StackExchange.Redis 래퍼)** 다. `tracelens.config.json`은 없다(기본 설정).

| 항목 | 값 |
|---|---|
| 분석 시간 | 11.1초 (CLI `analyze`, 빌드 제외) |
| 진입점 / 노드 / 엣지 / 호출 근거 | 41 / 732 / 901 / 1023 (상한으로 80건 제외) |
| 진입점 출처 | Controller 41, 모두 확실. Minimal API 0 |
| 호출 해석 | 검사 1141 = 직접 962 + DI 179. 유일 구현체 0, 모호 0, 미해석 0. 외부 1168. 해석률 1.0 |
| **고립 노드(엣지 0개)** | **118 / 732** (Method 74 · Repository 29 · Service 15) |
| DB 접근 / 캐시 접근 | **0** / 50 (Cache 33, Redis 17) |
| 트랜잭션 | 2 (모두 `GameDbBase.TransactionAsync` 래퍼 정의 자체) |
| 쓰기 경로 | 14 (전부 캐시) |
| 점검 | 75 = CACHE-003 36(추정) · COMP-002 22(치명·확실) · COMP-001 14 · MW-012 3(확실) |
| 규칙 카탈로그 | 42 = DB 30 + MW 12. 기본 실행 26 (꺼짐 12, 샤드 전용 4) |
| graph.json | 3.1 MB. 출력 위치 `C:\WorkingDev\com2us-taskbar-hero\.tracelens\` (프로젝트 폴더가 아니라 공통 상위 폴더) |

**고립 노드 118개의 원인.** 예: `CubeRepository.LoadCubeAsync(QueryFactory, DbTransaction, long)`는 `CubeRepository.cs:116/215/318`에서 호출되지만 그래프에 호출자가 없다.
세 호출 모두 `TransactionAsync<T>(async (db, transaction) => { … await LoadCubeAsync(db, transaction, userId); … })` 형태의 **람다 본문 안**이다.
`AnalysisCallable.cs:125-126`의 `IsBoundary`가 람다에서 순회를 멈추고, `SolutionScanner.cs:253-292`는 람다를 callable로 만들지 않으므로(Minimal API 핸들러·미들웨어 `Use` 람다만 예외) 이 호출들은 엣지도, 경계 목록에도 남지 않는다.
그래서 해석률 분모에도 들어가지 않는다.

### 2.3 DB 접근 0건의 원인

`DataAccessScanner.cs:354-399`는 (a) 문자열 리터럴이 SQL 동사로 시작하거나 (b) 호출 대상이 `db.queryApis` 패턴(`Dapper.SqlMapper.*`, `*DbConnection.Query*` 등)에 맞을 때만 DB 접근으로 본다.
실제 코드는 `db.Query("player_attendance").Where(…).UpdateAsync(new { … }, transaction)`처럼 **SqlKata 빌더 체인**이라 둘 다 아니다.
결과적으로 DB·캐시 보기의 "클래스 × 테이블", "트랜잭션 경계", "보상 처리"가 이 프로젝트에서는 비어 있다.

### 2.4 틀린 판정 두 가지

- **MW-012(확실) 3건.** 파이프라인은 `RequestLogging → UseExceptionHandler → MapOpenApi → UseSwaggerUI → UseHttpsRedirection → GameAuthMiddleware → MapControllers`다.
  `MiddlewareScanner.cs:163`이 이름이 `Map`으로 시작하면 터미널로 간주해 `MapOpenApi` 뒤의 세 단계를 "엔드포인트 실행 뒤에 등록되어 실행되지 않는다"고 판정했다.
  `WebApplication`에서 `MapOpenApi()`·`MapControllers()`는 엔드포인트 등록이지 미들웨어가 아니며, 엔드포인트 실행은 파이프라인 맨 끝에서 일어난다. 인증 미들웨어는 실제로 실행된다.
- **CACHE-003(추정) 36건.** 예: `BossRushSeasonBatchScheduler.cs:155`의 `rankCache.SetCurrentSeasonAsync(next)`. `IsCacheApi`의 기본 패턴 `*Cache.*`가 솔루션 안 래퍼 타입(`…RankCache`)에도 걸려,
  래퍼 메서드 호출 자체를 "SET, 만료 인자 없음"으로 판정했다. 만료 시간은 래퍼 안에서 넘긴다. 대상(target)도 키가 아니라 변수 이름 `next`다.
  COMP-002 22건도 이런 래퍼 호출로 만든 캐시 쓰기 경로에서 나왔으므로 T3 뒤에 다시 세어야 한다.

### 2.5 복원 경로 불일치

`TraceLensAnalyzer.cs:382`의 `ResolveRootDirectory`는 분석 가능한 프로젝트들의 공통 상위 폴더를 루트로 잡아 `<루트>/.tracelens/graph.json`에 쓴다.
반면 `AnalyzeEndpoints.cs:63-65`(`POST /api/analyze/load`)와 `ServerHost.cs:108-114`(`serve --target` 시작 시 복원)는 `<대상 파일의 폴더>/.tracelens/graph.json`을 읽는다.
참조 프로젝트가 형제 폴더에 있는 `.csproj`를 분석하면 두 경로가 달라, 서버를 다시 켜면 "저장된 분석 결과가 없다"(404)가 나고 재분석해야 한다. 이번 점검에서 재현했다.
`McpConfigWriter`·`SnapshotStore`는 `graph.RootDirectory`를 써서 문제없다.

### 2.6 코드 감사에서 확인한 그 밖의 사실

각 항목의 근거 위치는 해당 태스크 절에 적었다. 요약하면:

- 클라이언트: 예제 버튼 비활성, `buildReport`·`adoptImportedGraph`·`getClasses`·`getAnalysisStatus`는 호출처가 없다, 파일별 제외 UI 없음, 하드코딩 한국어 다수, Cytoscape가 App 리렌더마다 재생성, 분석 작업 실패 시 전체 분석을 한 번 더 실행, 진입점 검색 없음, 키보드 탐색 없음.
- 서버: 신선도 검사가 매번 전체 해시, 공유 태스크가 첫 호출자의 취소 토큰을 물려받음, 분석 작업 목록 무제한, `.tracelens/mcp.json`을 매번 덮어씀, Claude 실행 타임아웃 없음, 로그 파일 없음, 제품 버전 미설정(항상 1.0.0), 변경 API에 Origin 검사 없음.
- 분석기: 제네릭 타입 멤버 호출의 노드 ID 불일치, 절대 메서드 라우트(`[HttpGet("/health")]`) 결합 오류, `<Compile Remove>` 파일이 영구 stale, 설정 파일 검증 없음, DI 등록은 제네릭 인자 2개짜리만 인식, virtual/override 디스패치 없음, `Program.cs` 하나만 분석.

---

## 3. 태스크 상세

<a id="t1"></a>
### T1. 람다 본문 안의 호출을 호출 그래프에 포함하고 트랜잭션 래퍼를 인식한다

**목적.** `Task.Run(() => …)`, LINQ 람다, 트랜잭션 래퍼에 넘기는 람다 안의 호출이 그래프·경계 어디에도 남지 않는 것을 고친다. 실제 프로젝트에서 732개 중 118개 노드가 고립된 직접 원인이다.

**설계 결정.** 람다를 별도 노드로 만들지 않고, **감싸는 callable의 호출로 귀속하되 확실도를 낮추고 근거에 "람다 본문"을 남긴다.**
이유: 람다 노드를 만들면 "부모 → 람다" 엣지가 필요한데 이는 메서드 호출이 아니라 CLAUDE.md 제약 2(엣지는 메서드 호출만)와 충돌한다.
CHANGELOG의 "람다 본문 호출을 부모의 직접 호출로 잡지 않는다"는 결정은 "**직접(확실)** 호출로 잡지 않는다"로 좁힌다. 추정으로 표시하고 근거를 남기면 제약 5(정직)를 지킨다.
DataAccessScanner는 이미 람다 안의 SQL·캐시 호출을 부모에 귀속하고 있으므로(`DataAccessScanner.cs:266,448`) 이 결정이 두 서브시스템을 일치시킨다.

**변경할 파일.**
- `src/TraceLens.Analyzer/AnalysisCallable.cs` — `IsBoundary`를 `LocalFunctionStatementSyntax`만 경계로 남긴다. 람다는 순회하되, 순회 중 만나는 각 노드에 대해 "어떤 람다 안에 있는지"를 함께 넘긴다(스택에 `(node, lambdaDepth, outerLambdaSyntax)`).
- `src/TraceLens.Analyzer/SolutionScanner.cs`, `CallGraphWalker.cs` — 이미 callable로 등록된 람다(Minimal API 핸들러, 미들웨어 `Use` 람다)의 구문 범위는 부모 순회에서 건너뛴다. 방법: 진입점 스캔이 만든 람다 callable들의 `SyntaxNode.Span`을 `HashSet<(filePath, spanStart)>`로 넘기고, 순회 중 그 범위에 들어가면 `continue`.
- `src/TraceLens.Analyzer/InvocationResolver.cs`, `CallSiteCollector.cs`, `Models/CallSite.cs`, `Models/GraphEdge.cs` — `CallSite`에 `ViaLambda: bool`(기본 false)을 추가. 람다 안 호출은 해석 종류(Direct/DiRegistration/…)는 그대로 두고 `Confidence = Estimated`, `Note = "람다 본문 안의 호출. 실제 실행 여부는 이 람다를 받는 {수신 메서드 표시 이름}가 결정한다"`. 수신 메서드는 람다가 인자로 들어간 `InvocationExpressionSyntax`의 심볼에서 얻는다(변수에 대입된 람다면 "대리자 변수 {이름}").
  엣지 확실도는 그 엣지의 call site 중 가장 높은 것을 따른다(직접 호출이 하나라도 있으면 확실).
- `src/TraceLens.Analyzer/DataAccessScanner.cs`, `DataAccessOptions.cs`, `Models/DataAccess.cs` — **트랜잭션 래퍼**:
  1. 설정 `db.transactionWrappers: string[]`(패턴). 기본값 `["*.TransactionAsync", "*.InTransactionAsync", "*.ExecuteInTransaction*", "*.RunInTransaction*", "*.WithTransaction*"]`.
  2. 자동 감지: 솔루션 안 메서드 중 본문에 `TransactionBegin` 사이트가 있고, 대리자 타입 파라미터(`Func<…>`/`Action<…>`)를 본문에서 호출(`param(...)`, `param.Invoke(...)`)하면 래퍼로 등록한다. `GameDbBase.TransactionAsync(Func<QueryFactory, MySqlTransaction, Task<TxResult<T>>>)`가 이 경우다.
  3. 사이트 수집 시 각 사이트의 조상 람다가 래퍼 호출의 인자이면 `InTransaction = true`로 두고, 그 래퍼 호출 지점을 `TransactionScopeInfo`(종류 `Wrapper`, 근거: 호출 줄과 래퍼 이름)로 기록한다. 쓰기 경로의 note에 "트랜잭션 래퍼 X 안"을 적는다.
  4. 람다가 래퍼가 아닌 메서드에 넘어가면(예: `Task.Run`) 기존과 같이 트랜잭션 밖으로 본다.
- `client/src/types/graph.ts`, `client/src/components/DetailPanel.tsx`, `src/TraceLens.Context/ContextMarkdown.cs` — 근거 표시에 "람다 본문(추정)" 배지·문구. i18n 키 추가.
- `docs/data-contract.md` — `callSites[].viaLambda`(선택, 기본 false), `dataAccess.transactions[].kind`(`Explicit`/`Wrapper`) 추가. 필드 추가만이라 schema는 v7 유지(이행 규칙 절에 "없으면 false" 명시).

**검증.**
- 픽스처 `tests/TraceLens.Analyzer.Tests/FixturesData`에 추가: `OrderService.CreateAsync`가 `_db.InTransactionAsync(async tx => { await _repo.InsertAsync(tx); await _ledger.AppendAsync(tx); })`를 호출하고, 별도 메서드가 `Task.Run(() => _svc.Notify())`, `items.Select(x => Map(x))`를 쓴다.
- 기대: `CreateAsync → InsertAsync`, `CreateAsync → AppendAsync`, `X → Notify`, `X → Map` 엣지가 있고 각 call site가 `viaLambda=true`, 확실도 `Estimated`. `CreateAsync`의 쓰기 경로는 `inTransaction=true`이고 COMP-002가 나지 않는다. `Task.Run` 안의 쓰기는 트랜잭션 밖이다.
- Minimal API 핸들러 람다 안의 호출이 `Program.<Main>$`의 호출로 **중복** 귀속되지 않는다(기존 `MinimalApiEntryPointTests`의 람다 경계 테스트 유지).
- 실제 프로젝트 재분석: 고립 노드 118개가 줄어든 수치를 working_log에 기록한다(분모 732 함께).

**완료 기준.** 위 테스트 통과, `dotnet build` 경고 0, 실제 프로젝트에서 `LoadCubeAsync`에 호출자가 생기고 근거에 "람다 본문" 표시가 보인다.

<a id="t2"></a>
### T2. 쿼리 빌더(SqlKata 등)로 실행하는 DB 접근을 인식한다

**목적.** SQL 리터럴 없이 빌더 체인으로 쿼리하는 코드베이스에서 DB·캐시 보기가 비어 있는 문제를 고친다.

**변경할 파일.**
- `src/TraceLens.Analyzer/DataAccessOptions.cs` — 새 옵션 `db.queryBuilders: QueryBuilderOption[]`:
  ```json
  {
    "root": "SqlKata.Execution.QueryFactory.Query",
    "tableArgumentIndex": 0,
    "queryType": "SqlKata.Query",
    "terminals": {
      "GetAsync": "Read", "Get": "Read", "FirstOrDefaultAsync": "Read", "FirstAsync": "Read",
      "CountAsync": "Read", "ExistsAsync": "Read", "PaginateAsync": "Read",
      "InsertAsync": "Write", "InsertGetIdAsync": "Write", "UpdateAsync": "Write",
      "DeleteAsync": "Write", "IncrementAsync": "Write", "DecrementAsync": "Write"
    },
    "joinMethods": ["Join", "LeftJoin", "RightJoin"]
  }
  ```
  SqlKata 항목을 **기본값에 포함**한다(설정 없이도 동작). `db.queryBuilders`를 명시하면 기본값을 대체한다.
- `src/TraceLens.Analyzer/DataAccessScanner.cs` — 사이트 분류(`:354` 부근)에 세 번째 경로 추가:
  1. 호출 이름이 어떤 빌더의 terminal에 있고, 수신식의 타입이 `queryType`(또는 그 파생)이면 후보다.
  2. 수신식 체인을 `MemberAccessExpressionSyntax → InvocationExpressionSyntax → …`로 거슬러 올라가 `root` 메서드 호출을 찾는다. 로컬 변수에 담긴 경우(`var q = db.Query("t"); … q.GetAsync()`)는 같은 메서드 안 선언자의 초기화식을 한 단계 따라간다.
  3. 테이블은 `root` 호출의 `tableArgumentIndex` 인자를 `SemanticModel.GetConstantValue`로 읽는다. 상수면 `Certain`, 아니면 대상 `"?"`에 `Estimated`, note에 원문 식.
  4. `statement`는 `"{UPDATE|SELECT|INSERT|DELETE} {table}"`로 합성한다(terminal의 연산과 이름에서 동사 결정). `joinMethods` 인자의 상수 테이블은 Read 대상으로 추가한다.
  5. **transaction 인자**: terminal 호출의 인자 중 타입이 `System.Data.IDbTransaction`/`System.Data.Common.DbTransaction` 파생이면 `InTransaction = true`, 근거 "transaction 인자 전달". T1의 래퍼 감지와 합쳐 둘 중 하나면 트랜잭션 안으로 본다.
  6. 데이터소스: 체인 맨 앞 수신 변수의 초기화 호출을 `connectionFactories` 패턴과 대조한다(예: `Db()`). 못 찾으면 `"Db(추정)"`.
- `src/TraceLens.Analyzer/DataRuleEngine.cs` — 합성 statement가 기존 SQL 규칙(RW·PERF·IDX)에 들어가면 오탐이 나므로, 빌더 사이트에는 `IsSynthesized` 표시를 두고 SQL 본문을 보는 규칙은 건너뛴다(카탈로그에서 "빌더 사이트는 검사하지 않음"을 규칙 설명에 적는다).
- `docs/data-contract.md` §7 설정 표에 `db.queryBuilders` 추가. 매뉴얼의 설정 절 갱신.

**검증.**
- 픽스처에 NuGet을 넣지 않고 **스텁 타입**을 둔다: `FixturesData/Stubs/SqlKataStubs.cs`에 `namespace SqlKata { class Query { Query Where(...); } }`, `namespace SqlKata.Execution { class QueryFactory { Query Query(string table); } static class QueryExtensions { Task<T> FirstOrDefaultAsync<T>(this Query q, IDbTransaction? tx = null); Task<int> UpdateAsync(this Query q, object data, IDbTransaction? tx = null); … } }`. 패턴 매칭은 전체 이름으로 하므로 스텁으로 충분하다.
- 기대: `db.Query("player_attendance").Where(...).UpdateAsync(new {...}, transaction)` → Write, 대상 `player_attendance`, `inTransaction=true`, 확실. `db.Query(tableVar)` → 대상 `?`, 추정. 클래스 × 테이블 매트릭스에 테이블이 나타난다.
- 실제 프로젝트 재분석: DB 접근 수와 데이터소스 목록을 기록한다(전: 0).

**완료 기준.** 테스트 통과, 실제 프로젝트의 DB·캐시 보기에서 테이블별 접근과 트랜잭션 경계가 보인다. EF Core(`DbSet<T>`)는 이 태스크 범위 밖이다(§4).

<a id="t3"></a>
### T3. 솔루션 내부 캐시 래퍼 호출에서 나는 CACHE-003 오탐을 없앤다

**목적.** `*Cache.*` 패턴이 솔루션 안 래퍼 타입에 걸려 래퍼 호출마다 "TTL 없이 SET"이 나고, 접근이 이중으로 잡히는 것을 고친다.

**변경할 파일.**
- `src/TraceLens.Analyzer/DataAccessScanner.cs`
  - `IsCacheApi(:587)`에서 **호출 대상 심볼이 솔루션 안에 선언된 메서드면 캐시 API로 보지 않는다**(`calleeSymbol.ContainingAssembly`가 솔루션 프로젝트의 어셈블리인지 확인). 그 호출은 일반 솔루션 호출로 남고, 래퍼 본문에서 실제 클라이언트 API를 만나는 곳에서만 캐시 사이트를 만든다.
  - 호출자는 기존 **접근 요약 전파**(`CompensationAnalyzer.BuildEffects`)로 래퍼의 캐시 접근을 물려받으므로 쓰기 경로에는 계속 나타난다. 표시는 "래퍼 `RankCache.SetCurrentSeasonAsync` 경유"로 한다(요약 전파 결과에 경유 메서드를 남기는 필드가 없으면 `AccessSummary`에 `ViaNodeId` 추가).
  - `HasExpiryArgument(:818)`에 `TimeSpan?`(Nullable), 명명 인자, `CommandFlags` 뒤에 오는 만료 인자를 포함한다.
  - 키 추출: CloudStructures처럼 키가 생성자에 있고 호출에는 값만 있는 API는 `target`을 "필드 {이름}(추정)"으로 적는다. 변수 이름을 키로 쓰지 않는다.
- `src/TraceLens.Analyzer/DataAccessOptions.cs` — 기본 `cacheApis`에 `CloudStructures.*` 추가. 내부 래퍼를 강제로 클라이언트로 보고 싶을 때 쓰는 `db.cacheWrappers: string[]`(선택) 추가.
- `src/TraceLens.Analyzer/DataRuleEngine.cs` — CACHE-003은 `HasExpiryArgument`를 판단할 수 있는 **외부 클라이언트 호출**에서만 낸다. 래퍼 경유 접근에는 내지 않는다.
- 접근 목록 중복 제거: 같은 `(file, line, target, operation)`은 하나로 합친다.

**검증.**
- 픽스처: `SessionCache` 래퍼(`SetAsync(key, value)` 안에서 `_redis.StringSetAsync(key, value, TimeSpan.FromMinutes(5))`) + 호출자. 기대: 호출자에 CACHE-003 없음, 래퍼 본문에도 없음(만료 있음). 만료 없이 부르는 두 번째 래퍼는 래퍼 본문 줄에 1건만.
- 실제 프로젝트 재분석: CACHE-003 36 → n, COMP-002 22 → n을 기록하고, 남은 판정 5건을 직접 열어 맞는지 확인한다(맞은 수/확인한 수로 적는다).

**완료 기준.** 테스트 통과, 실제 프로젝트에서 래퍼 호출 줄에 CACHE-003이 없고 접근 목록에 같은 줄이 두 번 나오지 않는다.

<a id="t4"></a>
### T4. MW-012가 `Map*` 엔드포인트 등록을 터미널 미들웨어로 오판하는 것을 고친다

**목적.** "확실"로 표시된 틀린 판정을 없앤다.

**변경할 파일.**
- `src/TraceLens.Analyzer/MiddlewareScanner.cs:163` — 터미널 판정을 다음으로 바꾼다.
  - 터미널: `UseEndpoints(...)`, `app.Run(...)`, `UseWelcomePage`, `UseStatusCodePages*`가 아닌 명시적 종단(`Run`).
  - **엔드포인트 등록**(`MapGet/MapPost/…/MapControllers/MapOpenApi/MapHub/MapGrpcService/MapHealthChecks/MapRazorPages/MapGroup/MapFallback*`)은 단계 종류 `EndpointRegistration`으로 따로 표시하고 터미널이 아니다. 파이프라인 그림에서는 맨 끝의 "엔드포인트 실행" 가상 단계 하나로 묶어 보여준다.
  - `IApplicationBuilder.Map(path, branch)`(분기)는 조건부 단계로 이미 처리하는 `UseWhen/MapWhen`과 같이 다룬다.
- `Models/MiddlewarePipeline.cs`, `client/src/types/graph.ts`, `client/src/components/MiddlewareView.tsx` — 단계에 `kind: "Middleware" | "EndpointRegistration" | "Terminal"` 추가. 뷰에서 등록 단계는 옅게, 마지막에 "엔드포인트 실행" 마커.
- MW-012 문구: "UseEndpoints/Run 뒤에 등록됐다"로 조건을 정확히 적는다. 규칙 카탈로그 설명도 갱신.
- 같은 오판이 MW-001~011의 "순서" 판정에 있는지 `MiddlewareScanner.cs:498-510` 주변을 확인한다(터미널 인덱스를 쓰는 규칙 전부).

**검증.**
- `MiddlewarePipelineTests`에 `app.MapOpenApi(); app.UseSwaggerUI(); app.UseAuthentication(); app.MapControllers();` 픽스처 추가. 기대: MW-012 0건. `app.UseEndpoints(e => …); app.UseXxx();`에서는 1건.
- 실제 프로젝트 재분석: MW-012 3 → 0.

**완료 기준.** 테스트 통과, 실제 프로젝트에서 인증 미들웨어가 "실행되지 않는다"로 나오지 않는다.

<a id="t5"></a>
### T5. `.csproj` 대상의 저장된 분석 결과를 다시 못 찾는 경로 불일치를 고친다

**목적.** 분석기가 쓰는 위치(공통 상위 폴더)와 복원이 읽는 위치(대상 파일 폴더)를 일치시킨다.

**변경할 파일.**
- `src/TraceLens.Context/GraphLocator.cs`(신규) — `FindForTargetAsync(string targetPath, CancellationToken)`: 대상 파일의 폴더에서 시작해 드라이브 루트 직전까지 조상 폴더를 올라가며 `.tracelens/graph.json`이 있으면 **헤더만**(`schemaVersion`, `solutionPath`, `rootDirectory`) `Utf8JsonReader`로 읽고, `solutionPath`가 대상의 전체 경로와 같은(OrdinalIgnoreCase) 첫 파일을 돌려준다. 최대 8단계. 여러 개가 맞으면 가장 가까운 것. 없으면 null과 "찾아본 경로 목록".
- `src/TraceLens.Server/Endpoints/AnalyzeEndpoints.cs:60-75`, `src/TraceLens.Server/ServerHost.cs:108-114` — 위 로케이터 사용. 404 메시지에 찾아본 경로를 details로 넣는다.
- `src/TraceLens.Context/EnvironmentDiagnostics.cs:159`(OUTPUT_NOT_WRITABLE) — 쓰기 가능 검사 위치도 실제 출력 위치와 같게 한다. 분석 전에는 루트를 모르므로 "대상 폴더와 그 조상 중 `.sln`이 있는 폴더"를 함께 검사하고 결과에 검사한 경로를 적는다.
- `src/TraceLens.Cli/CommandRouter.cs:46` 도움말 문구 "대상 폴더의 .tracelens/graph.json" → "대상과 참조 프로젝트의 공통 상위 폴더의 .tracelens/graph.json(analyze 결과의 outputPath)".

**검증.** `AnalyzeEndpointsTests`에 "그래프가 조상 폴더에 있는 csproj 대상 복원" 테스트 추가. 수동: 실제 프로젝트에서 `tracelens serve --target …\GameServer.csproj` 시작 직후 `/api/analysis-status`가 200.

**완료 기준.** 테스트 통과, 재시작 뒤 재분석 없이 복원된다.

<a id="t6"></a>
### T6. 첫 화면의 "예제 분석" 버튼을 살린다

**목적.** `App.tsx:982`가 `samplePath={null}`을 넘겨 `WelcomePanel.tsx:52`의 버튼이 항상 비활성이다. 첫 화면의 대표 행동이 죽어 있다.

**설계.** 예제는 도구 설치 폴더 안에서 직접 분석하지 않는다(.NET tool 설치 폴더는 쓰기 불가일 수 있고 `.tracelens` 산출물이 생긴다). CLI `demo`처럼 **사용자 영역에 복사한 뒤** 분석한다.

**변경할 파일.**
- `src/TraceLens.Cli/DemoSamples.cs` → `src/TraceLens.Context/DemoSamples.cs`로 옮긴다(순수 파일 로직이라 Context에 둘 수 있다). CLI와 Server가 함께 쓴다. 탐색 위치는 기존과 같이 `AppContext.BaseDirectory/samples`, CWD/samples.
- `src/TraceLens.Server/Endpoints/SampleEndpoints.cs`(신규)
  - `GET /api/samples` → `samples.json`의 목록 + `available`(소스 폴더 존재 여부).
  - `POST /api/samples/{id}/prepare { destination?: string }` → 기본 목적지 `%LOCALAPPDATA%\TraceLens\samples\{id}` (이미 있으면 덮어쓰지 않고 그대로 사용, `overwrite: true`일 때만 다시 복사). 응답 `{ targetPath, copied: bool }`.
- `client/src/api/client.ts` — `getSamples()`, `prepareSample(id)`.
- `client/src/App.tsx`, `client/src/components/WelcomePanel.tsx` — 버튼 클릭 → prepare → `solutionPath` 설정 → 기존 분석 작업 시작. 예제의 `expected` 목록을 분석 완료 뒤 `GuidedWalkthrough`의 첫 단계 안내에 보여준다.
- i18n 키(`welcome.sample.preparing`, `welcome.sample.unavailable` 등). 예제가 없으면(소스 빌드에서 `samples` 폴더가 없는 경우) 버튼 대신 사유를 보여준다.

**검증.** Server 테스트: `/api/samples` 목록, `prepare`가 임시 폴더에 복사하고 두 번째 호출은 복사하지 않는다. 브라우저: 첫 화면 → 예제 분석 → 진입점 8개 → 안내 진행.

**완료 기준.** 소스 빌드(`run.ps1`)와 portable ZIP 둘 다에서 버튼이 동작한다.

<a id="t7"></a>
### T7. README가 약속했지만 UI에 없는 세 흐름을 연결한다

**목적.** `client/src/api/client.ts`의 `buildReport`, `adoptImportedGraph`에 호출처가 없고, `ContextPreview.tsx:59-63`의 파일 목록은 읽기 전용이다. README는 세 가지 모두 있다고 쓴다. 문구를 낮추지 않고 구현한다.

**변경할 파일.**
1. **공유 보고서** — `client/src/components/ShareReportDialog.tsx`(신규): AskPanel 하단 "공유 보고서…" 버튼 → 모달. 옵션 체크박스 `상대 경로 포함(includePaths)`, `테이블·키 이름 포함(includeStorageNames)` → `buildReport` 호출 → Markdown 미리보기 → "복사", ".md로 저장". 절대 경로·소스 본문은 서버가 항상 제외한다(`ShareReportBuilder.cs`), 모달에도 그 사실을 적는다.
2. **가져오기 → 원본 루트 연결** — `App.tsx:396-410` 가져오기 성공 뒤 상단에 지속 알림 "가져온 분석입니다. 원본 루트를 연결하면 소스를 볼 수 있습니다 [연결…]". `PathPicker`를 폴더 선택 모드로 열어(`/api/browse`는 이미 폴더를 준다) `adoptImportedGraph(root)` 호출. 성공하면 알림 제거·소스 보기 활성. `409 INPUT_MISMATCH`면 details의 불일치 목록(어떤 파일 해시가 다른지)을 알림 안에 표시한다.
3. **파일별 제외** — `ContextPreview.tsx`의 파일 목록에 체크박스. `AskPanel` 상태 `excludedFiles: string[]` → `buildContext`와 `/api/ask` 요청 모두에 `excludeFiles`로 전달. **서버 수정 필수**: `src/TraceLens.Server/Endpoints/AskEndpoints.cs:110-120`이 `ContextRequest`를 만들 때 `IncludeSource = request.IncludeSource, ExcludeFiles = request.ExcludeFiles`를 넘기지 않아 미리보기와 실제 프롬프트가 달라질 수 있다. `AskRequest`에 두 필드를 추가하고 그대로 넘긴다.

**검증.** `AskEndpointsTests`: `excludeFiles`에 넣은 파일의 소스가 프롬프트에 없다, `includeSource=false`면 소스 블록이 없다. 클라이언트: 세 흐름을 브라우저로 확인. `api/client.ts`의 dead export 4개(`buildReport`, `adoptImportedGraph`, `getClasses`, `getAnalysisStatus`) 중 앞 둘은 호출처가 생기고, 뒤 둘은 쓰거나 지운다.

**완료 기준.** README의 해당 문장이 사실이 된다.

<a id="t8"></a>
### T8. 영어 UI를 완성한다와 회귀 테스트

**목적.** 사전 구조는 있으나 컴포넌트에 한국어가 남아 영어로 바꿔도 화면 대부분이 한국어다.
한글 포함 줄 수(주석 포함): `DetailPanel.tsx` 62, `DataDetailPanel.tsx` 58, `DataAccessView.tsx` 58, `MiddlewareView.tsx` 24, `SidePanels.tsx` 20, `PathPicker.tsx` 16, `DiagramCanvas.tsx` 16, `DataSidePanels.tsx` 14, `ClassDiagram.tsx` 8, `StartupFlowView.tsx` 6, `AskPanel.tsx` 6, `GuidedWalkthrough.tsx` 5, `Splitter.tsx`(title), `GraphView.tsx`.

**방법.**
- 파일 단위로 훑어 사용자에게 보이는 문자열을 `client/src/i18n/ko.ts`에 키로 옮긴다. 키는 화면별 접두사(`detail.*`, `data.*`, `mw.*`, `startup.*`, `picker.*`, `canvas.*`). `en.ts`는 타입으로 강제되므로 누락 시 컴파일 오류.
- `I18nProvider.tsx`에서 언어가 바뀔 때 `document.documentElement.lang`을 갱신한다(`index.html`의 `lang="ko"` 고정 해제).
- 새 테스트 `client/src/i18n/noHardcodedKorean.test.ts`: Node `fs`로 `src/components/**/*.tsx`, `src/App.tsx`를 읽어 주석(`//`, `/* */`, JSX `{/* */}`)을 제거한 뒤 `[가-힣]`이 남으면 실패. 허용 목록(파일:내용)은 테스트 안 상수로 두고 비워 나간다.
- **범위 밖(별도 결정)**: 서버가 만드는 한국어 문자열(규칙의 `what/why/fix`, doctor 메시지, API 오류 메시지, `limitations`). 오류는 이미 `code`로 번역하므로 UI가 `error.<code>`를 우선한다. 규칙 문구는 `ruleId` 기준으로 클라이언트 사전에 영어를 두는 방안(`rule.COMP-002.what` 등)을 T16에서 결정한다.

**검증.** `npm test` 통과(새 테스트 포함), 영어로 전환해 여섯 보기·상세·질문 패널·모달을 한 바퀴 돈다.

**완료 기준.** 새 테스트의 허용 목록이 비어 있고, 로드맵 A 완료 기준 "영어 전환 시 버튼·오류·접근성 라벨에 번역 누락 없음"을 만족한다.

<a id="t9"></a>
### T9. 신선도 검사 비용을 줄이고 공유 태스크의 취소 토큰 버그를 고친다

**근거.** `GraphFreshnessEvaluator.cs:68-134`는 호출마다 `fileHashes`의 모든 파일과 manifest 입력을 SHA-256으로 다시 계산하고 소스 루트 전체를 열거한다(병렬 8). UI는 30초마다 부른다(`FreshnessBadge.tsx:12`).
`GraphFreshnessService.cs:35`는 진행 중 태스크를 공유하면서 **첫 호출자의 취소 토큰**으로 실행하므로 첫 탭이 닫히면 다른 탭이 `TaskCanceledException`으로 500을 받는다.

**방법.**
- `GraphFreshnessService`: 공유 평가는 `CancellationToken.None`으로 실행하고 호출자는 `task.WaitAsync(callerToken)`으로 기다린다. 결과를 `snapshotId` 기준 5초 TTL로 캐시한다.
- `GraphFreshnessEvaluator`: 메모리 캐시 `path → (length, lastWriteUtc, hash)`. 파일의 길이·수정 시각이 캐시와 같으면 해시를 건너뛴다. 처음 한 번은 전체 해시다. 파일 감시(FileSystemWatcher)는 쓰지 않는다(CLAUDE.md 제약 3과 혼동을 피한다).
- 디렉터리 열거 결과도 같은 TTL로 캐시한다.
- 측정: 이 저장소(분석 대상으로 삼아)와 실제 프로젝트에서 첫 호출·두 번째 호출 시간을 기록한다. 실제 프로젝트는 126개 파일이라 작다는 점을 함께 적는다.

**검증.** `GraphFreshnessServiceTests`에 "첫 호출자 취소가 두 번째 호출자에 전파되지 않음", "변경 없는 두 번째 호출은 해시를 다시 계산하지 않음(평가기 카운터로 확인)" 추가.

**완료 기준.** 테스트 통과, 두 번째 호출이 첫 호출보다 빠른 것을 수치로 기록.

<a id="t10"></a>
### T10. 서버 견고성 묶음

각 항목은 독립적으로 작고, 한 커밋씩 처리한다.

| 항목 | 근거 | 변경 |
|---|---|---|
| 분석 작업 목록이 무제한으로 자란다 | `AnalysisJobService.cs:67,80` — 딕셔너리에서 제거하지 않고 `CancellationTokenSource`도 dispose하지 않음 | 종료된 작업은 최근 20개만 유지, 그 외는 10분 뒤 제거. `finally(:167)`에서 CTS dispose. 같은 대상의 두 번째 작업은 큐잉 대신 `409 JOB_ALREADY_RUNNING`으로 거절(현재는 둘 다 실행되고 나중 것이 이긴다). |
| `.tracelens/mcp.json`을 매번 덮어쓴다 | `McpConfigWriter.cs:80` `File.Create` | 기존 파일이 있으면 JSON을 읽어 `mcpServers.tracelens` 항목만 갱신하고 나머지 키는 보존. 파싱 실패 시 `.bak`을 남기고 새로 쓴다. README의 "기존 설정을 덮어쓰지 않는다"와 일치시킨다. |
| Claude 실행에 타임아웃·취소 수단이 없다 | `ProcessRunner.cs:100` 무한 대기, 취소는 SSE 끊김뿐 | `AskRequest.timeoutSeconds`(기본 600) + `DELETE /api/ask/{runId}`. 타임아웃 시 프로세스 트리 종료 후 `kind: "error", code: "AGENT_TIMEOUT"` 이벤트. |
| `claude`가 없을 때 OS 메시지 그대로 노출 | `AskEndpoints.cs:179-184` | `Win32Exception`을 `AGENT_NOT_FOUND`로 바꾸고 doctor의 안내 문구(설치 링크)를 details에 넣는다. |
| 비JSON stdout을 답변 본문으로 표시 | `ClaudeStreamParser.cs:25-28` | 파싱 실패 줄은 `kind: "log"`로 보낸다(stderr와 같은 취급). |
| 파일 IO 예외가 500으로 샌다 | `SourceReader.cs:47`, `SnapshotStore.cs:89,100`, `SourceEndpoints.cs:30`, `SnapshotEndpoints.cs:65` | `IOException`/`UnauthorizedAccessException`을 `SOURCE_DENIED`(사유: locked/permission)·`SNAPSHOT_READ_FAILED`로 매핑. |
| `serve`가 포트 충돌 시 스택 트레이스로 죽는다 | `CommandRouter.cs:86` → `ServerHost.cs:99` | 바인딩 예외를 잡아 doctor의 `PORT_IN_USE` 문구와 `--port` 힌트를 출력하고 종료 코드 1. `--port` 범위(1~65535) 검증. |
| 가져오기 본문 무제한·임시 폴더 미정리 | `AnalyzeEndpoints.cs:103-119` | 크기 상한(예: 64 MB)과 초과 시 `GRAPH_TOO_LARGE`. `tracelens-imported/` 는 시작 시 7일 지난 것을 지운다. |
| 스냅샷 보관 상한이 안내뿐 | `SnapshotStore.cs:29-30`, `SnapshotEndpoints.cs:44-46` | 상한(20) 도달 시 저장을 거절하지 않되 응답에 `pruneCandidates`(가장 오래된 것들)를 주고, UI에서 "정리" 버튼으로 삭제하게 한다. 자동 삭제는 하지 않는다. |
| MCP `compare_snapshots`에 `expectedSnapshotId` 없음, `get_source`/`get_node` 거부가 예외로 나감 | `TraceLensTools.cs:225,263,286` | 인자 추가, 거부는 `{ code: "SOURCE_DENIED", … }` 구조로 반환. `get_callers`/`get_callees` 설명에 "빈 목록은 호출자가 없다는 증명이 아니다(경계 목록 확인)"를 넣는다. |

**검증.** 각 항목에 단위 테스트 1개 이상(`AnalysisJobService` 테스트 클래스 신설). `.tracelens/mcp.json`에 다른 서버를 적어 두고 `/api/ask` 뒤에도 남아 있는지 확인.

<a id="t11"></a>
### T11. 현장 진단 가능성

**근거.** 서버 전체에 로그 문장이 2개뿐이고 파일 로그가 없다. `CONTRIBUTING.md`는 `--version`과 `doctor --json`을 받지만 500 오류에 첨부할 것이 없다. `Directory.Build.props`에 `Version`이 없어 모든 빌드가 `1.0.0`이며 이 값이 `graph.json`의 `analyzerVersion`과 스냅샷 비교 전제 조건에 들어간다.

**방법.**
- 롤링 파일 로거: 외부 패키지 없이 `ILoggerProvider` 구현(`src/TraceLens.Server/FileLoggerProvider.cs`), 위치 `<루트>/.tracelens/logs/tracelens-yyyyMMdd.log`, 7일 보관, 크기 상한. 루트를 모르는 시작 시점은 `%LOCALAPPDATA%\TraceLens\logs`.
- `ApiError.details.errorId`(8자 난수)를 500 응답과 로그 양쪽에 남긴다. 예외를 잡지 않은 곳은 미들웨어에서 한 번에 처리한다.
- `doctor --json`에 로그 경로와 최근 오류 3건 요약을 넣는다(비밀값·전체 환경 변수는 여전히 수집하지 않는다).
- `Directory.Build.props`에 `<Version>`·`<InformationalVersion>`을 두고 `pack.ps1 -Version`과 한 곳에서만 바꾸게 한다. CHANGELOG에 버전 절을 만든다.
- **Origin 검사**: 루프백이어도 브라우저의 다른 페이지가 `POST /api/analysis-jobs`(임의 경로 빌드)·`POST /api/ask`(CLI 실행)를 보낼 수 있다. `ServerHost`에 미들웨어를 추가해 GET/HEAD가 아닌 `/api/*` 요청은 `Sec-Fetch-Site`가 `same-origin`/`none`이거나 `Origin`이 서버 자신(또는 개발용 25012)일 때만 통과, 아니면 `403 FORBIDDEN_ORIGIN`. MCP는 stdio라 영향 없다.

**검증.** Server 테스트: 다른 Origin의 POST가 403, 같은 Origin은 통과. 500을 일부러 내서 errorId가 응답과 로그에 같이 있는지 확인.

<a id="t12"></a>
### T12. 클라이언트 성능·오류 처리 묶음

| 항목 | 근거 | 변경 |
|---|---|---|
| Cytoscape가 App 리렌더마다 재생성·재배치 | `App.tsx:1151` 인라인 화살표를 `GraphView.tsx:83` effect deps에 넣음. 30초 신선도 틱에도 팬·줌이 초기화된다 | `useCallback`으로 고정하고, 선택 강조는 별도 effect에서 `cy.$id().addClass('selected')`로 처리 |
| 호출 흐름 노드 상한 없음 | `windowing.ts:66` 2홉 BFS 무제한 | 창 크기 상한(예: 120)과 "+N 더 보기" 표시. README의 "한 단계 확장"과 실제 2홉 중 하나로 통일(2홉 유지 시 문서 수정) |
| 실패한 작업 뒤 전체 분석을 한 번 더 실행 | `App.tsx:338-346` — 어떤 예외든 `analyzeTarget`으로 폴백 | `startAnalysisJob`이 404/405일 때만 폴백. 그 외는 원래 오류를 표시 |
| 네트워크 오류가 브라우저 원문으로 노출 | `client.ts:39-49`는 JSON 오류만 매핑 | `fetch`를 try/catch로 감싸 `ApiError('NETWORK', 0)`으로 통일, `error.NETWORK` 번역 |
| 오류 배너를 닫을 수 없고 부팅 복원 실패가 무음 | `App.tsx:923`, `:279,287` | 닫기 버튼, 복원 실패 시 사유 표시 |
| `findEntryPointFor`가 매번 전체 엣지 배열을 훑는다 | `App.tsx:463-479` | `useMemo`로 역방향 인접 맵을 만들어 재사용 |
| 진입점 목록에 검색이 없다 | `SidePanels.tsx:17-38` | 라우트·HTTP 동사·컨트롤러 이름 필터 상자(클래스 목록에는 이미 있음) |
| 영향 → 호출 흐름 이동 시 대상이 창 밖 | `App.tsx:489-496`가 `focusStack: []` | 대표 경로의 노드 ID들을 `focusStack`으로 넣어 진입점부터 대상까지 창을 펼치고 경로 엣지에 강조 클래스 |
| 클래스 150개 초과 시 "맞춤 줌" 대신 50% | `App.tsx:306` | `fitZoom` 호출로 바꾸거나 README 수정. 구현을 권장 |
| 호출 흐름·전체 흐름도 상태가 새로고침에 사라진다 | `App.tsx:105-107` `callFlowEntry`·`focusStack`·`flowEntryId` 미저장 | `viewState`에 포함하고 존재 검사 후 복원 |
| SSE 파서에서 `JSON.parse` 예외가 스트림 전체를 죽인다 | `client.ts:276-296` | 줄 단위 try/catch, 실패 줄은 무시하고 카운트 |
| `eslint-disable` 주석이 있지만 ESLint가 없다 | `package.json`에 lint 스크립트 없음 | `eslint` + `eslint-plugin-react-hooks` 추가, `npm run lint`를 CONTRIBUTING 검증 목록에 넣고 4곳의 stale closure를 실제로 고친다 |

**검증.** `readServerSentEvents`·`normalizeGraph`·역방향 인접 맵을 순수 함수로 빼서 단위 테스트. 브라우저에서 30초 이상 두고 팬·줌이 유지되는지 확인.

<a id="t13"></a>
### T13. 분석기 정확도 버그

1. **제네릭 타입 멤버 호출의 엣지 유실.** 노드 ID는 선언 심볼(`Repo<T>.Get`)로, 엣지 대상은 생성된 심볼(`Repo<Order>.Get`)로 만들어 `SolutionScanner.cs:179`에서 대상 노드를 못 찾아 엣지를 버린다. `SymbolNames.GetNodeId`/`CallableIds.ForMethod`에서 대상 심볼을 `method.OriginalDefinition`으로, 확장 메서드는 `method.ReducedFrom ?? method`로 정규화한다. 픽스처에 제네릭 저장소와 확장 메서드 호출을 추가한다(현재 픽스처에 제네릭 타입이 하나도 없다).
2. **절대 메서드 라우트.** `EntryPointScanner.cs:121-130`이 `[Route("api/[controller]")]` + `[HttpGet("/health")]`를 `/api/x/health`로 만든다. 이미 있는 `EndpointIdentity.Combine(:43-53)`(호출처 없음)을 쓰고, `[Route]`가 기반 클래스에 있거나 여러 개인 경우(라우트 곱)도 처리한다.
3. **`<Compile Remove>`·조건부 컴파일 파일이 영구 stale.** `GraphFreshnessEvaluator.cs:127-134`가 디렉터리의 모든 `.cs`를 알려진 목록과 비교한다. `AnalysisManifestBuilder`가 분석 시점에 "소스 루트 아래에 있지만 어떤 프로젝트 Documents에도 없는 파일" 집합을 `manifest.ignoredSources`로 기록하고, 평가기는 이 집합을 `Added`에서 뺀다(그 파일이 수정돼도 무시하며, 그 사실을 `limitations`에 적는다).
4. **설정 파일 검증.** `TraceLensConfig.cs:93-157`은 잘못된 JSON·타입 불일치·필수 키 누락에서 예외로 분석 전체가 죽는다. `LoadAsync`가 `(config, issues[])`를 돌려주고, 알 수 없는 키는 경고, 타입 오류는 기본값 대체 + 경고, JSON 오류는 기본 설정 + 오류로 진행한다. issues는 `diagnostics.limitations`와 UI 배너, CLI 출력에 나온다.
5. **작은 방어.** `SnapshotComparer.cs:142-143`의 `ToDictionary`를 `GroupBy` 후 사전으로(중복 `endpointId`가 있는 가져온 파일에서 예외). `SourcePaths.cs:13`의 `GetFullPath`를 경로가 아닌 문서 이름(생성 문서)에 대해 보호. 규칙 단계(`DataRuleEngine.Build`, `CompensationAnalyzer`)에 `CancellationToken` 전달.

**검증.** 항목마다 픽스처·테스트 1개. `ContextAndImpactTests`에 중복 endpointId 비교 테스트.

<a id="t14"></a>
### T14. 접근성 기본

로드맵 A의 완료 기준("키보드로 예제 선택·진입점 선택·질문 복사가 가능")이 충족되지 않았다. `src`에 `onKeyDown`이 하나도 없고 다이어그램 박스는 `tabIndex={-1}`이다.

- 다이어그램 박스(`UmlClassBox.tsx:51`, `FlowDiagram.tsx:65`)와 점검 표 행(`DataAccessView.tsx:559-562`)을 `button` 또는 `tabIndex=0` + Enter/Space 처리.
- `PathPicker.tsx:71-73`: 열릴 때 닫기 버튼에 포커스, Escape로 닫기, 닫힐 때 이전 포커스 복원.
- `ViewToolbar.tsx:57`의 `role="tablist"`에 `aria-controls`/`tabpanel` 짝, 보기 전환 시 `aria-live` 알림.
- `Splitter.tsx`: 화살표 키로 폭 조절, `aria-valuenow`.
- 보상 격자·범례는 이미 텍스트 라벨이 있으므로 유지.

**검증.** 마우스 없이 첫 화면 → 예제 분석 → 진입점 선택 → 문맥 복사까지 진행. 클라이언트 테스트에 `PathPicker` Escape 동작(`@testing-library/react` 도입).

<a id="t15"></a>
### T15. 테스트 공백 메우기

현재 84 + 41개. 아래는 테스트가 전혀 없는 곳이다.

| 영역 | 추가할 테스트 |
|---|---|
| `AnalysisJobService`/`AnalysisJobEndpoints` | 시작·진행 이벤트·취소 후 이전 그래프 유지·같은 대상 중복 거절·목록 상한 |
| `SnapshotEndpoints` | 저장·목록·삭제·비교·`OFFLINE_SNAPSHOT` 거절 |
| `SourceAccessPolicy` | `..`, 절대 경로, UNC, 대소문자, 심볼릭 링크/정션 탈출, 오프라인 스냅샷 거부 |
| `GraphFreshnessEvaluator` | Added/Deleted/Modified/ConfigChanged/unknown 각 경로 |
| `TraceLensConfig.LoadAsync` | 잘못된 JSON, 타입 오류, 알 수 없는 키(T13-4 이후) |
| `DiResolver` | keyed, 조건부, 복수 등록, 팩토리 람다, open generic(현재 픽스처에는 `AddScoped<I,Impl>` 하나뿐) |
| `EnvironmentDiagnostics`, `ShareReportBuilder`, `SnapshotStore` | 정상·경계 사례 |
| 클라이언트 | `readServerSentEvents`, `normalizeGraph`, `api/client.ts` 오류 매핑, `viewState` 저장/복원, `noHardcodedKorean` |

**완료 기준.** 위 표의 각 행에 테스트 클래스/파일이 생기고 CONTRIBUTING의 검증 절에 반영.

<a id="t16"></a>
### T16. 문서 정합성

코드로 고칠 것은 해당 태스크에서 고치고, 여기서는 문서만 맞춘다. 원칙은 "문구를 낮추기보다 구현"이므로, 아래 중 구현 태스크가 있는 것은 그 태스크 완료 뒤 문서가 자동으로 맞는지 확인한다.

- README/README_kr: 파일별 제외·공유 보고서 UI·원본 루트 연결(T7 뒤 확인), 150개 초과 시 "맞춤 줌"(T12), "한 단계 확장" vs 2홉(T12), CLI 표에 `--no-browser` 추가, 해석 종류 표에 `External`(외부 코드, 분모 제외) 추가, 의도 4종 → 실제 5종(내부 `Share` 포함 여부 결정), "42 rules" 옆에 "기본 실행 26개(꺼짐 12, 샤드 전용 4)" 명시, `.csproj` 출력 위치 설명(T5).
- `docs/data-contract.md`: 오류 코드에 `SNAPSHOT_SAVE_FAILED`(이미 코드에 있음)와 T10·T11에서 추가하는 코드, `callSites[].viaLambda`, `transactions[].kind`, `manifest.ignoredSources`, `db.queryBuilders`·`db.transactionWrappers`·`db.cacheWrappers`. `GraphEndpoints.cs:12-24`의 옛 `{ error }` 응답에도 `code`를 넣는다(문서 각주 대신 코드 수정, 1줄짜리).
- 매뉴얼(한·영): 설정 표, 미들웨어 보기의 "엔드포인트 실행" 단계, DB·캐시 보기의 빌더 지원 범위.
- CHANGELOG: T1~T5의 "바뀐 동작"을 정직하게 적는다(예: "람다 본문 호출을 추정 근거로 포함한다. 이전 그래프와 엣지 수가 달라진다").
- 규칙 문구의 영어 제공 방식을 결정해 T8 범위 밖 항목을 닫는다.

<a id="t17"></a>
### T17. 분석 범위 확장

사용자 실패 사례가 있을 때 하나씩 넣는다. 각 항목은 "확정 못 하면 미확정 사유를 남긴다"를 지킨다.

1. **DI 등록 패턴.** `DiResolver.cs:166-169`는 제네릭 인자 2개짜리만 본다. 추가: `AddScoped<TImpl>()`(자기 등록), `AddScoped<I>(sp => new Impl(...))`/`sp.GetRequiredService<Impl>()`(람다 본문이 단일 `new`/`GetRequiredService`면 확정, 아니면 사유 "팩토리 본문을 해석하지 못함"), `AddSingleton<I>(instance)`(변수 타입으로 확정), `AddScoped(typeof(IRepo<>), typeof(Repo<>))`(OriginalDefinition으로 매핑), `AddHostedService<T>`(엣지는 없고 분류만). `DiResolver.cs:29,59,148-152`의 파일 힌트 문자열에 `AddHttpClient`, `AddHostedService`, `typeof(`를 추가.
2. **virtual/override 디스패치.** `InvocationResolver.cs:97`은 인터페이스만 DI로 푼다. virtual/abstract 대상은 `SymbolFinder.FindOverridesAsync`로 솔루션 안 재정의를 찾아 하나면 `SingleImplementation`(추정) 엣지를 추가, 여럿이면 `Ambiguous` 경계로 남긴다. 기반 선언으로 가는 `Direct` 엣지는 유지한다.
3. **다중 호스트.** `StartupFlowScanner.cs:34-39`, `MiddlewareScanner.cs:54-59`가 첫 `Program.cs`만 본다. 실행 가능한 프로젝트마다 `Program.cs` 또는 `Startup.Configure(IApplicationBuilder)`를 찾아 `startupFlows[]`/`middlewarePipelines[]`(projectKey 포함)로 확장한다. 기존 `startupFlow`/`middleware`는 첫 항목을 그대로 두어 호환한다(schema v8 후보 — data-contract 절차를 따른다).
4. **최상위 문·기타 본문.** `SolutionScanner.cs:59,253`이 타입 선언 멤버만 순회한다. `Program.<Main>$`(최상위 문), 필드·프로퍼티 초기화식, 인덱서, 연산자, 이벤트 접근자를 callable로 추가한다.
5. **컨트롤러 라우팅 나머지.** `[Route]` 다중·기반 클래스(T13-2에서 처리), `[AcceptVerbs]`, `[NonAction]` 제외, `[area]` 토큰, `MapFallback`·`MapHub`·`MapGrpcService`는 진입점이 아니라 "기타 등록"으로 표시, 버전 토큰 `v{version:apiVersion}`은 제약을 지우되 라우트가 합쳐지지 않게 원문 세그먼트를 보존.

<a id="t18"></a>
### T18. 큰 솔루션 성능

측정 없이 손대지 않는다. 순서:

1. **측정 도구.** CLI `analyze --profile`이 단계별(load·entrypoints·scan·rules·save) 시간과 최대 메모리를 출력한다(SSE에 이미 단계가 있으니 같은 값을 쓴다). 이 저장소·`samples/OrderApi`·실제 프로젝트에서 수치를 working_log에 남긴다. 현재 실제 프로젝트(126 파일)는 11.1초다.
2. **분석기.** `DiResolver`, `EntryPointScanner`, `MinimalApiEntryPointScanner`, `SolutionScanner`, `DataAccessScanner`가 각각 전체 문서를 다시 순회하고 `GetTextAsync`를 두 번 부른다. 문서 한 번 순회에서 구문 트리·시맨틱 모델을 만들어 모든 수집기에 넘기는 파이프라인으로 바꾸고, 프로젝트 안 문서는 `Parallel.ForEachAsync`로 처리한다(수집기는 스레드별 결과를 모아 마지막에 합친다). `MinimalApiEntryPointScanner.cs:98-102,154-157`의 등록자 × 등록 O(n²)를 색인으로 바꾼다.
3. **클라이언트.** `ClassDiagram.tsx:89-107`이 보이는 모든 박스를 DOM으로 그린다. `DiagramCanvas`의 스크롤 사각형으로 뷰포트 밖 박스를 그리지 않는다. `treeLayout`이 클래스 564개에서 몇 ms 걸리는지 먼저 잰다(`treeLayout.test.ts`에 성능 가드 대신 측정 스크립트).
4. 측정 전후 수치를 CHANGELOG에 "측정 조건과 함께" 적는다. "n배 빨라짐" 같은 문구는 조건 없이는 쓰지 않는다.

<a id="t19"></a>
### T19. 다중 RID 패키지 빌드

`pack.ps1:39,90`이 `win-x64`만 만든다. `-Runtimes @('win-x64','linux-x64','osx-arm64')` 매개변수를 추가해 ZIP을 각각 만들고 manifest에 체크섬을 남긴다. **빌드만 한다.** README의 지원 표는 사람이 해당 OS에서 doctor·analyze·serve·소스 보기를 확인하기 전까지 바꾸지 않는다. `-SkipClient`일 때 `wwwroot`의 `index.html`만 확인하는 것(`:80-83`)을 빌드 시각 비교로 강화한다.

---

## 4. 이번에 하지 않는 것

- **사람이 해야 하는 일.** 소규모 사용자 관찰(로드맵 §16.1), 공개 게시·홍보(§16.2), macOS·Linux 실제 검증. 코드로 대신하지 않는다.
- **설계 제약상 넣지 않는 것.** 파일 저장 감시 자동 재분석, 외부 바인딩, 이벤트·메시지 구독을 호출 엣지로, 파일 트리 탐색기, 자체 편집기·자동 커밋.
- **보류(실패 사례를 받은 뒤 설계).** MediatR 같은 동적 디스패치 해석(설정 기반 "요청 타입 → 핸들러" 규칙 후보), EF Core `DbSet<T>`의 테이블 매핑(엔티티 규약·`[Table]`·Fluent API를 읽어야 한다), 라이트 테마.

## 5. 검증 명령

```powershell
dotnet build TraceLens.sln            # 경고 0 유지 (TreatWarningsAsErrors)
dotnet test  TraceLens.sln            # 현재 84개
cd client; npm test; npm run build    # 현재 41개
cd ..; .\pack.ps1                     # 배포물 + manifest

# 실제 프로젝트 기준선 재측정 (T1~T4 전후 비교용)
dotnet run --project src/TraceLens.Cli -- analyze C:\WorkingDev\com2us-taskbar-hero\GameServer\GameServer.csproj --json
# graph.json의 diagnostics.calls, dataAccess.findings(ruleId별), middleware.findings, 고립 노드 수를 기록한다.
```

기록할 수치는 항상 분모와 함께 적는다(예: "고립 노드 118/732 → n/732", "CACHE-003 36 → n, 확인한 5건 중 맞은 것 k건").
