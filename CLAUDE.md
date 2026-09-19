# TraceLens
이 문서는 TraceLens 저장소에서 작업하는 AI 코딩 에이전트(Claude Code 포함)를 위한 지침이다. 사람이 보는 개요는 README.md를 참고한다.

## 프로젝트 한 줄 설명
ASP.NET Core Web API 코드베이스의 호출 흐름을 진입점 기준 그래프로 시각화하고, 그래프에서 선택한 코드 위치를 AI 코딩 에이전트에게 근거와 함께 전달할 수 있게 해주는 로컬 개발 도구다. 데모용이 아니라 실전에서 계속 쓰는 것을 목표로 개발한다.

## 반드시 지켜야 할 설계 제약
아래 여섯 가지는 이미 논의를 거쳐 확정된 방향이다. 별도 지시가 없는 한 이 범위를 벗어나는 기능을 임의로 추가하지 않는다.

1. **그래프 규모.** 호출 관계를 따라가는 보기(호출 흐름)는 항상 하나의 포커스(HTTP 진입점) 기준으로 단계식으로 보여준다(포커스 기반 윈도잉, `client/src/graph/windowing.ts`). 탐색 축은 진입점 목록과 네임스페이스/클래스 목록 두 가지뿐이고 파일 트리 탐색기는 만들지 않는다.

   **예외 — 클래스 다이어그램과 전체 흐름도는 솔루션 전체를 한 캔버스에 그린다.** "이 코드베이스가 어떻게 생겼는지"를 보는 것이 목적이라 일부만 그리면 쓸모가 없기 때문이다. 대신 노드 수 대신 **보는 밀도를 조절하는 손잡이**를 반드시 함께 둔다.

   - 멤버(필드·메서드) 접기 토글 — 끄면 박스 높이가 30~44px로 줄어 전체 면적이 1/3 이하가 된다
   - 관계 4종(상속·구현·연관·의존) 개별 토글 — 의존(호출)은 기본으로 꺼 둔다
   - 네임스페이스 체크박스로 패키지 단위 표시/숨김, "선택 1홉만" 토글
   - 줌 50/75/100/맞춤
   - 클래스 150개를 넘으면 멤버 접힘 + 의존 끔 + 맞춤 줌으로 시작하고 범위를 좁히라는 안내를 띄운다

   이 손잡이 없이 전체를 그리는 화면은 추가하지 않는다.

   **예외 — "변경 영향"은 목록으로만 보여준다.** 영향받는 진입점이 여러 개여도 한 그래프에 펼치지 않는다. 목록에서 하나를 고르면 기존 호출 흐름 보기로 이동해 그 경로를 강조한다.
2. **엣지 범위.** 그래프의 엣지는 메서드 호출 관계로만 구성한다. 생성자(`new X()`)도 메서드 호출로 보고 포함하며(`includeObjectCreation`으로 끌 수 있다), 프로퍼티 읽기/쓰기 같은 접근은 엣지로 만들지 않는다. DI 의존 관계나 이벤트/메시지 발행-구독 흐름을 엣지로 표현하는 기능을 넣지 않는다. DI 등록 정보(`AddScoped`/`AddSingleton`/`TryAdd*` 등)는 인터페이스 호출을 실제 구현체 메서드로 정확히 연결하는 내부 해석 로직에만 사용하고, 등록을 찾지 못하면 솔루션 안 구현체가 하나뿐일 때만 그 구현체로 잇는다(이때는 추정으로 표시한다).

   호출 근거(`callSites`)는 엣지를 늘리는 것이 아니라 **엣지가 왜 생겼는지를 남기는 것**이다. 확정하지 못한 호출은 엣지를 만들지 않고 "경계" 목록에만 남긴다.

   **예외 — "DB · 캐시" 보기와 "미들웨어" 보기는 호출 그래프가 아니라 별도 분석이다.** DB·캐시 보기는 SQL 리터럴과 캐시 호출에서 뽑은 **저장소 접근**과, 트랜잭션 없이 쓰는 경로의 **보상 처리(수동 롤백) 판정**을 보여준다. 미들웨어 보기는 `app.UseXxx()` 순서와 각 미들웨어의 `InvokeAsync` 본문(요청/응답 구간·단락 지점)을 보여준다. 둘 다 호출 흐름 엣지를 늘리지 않는다. 규칙 판정은 언제나 **근거와 확실도(확실/추정)를 함께** 보여주고, 검사하지 않는 규칙도 카탈로그에 "꺼짐"으로 남겨 무엇을 보지 않는지 분명히 한다.

   **예외 — "시작 흐름" 보기는 Program.cs의 DI 등록·미들웨어 파이프라인을 그대로 보여준다.** 이 보기는 위에서 말하는 "그래프"(호출 흐름 엣지)가 아니라, Program.cs 하나를 소스 순서대로 읽어 만드는 별도의 타임라인이다.
3. **최신 상태 유지.** 파일 저장을 감지해서 자동으로 재분석하는 기능을 넣지 않는다. 재분석은 항상 사용자가 명시적으로 실행한다(버튼 클릭 또는 API 호출). 대신 마지막 분석 시점과 현재 코드가 같은지 항상 확인할 수 있는 신선도 표시를 유지한다. 신선도는 `fresh`/`stale`/`unknown` 세 가지이고, **신선도 입력(manifest)이 없는 옛 분석을 `fresh`라고 단정하지 않는다.**
4. **로컬 전용.** 그래프 서버는 루프백(`127.0.0.1`)에만 바인딩하고 MCP는 로컬 stdio를 쓴다. 원격에서 접근 가능하게 만드는 옵션, 인증 없이 외부 바인딩을 허용하는 설정, 서버를 외부에 공개하는 공유 방식을 추가하지 않는다.
5. **판정에 정직할 것.** 확정하지 못한 것은 **추정**으로 쓰고 근거를 남긴다. 분모가 정의되지 않은 정확도 수치를 쓰지 않는다(해석률은 분모를 함께 적는다). "호출자가 없다 → 삭제해도 안전", "차이 없음 → 안전·테스트 통과", 측정 전의 "토큰 n% 절약·속도 n배" 같은 문구를 쓰지 않는다. 검사하지 않은 규칙은 목록에서 지우지 않고 "꺼짐"으로 남긴다.
6. **개발 순서.** 단계적으로 개발하되, 각 단계가 끝나면 실제로 켜서 쓸 수 있는 상태여야 한다. "임시로 써보는 데모"가 아니라 계속 쓸 도구를 목표로 코드 품질(에러 처리, 최소한의 테스트)을 유지한다.

## 저장소 구조

```
tracelens/
├── CLAUDE.md                     # 이 문서(정본). AGENTS.md는 여기를 가리키기만 한다
├── CONTRIBUTING.md               # 검증 명령, 픽스처, 판정 문구 약속
├── CHANGELOG.md                  # 수동 갱신 릴리스 노트
├── run.ps1                       # 빌드 + 실행을 한 번에 하는 스크립트
├── pack.ps1                      # 배포물(.NET tool · portable ZIP) 생성 + 체크섬·검증 기록
├── README.md / README_kr.md
├── TraceLens.sln
├── docs/
│   ├── data-contract.md          # graph.json 스키마(v7), API·MCP 계약, 이행 규칙
│   ├── tracelens.config.schema.json  # 설정 JSON Schema. 코드와 어긋나면 테스트가 실패한다
│   ├── next-tasks_kr.md          # 1차 점검 결과와 태스크(T1~T19, 완료)
│   ├── next-tasks-2_kr.md        # 2차 점검 결과와 태스크(T20~T42, 완료)
│   ├── next-tasks-3_kr.md        # 3차 기능·UX 점검과 태스크(T43~T72, 완료)
│   ├── manual.html / manual_kr.html
│   └── mcp-setup.md / mcp-setup_kr.md
├── samples/                      # 배포물에 함께 넣는 예제(분석 대상). 도구 코드가 아니다
│   ├── samples.json              # 예제 목록 · 예상 결과 · 학습 순서
│   └── OrderApi/                 # 컨트롤러 + Minimal API + 보상 처리 누락 경로
├── src/
│   ├── TraceLens.Analyzer/       # Roslyn 기반 분석 라이브러리 (UI/서버에 대한 의존성 없음)
│   │   ├── SolutionLoader.cs             # 워크스페이스 로드 + 프로젝트별 진단 수집
│   │   ├── EntryPointScanner.cs          # 컨트롤러 특성 + Minimal API 합치기(기반 클래스 액션·[ApiVersion])
│   │   ├── EntryPointCandidateScanner.cs # HTTP 밖 실행 단위를 세기만 한다(진입점·엣지를 만들지 않는다)
│   │   ├── HostDocuments.cs              # Program.cs + Startup.cs(클래식 구조)를 함께 찾는다
│   │   ├── UnreadFrameworks.cs           # 인식했지만 해석하지 않은 프레임워크를 한계 문장으로
│   │   ├── MinimalApiEntryPointScanner.cs# Map*/MapGroup/MapMethods, 람다·로컬 함수 핸들러
│   │   ├── EndpointIdentity.cs           # 라우트 정규화와 endpointId 생성
│   │   ├── AnalysisCallable.cs           # 메서드·생성자·람다·로컬 함수의 공통 표현 + 본문 경계 순회
│   │   ├── CallableIds.cs                # 람다·로컬 함수의 구조 기반 노드 ID
│   │   ├── SolutionScanner.cs            # 솔루션 전체 타입/멤버 스캔 (analysisScope=solution)
│   │   ├── CallGraphWalker.cs            # 진입점 BFS (analysisScope=entryPoints)
│   │   ├── InvocationResolver.cs         # 호출식 → 해석 결과(근거·확실도 포함)
│   │   ├── CallSiteCollector.cs          # 호출 근거·통계 수집과 상한
│   │   ├── DiResolver.cs                 # DI 등록 해석(keyed·조건부·복수 등록은 확정하지 않음)
│   │   ├── AnalysisManifestBuilder.cs    # 분석 입력 목록과 소스 루트(신선도의 새 파일 검사)
│   │   ├── MethodFingerprints.cs         # 시그니처·본문 지문(스냅샷 비교용)
│   │   ├── ClassMemberCollector.cs       # 타입 심볼 → GraphClassMember
│   │   ├── DataAccessScanner.cs          # DB·캐시 접근과 구조 정보 수집
│   │   ├── CompensationAnalyzer.cs       # 접근 요약 전파 + 쓰기 경로·보상 판정
│   │   ├── DataRuleEngine.cs             # DB·캐시 규칙과 규칙 카탈로그
│   │   ├── MiddlewareScanner.cs          # 미들웨어 파이프라인·단락·MW 규칙
│   │   ├── StartupFlowScanner.cs         # Program.cs의 DI 등록·파이프라인 타임라인
│   │   ├── NodeClassifier.cs · SymbolNames.cs · SnapshotHasher.cs · SourcePaths.cs
│   │   ├── TraceLensConfig.cs · DataAccessOptions.cs
│   │   └── Models/
│   │       ├── GraphDocument.cs          # schema v7 본체
│   │       ├── GraphNode.cs              # callable·stableKey·지문 포함
│   │       ├── GraphEdge.cs · GraphClass.cs · GraphClassMember.cs
│   │       ├── EntryPoint.cs             # endpointId·출처·핸들러 종류·확실도
│   │       ├── CallSite.cs               # 호출 근거(해석 종류·확실도)
│   │       ├── AnalysisManifest.cs       # 분석 입력 목록
│   │       ├── AnalysisDiagnostics.cs    # 프로젝트·호출 해석 통계·한계
│   │       ├── DataAccess.cs · MiddlewarePipeline.cs · Findings.cs · StartupFlow.cs
│   ├── TraceLens.Context/        # Server와 Mcp가 함께 쓰는 순수 조회 라이브러리
│   │   ├── GraphIndex.cs                 # 노드·호출자·피호출자·엔드포인트·근거 색인
│   │   ├── ContextBuilder.cs · ContextModels.cs · ContextMarkdown.cs
│   │   ├── ImpactAnalyzer.cs             # 역방향 BFS로 영향 후보 진입점
│   │   ├── SnapshotComparer.cs           # 스냅샷 비교(전제 조건 확인 포함)
│   │   ├── SnapshotStore.cs              # .tracelens/history 아래 기준 보관
│   │   ├── GraphFreshnessEvaluator.cs    # 해시 + 파일 집합 + 설정 입력 비교
│   │   ├── GraphJsonStore.cs             # 읽기 검증·원자적 쓰기·미래 schema 거절
│   │   ├── SourceAccessPolicy.cs · SourceReader.cs
│   │   ├── ShareReportBuilder.cs         # 공유용 Markdown 보고서
│   │   └── EnvironmentDiagnostics.cs     # doctor가 쓰는 환경 점검
│   ├── TraceLens.Server/         # ASP.NET Core Minimal API + React 빌드 결과물 정적 호스팅
│   │   ├── ServerHost.cs                 # 호스트 구성(CLI의 serve가 재사용)
│   │   ├── Program.cs · GraphStore.cs · GraphFreshnessService.cs
│   │   ├── AnalysisJobService.cs         # 진행 상태(SSE)·취소
│   │   ├── AgentRunner.cs                # 외부 CLI 의존을 분리한 IAgentRunner
│   │   ├── McpConfigWriter.cs            # mcp.json 생성 + 호스트별 설정·원라이너·에이전트 안내문
│   │   ├── ApiResults.cs                 # { code, message, details } 오류 형태
│   │   ├── Endpoints/
│   │   │   ├── AnalyzeEndpoints.cs · AnalysisJobEndpoints.cs
│   │   │   ├── BrowseEndpoints.cs · GraphEndpoints.cs · SourceEndpoints.cs
│   │   │   ├── FreshnessEndpoints.cs · DiagnosticsEndpoints.cs
│   │   │   ├── ContextEndpoints.cs · ImpactEndpoints.cs · SnapshotEndpoints.cs
│   │   │   ├── ManualEndpoints.cs        # /manual/ko · /manual/en 정적 서빙
│   │   │   └── AskEndpoints.cs           # 허용 도구 목록은 [McpServerTool]에서 리플렉션으로 만든다
│   │   └── wwwroot/                      # client 빌드 결과물이 빌드 시 여기로 복사됨
│   ├── TraceLens.Mcp/            # 로컬 전용 stdio MCP 서버
│   │   ├── McpHost.cs · Program.cs · GraphFileStore.cs · TraceLensTools.cs
│   └── TraceLens.Cli/            # tracelens doctor|analyze|serve|mcp|demo|init|config (.NET tool)
│       ├── CommandRouter.cs · DemoSamples.cs
├── tests/
│   ├── TraceLens.Analyzer.Tests/
│   │   ├── Fixtures/                     # 컨트롤러·DI 해석용 미니 솔루션
│   │   ├── FixturesData/                 # DB·캐시·미들웨어용 미니 솔루션
│   │   ├── FixturesMinimalApi/           # Minimal API 등록 패턴 회귀용
│   │   ├── FixturesClassic/              # Program.cs + Startup.cs, 기반 클래스 액션·[ApiVersion]
│   │   └── FixturesExternal/             # 외부 호출 집계·HTTP 밖 실행 단위 회귀용
│   ├── TraceLens.Server.Tests/
│   └── TraceLens.Mcp.Tests/
└── client/                               # React + TypeScript (Vite)
    └── src/
        ├── i18n/                         # ko.ts(원본) · en.ts(키로 타입 제한) · I18nProvider.tsx · labels.ts
        ├── content/recipes.ts            # 앱과 매뉴얼이 공유하는 실무 절차
        ├── components/
        │   ├── WelcomePanel.tsx          # 분석 전 첫 화면(예제·내 프로젝트·지원 범위·레시피)
        │   ├── GuidedWalkthrough.tsx     # 첫 성공 경로 안내(workspace 밖, 분석 전에도 보인다)
        │   ├── HelpDialog.tsx            # 용어집·단축키·실무 절차 + 용어 옆 `?` 링크
        │   ├── DiagnosticsCard.tsx       # 환경 점검(첫 화면·헤더 모달 공용)
        │   ├── DiagnosticsPanel.tsx      # 분석 진단(무엇을 보고 무엇을 못 봤나)
        │   ├── McpSetupDialog.tsx        # MCP 등록(호스트 탭·원라이너·에이전트 안내문)
        │   ├── QuickFind.tsx             # Ctrl+K 통합 찾기
        │   ├── SourceLink.tsx            # 화면의 모든 `파일:줄`
        │   ├── ContextPreview.tsx        # 전달할 문맥 미리보기
        │   ├── ImpactPanel.tsx           # 변경 영향(후보 목록)
        │   ├── SnapshotDiffPanel.tsx     # 기준 저장 → 재분석 → 비교
        │   ├── AskPanel.tsx              # AI에게 질문(문맥 복사 / Claude Code 실행 / 에디터)
        │   ├── ViewToolbar.tsx · SidePanels.tsx · DetailPanel.tsx
        │   ├── DataAccessView.tsx · DataSidePanels.tsx · DataDetailPanel.tsx
        │   ├── MiddlewareView.tsx · StartupFlowView.tsx
        │   ├── GraphView.tsx · DiagramCanvas.tsx · UmlClassBox.tsx
        │   ├── ClassDiagram.tsx · FlowDiagram.tsx
        │   ├── Splitter.tsx · CodePanel.tsx · PathPicker.tsx · FreshnessBadge.tsx
        ├── graph/                        # 순수 배치·파생 계산(React 비의존). search.ts 포함
        ├── state/viewState.ts · state/urlState.ts
        ├── api/client.ts
        └── types/graph.ts · types/context.ts
```

## 빌드 및 실행

```
# 한 번에 빌드하고 실행 (클라이언트 빌드 결과를 Server의 wwwroot에 넣고 서버 실행)
.\run.ps1

# 배포물 만들기(portable ZIP + .NET tool). 게시는 하지 않는다
.\pack.ps1

# CLI
dotnet run --project src/TraceLens.Cli -- doctor --target <대상>
dotnet run --project src/TraceLens.Cli -- analyze <대상>
dotnet run --project src/TraceLens.Cli -- serve --target <대상>

# 백엔드
dotnet build
dotnet run --project src/TraceLens.Server

# 프론트엔드 (개발 중에는 별도 포트에서 Vite dev server + 프록시로 서버 API 호출)
cd client
npm install
npm run dev
npm test
npm run build      # Server의 wwwroot로 산출물 복사
```

## 코딩 컨벤션

**C# (Analyzer, Context, Server, Mcp, Cli)**
- `<Nullable>enable</Nullable>`, file-scoped namespace, `ImplicitUsings` 사용.
- Server는 컨트롤러 클래스 없이 Minimal API 엔드포인트 그룹(`MapGroup`)으로 작성한다. 엔드포인트 정의와 처리 로직을 분리해서 `Endpoints/` 아래 기능별 파일로 나눈다.
- Analyzer는 Context/Server/Mcp/Cli에 대한 참조를 가지지 않는다. 순수 라이브러리로 유지해서 CLI나 다른 호스트에서도 재사용 가능하게 한다.
- Context는 Analyzer 모델만 참조하고 Server/Mcp를 참조하지 않는다. **Server와 MCP가 같은 결과를 돌려주도록 조회 로직은 여기에 둔다.**
- 비동기 I/O(파일, 프로세스 실행)는 항상 `async`/`await`로 작성하고 `.Result`/`.Wait()`를 쓰지 않는다.
- 외부 프로세스 실행(`claude` CLI 호출 등)은 `IProcessRunner`/`IAgentRunner`를 통해서만 수행하고 테스트에서 모킹 가능하게 한다.
- 새 API 오류는 `{ code, message, details }`로 통일한다. 과거 API는 `error` 필드를 유지하면서 같은 code를 함께 담는다.

**TypeScript/React (client)**
- `strict: true`. 함수형 컴포넌트 + 훅만 사용하고 클래스 컴포넌트를 쓰지 않는다.
- 그래프/코드 뷰 상태는 최상위 `App.tsx`에서 관리하고 props로 내려준다. 별도 상태관리 라이브러리를 도입하지 않는다.
- 배치 계산(`graph/` 아래)은 React에 의존하지 않는 **순수 함수**로 두고 단위 테스트를 붙인다. 컴포넌트는 그 결과를 그리기만 한다.
- 클래스 다이어그램과 전체 흐름도는 Cytoscape가 아니라 DOM + SVG로 직접 그린다. 호출 흐름 보기는 Cytoscape 그대로 둔다.
- 서버 API 호출은 반드시 `api/client.ts`를 통해서만 하고, 컴포넌트에서 `fetch`를 직접 호출하지 않는다.
- 사용자에게 보이는 문자열은 `i18n/ko.ts`에 키로 넣는다(영어는 그 키로 타입이 제한된다). 심볼·라우트·코드·파일 경로는 번역하지 않는다.

## 데이터 계약

현재 스키마는 **v7**이다. 정본은 Analyzer의 `Models/`, Server의 `Endpoints/`, Context의 DTO, client의 `types/`다.
버전 이력·이행 규칙·최소 예제·API 오류 코드는 [`docs/data-contract.md`](docs/data-contract.md)에 있다.
스키마를 바꿀 때는 그 문서의 "스키마를 바꿀 때" 절차를 따른다(네 곳의 타입을 함께 갱신하고 문서를 갱신한다).

## 진행 상태 확인

단계별 구현 체크리스트 문서(task.md·task2.md·task3.md)는 6단계 완료 후 삭제했다.
[`docs/adoption-and-vibe-coding-roadmap_kr.md`](docs/adoption-and-vibe-coding-roadmap_kr.md)의 A~I는 구현을 마쳤고,
그 결과는 [`CHANGELOG.md`](CHANGELOG.md)에 정리돼 있다. 현재 저장소 상태와 남은 작업은 `git log`와 코드를 직접 확인해서 판단한다.
2026-09-17에 README·CLAUDE.md의 목표와 실제 코드·실제 프로젝트 분석 결과를 대조한 점검 문서
[`docs/next-tasks_kr.md`](docs/next-tasks_kr.md)가 있다(T1~T19, 모두 완료).
같은 날 "신입 개발자가 혼자 쓸 수 있는가"를 기준으로 다시 점검한 2차 문서
[`docs/next-tasks-2_kr.md`](docs/next-tasks-2_kr.md)가 있다(T20~T42, 2026-09-18에 모두 완료).
두 문서의 태스크는 전부 끝났다. 2026-09-18 저녁에 "매일 쓰는 개발자가 계속 쓰겠는가"를 기준으로
실제 실행 스크린샷과 코드 읽기로 다시 점검한 3차 문서 [`docs/next-tasks-3_kr.md`](docs/next-tasks-3_kr.md)가 있다
(T43~T72, 2026-09-19에 모두 완료).
세 문서의 태스크가 모두 끝났으므로 다음 작업은 새 점검에서 나온다.
새 "다음 작업" 문서를 만들면 상단 태스크 목록을 하나 끝낼 때마다 즉시 체크한다.

로드맵에서 **아직 하지 않은 것**은 사람이 해야 하는 일이다: 소규모 사용자 관찰(§16.1), 공개 게시와 홍보(§16.2),
macOS·Linux 검증. 이것들을 코드로 대신하지 않는다.

## 분석 대상과 이 저장소를 혼동하지 않기

이 저장소는 도구 자체의 코드다. 사용자가 분석하는 대상(예: 사내 ASP.NET Core 서비스)은 별도 경로의 다른 솔루션이며, 분석 결과물(`.tracelens/graph.json`)은 이 저장소가 아니라 **분석 대상 솔루션 폴더 아래**에 생성된다. 이 저장소 안에 분석 결과 예시가 필요하면 `samples/OrderApi`나 `tests/TraceLens.Analyzer.Tests/Fixtures*` 아래의 샘플 솔루션을 사용한다.
