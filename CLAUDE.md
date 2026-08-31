# TraceLens
이 문서는 TraceLens 저장소에서 작업하는 AI 코딩 에이전트(Claude Code 포함)를 위한 지침이다. 사람이 보는 개요는 README.md, 단계별 구현 상세와 체크리스트는 task.md(1~4단계), task2.md(5단계: 클래스 다이어그램·전체 흐름도), task3.md(6단계: DB·캐시 보기와 미들웨어 보기)를 참고한다.

## 프로젝트 한 줄 설명
ASP.NET Core Web API 코드베이스의 호출 흐름을 진입점 기준 그래프로 시각화하고, 그래프에서 선택한 코드 위치를 Claude Code에게 곧바로 질의할 수 있게 해주는 로컬 개발 도구다. 데모용이 아니라 실전에서 계속 쓰는 것을 목표로 개발한다.

## 반드시 지켜야 할 설계 제약
아래 다섯 가지는 이미 논의를 거쳐 확정된 방향이다. 별도 지시가 없는 한 이 범위를 벗어나는 기능을 임의로 추가하지 않는다.

1. **그래프 규모.** 호출 관계를 따라가는 보기(호출 흐름)는 항상 하나의 포커스(HTTP 진입점) 기준으로 단계식으로 보여준다. 자세한 방식은 task.md의 "포커스 기반 윈도잉"을 따른다. 탐색 축은 진입점 목록과 네임스페이스/클래스 목록 두 가지뿐이고 파일 트리 탐색기는 만들지 않는다.

   **예외 — 클래스 다이어그램과 전체 흐름도는 솔루션 전체를 한 캔버스에 그린다.** "이 코드베이스가 어떻게 생겼는지"를 보는 것이 목적이라 일부만 그리면 쓸모가 없기 때문이다(5단계에서 확정, 상세는 task2.md). 대신 노드 수 대신 **보는 밀도를 조절하는 손잡이**를 반드시 함께 둔다.

   - 멤버(필드·메서드) 접기 토글 — 끄면 박스 높이가 30~44px로 줄어 전체 면적이 1/3 이하가 된다
   - 관계 4종(상속·구현·연관·의존) 개별 토글 — 의존(호출)은 기본으로 꺼 둔다
   - 네임스페이스 체크박스로 패키지 단위 표시/숨김, "선택 1홉만" 토글
   - 줌 50/75/100/맞춤
   - 클래스 150개를 넘으면 멤버 접힘 + 의존 끔 + 맞춤 줌으로 시작하고 범위를 좁히라는 안내를 띄운다

   이 손잡이 없이 전체를 그리는 화면은 추가하지 않는다.
2. **엣지 범위.** 그래프의 엣지는 메서드 호출 관계로만 구성한다. 생성자(`new X()`)도 메서드 호출로 보고 포함하며(`includeObjectCreation`으로 끌 수 있다), 프로퍼티 읽기/쓰기 같은 접근은 엣지로 만들지 않는다. DI 의존 관계나 이벤트/메시지 발행-구독 흐름을 엣지로 표현하는 기능을 넣지 않는다. DI 등록 정보(`AddScoped`/`AddSingleton`/`TryAdd*` 등)는 인터페이스 호출을 실제 구현체 메서드로 정확히 연결하는 내부 해석 로직에만 사용하고, 등록을 찾지 못하면 솔루션 안 구현체가 하나뿐일 때만 그 구현체로 잇는다.

   **예외 — "DB · 캐시" 보기와 "미들웨어" 보기는 호출 그래프가 아니라 별도 분석이다.** DB·캐시 보기는 SQL 리터럴과 캐시 호출에서 뽑은 **저장소 접근**과, 트랜잭션 없이 쓰는 경로의 **보상 처리(수동 롤백) 판정**을 보여준다. 미들웨어 보기는 `app.UseXxx()` 순서와 각 미들웨어의 `InvokeAsync` 본문(요청/응답 구간·단락 지점)을 보여준다. 둘 다 호출 흐름 엣지를 늘리지 않는다(6단계에서 확정, 상세는 task3.md). 규칙 판정은 언제나 **근거와 확실도(확실/추정)를 함께** 보여주고, 검사하지 않는 규칙도 카탈로그에 "꺼짐"으로 남겨 무엇을 보지 않는지 분명히 한다.

   **예외 — "시작 흐름" 보기는 Program.cs의 DI 등록·미들웨어 파이프라인을 그대로 보여준다.** 이 보기는 위에서 말하는 "그래프"(호출 흐름 엣지)가 아니라, Program.cs 하나를 소스 순서대로 읽어 만드는 별도의 타임라인이다(어떤 인터페이스가 어떤 구현체로, 어떤 수명(Scoped/Singleton/Transient)으로 등록됐는지와 `app.UseXxx()`/`MapXxx()` 파이프라인 순서). 상단 메뉴의 "전체 흐름도" 옆에 네 번째 보기로 둔다.
3. **최신 상태 유지.** 파일 저장을 감지해서 자동으로 재분석하는 기능을 넣지 않는다. 재분석은 항상 사용자가 명시적으로 실행한다(버튼 클릭 또는 API 호출). 대신 마지막 분석 시점과 현재 코드가 같은지 항상 확인할 수 있는 신선도 표시를 유지한다.
4. **로컬 전용.** 그래프 서버와 (3단계의) MCP 연동은 `localhost`에서만 동작한다. 원격에서 접근 가능하게 만드는 옵션, 인증 없이 외부 바인딩을 허용하는 설정을 추가하지 않는다.
5. **개발 순서.** 단계적으로 개발하되, 각 단계가 끝나면 실제로 켜서 쓸 수 있는 상태여야 한다. "임시로 써보는 데모"가 아니라 계속 쓸 도구를 목표로 코드 품질(에러 처리, 최소한의 테스트)을 유지한다.

## 저장소 구조

```
tracelens/
├── CLAUDE.md
├── run.ps1                       # 빌드 + 실행을 한 번에 하는 스크립트
├── README.md
├── task.md                       # 0장 데이터 계약 + 1~4단계
├── task2.md                      # 5단계(클래스 다이어그램·전체 흐름도)
├── task3.md                      # 6단계(DB·캐시 보기·보상 처리·미들웨어 보기)
├── TraceLens.sln
├── src/
│   ├── TraceLens.Analyzer/       # Roslyn 기반 분석 라이브러리 (UI/서버에 대한 의존성 없음)
│   │   ├── SolutionLoader.cs
│   │   ├── EntryPointScanner.cs
│   │   ├── SolutionScanner.cs            # 솔루션 전체 타입/멤버 스캔 (4단계, 5단계에서 인터페이스·멤버 추가)
│   │   ├── ClassMemberCollector.cs       # 타입 심볼 → GraphClassMember (5단계)
│   │   ├── InvocationResolver.cs         # 호출식 → 심볼 해석 (스캔/BFS 공용)
│   │   ├── CallGraphWalker.cs            # 진입점 BFS (analysisScope=entryPoints)
│   │   ├── DataAccessScanner.cs          # DB·캐시 접근과 구조 정보 수집 (6단계)
│   │   ├── CompensationAnalyzer.cs       # 접근 요약 전파 + 쓰기 경로·보상 판정 (6단계)
│   │   ├── DataRuleEngine.cs             # DB·캐시 규칙과 규칙 카탈로그 (6단계)
│   │   ├── MiddlewareScanner.cs          # 미들웨어 파이프라인·단락·MW 규칙 (6단계)
│   │   ├── DataAccessOptions.cs          # tracelens.config.json의 db 섹션 (6단계)
│   │   ├── DiResolver.cs
│   │   ├── NodeClassifier.cs
│   │   ├── SnapshotHasher.cs
│   │   └── Models/
│   │       ├── GraphNode.cs
│   │       ├── GraphEdge.cs
│   │       ├── GraphClass.cs
│   │       ├── GraphClassMember.cs
│   │       ├── DataAccess.cs             # 접근·트랜잭션·쓰기 경로 (6단계)
│   │       ├── MiddlewarePipeline.cs     # 미들웨어 단계 (6단계)
│   │       ├── Findings.cs               # 점검 항목·규칙 카탈로그 (6단계)
│   │       └── GraphDocument.cs
│   ├── TraceLens.Server/         # ASP.NET Core Minimal API + React 빌드 결과물 정적 호스팅
│   │   ├── Program.cs
│   │   ├── Endpoints/
│   │   │   ├── AnalyzeEndpoints.cs
│   │   │   ├── BrowseEndpoints.cs         # 분석 대상(.sln/.csproj) 선택용 로컬 디렉터리 탐색
│   │   │   ├── GraphEndpoints.cs
│   │   │   ├── SourceEndpoints.cs
│   │   │   └── AskEndpoints.cs           # 3단계에서 추가
│   │   └── wwwroot/                      # client/dist 결과물이 빌드 시 여기로 복사됨
│   └── TraceLens.Mcp/            # 3단계에서 추가되는 로컬 전용 MCP 서버
├── tests/
│   └── TraceLens.Analyzer.Tests/
│       └── Fixtures/                     # 분석 테스트용 미니 ASP.NET Core 샘플 솔루션
└── client/                               # React + TypeScript (Vite)
    └── src/
        ├── components/
        │   ├── ViewToolbar.tsx           # 보기 전환(호출 흐름 / 클래스 다이어그램 / 전체 흐름도 / 시작 흐름 / DB·캐시 / 미들웨어)
        │   ├── DataAccessView.tsx        # DB·캐시 보기(실행 경로·접근 지도·트랜잭션·보상 처리·점검) (6단계)
        │   ├── MiddlewareView.tsx        # 미들웨어 보기(파이프라인·단락 지점·점검) (6단계)
        │   ├── DataSidePanels.tsx        # 두 보기의 좌측 패널 (6단계)
        │   ├── DataDetailPanel.tsx       # 점검 항목·쓰기 경로·규칙 상세 (6단계)
        │   ├── GraphView.tsx             # Cytoscape.js 래퍼 (호출 흐름 전용)
        │   ├── DiagramCanvas.tsx         # 배치 결과(박스+선)를 그리는 공용 캔버스
        │   ├── UmlClassBox.tsx           # UML 박스(이름/필드/메서드 3단)
        │   ├── ClassDiagram.tsx          # 클래스 다이어그램 조립
        │   ├── FlowDiagram.tsx           # 전체 흐름도 조립
        │   ├── SidePanels.tsx            # 보기별 좌측 패널(진입점 / 네임스페이스 / 전체 지도)
        │   ├── DetailPanel.tsx           # 클래스·메서드·네임스페이스 상세
        │   ├── Splitter.tsx              # 3분할 폭 조절
        │   ├── CodePanel.tsx             # Monaco Editor 래퍼(SourceView)
        │   ├── AskPanel.tsx
        │   ├── PathPicker.tsx            # 분석 대상 선택 대화상자
        │   └── FreshnessBadge.tsx
        ├── graph/
        │   ├── dataView.ts               # 보상 격자·접근 매트릭스·진입점 저장소 경로 (순수, 6단계)
        │   ├── windowing.ts              # 호출 흐름의 포커스 기반 윈도잉
        │   ├── relations.ts              # 상속·구현·연관·의존 관계 파생 (순수)
        │   ├── treeLayout.ts             # 계층(트리) 배치 엔진 (순수)
        │   ├── packageLayout.ts          # 네임스페이스 격자 배치 (순수)
        │   ├── classDiagram.ts           # 박스 크기·필터·보기 조립
        │   ├── flowMap.ts                # 진입점 지도 / 진입점 흐름
        │   └── geometry.ts               # 선 라우팅에 쓰는 기하 함수
        ├── state/viewState.ts            # localStorage에 저장하는 보기·필터 상태
        ├── api/client.ts
        └── types/graph.ts
```

## 빌드 및 실행

```
# 한 번에 빌드하고 실행 (클라이언트 빌드 결과를 Server의 wwwroot에 넣고 서버 실행)
.\run.ps1

# 백엔드
dotnet build
dotnet run --project src/TraceLens.Server

# 프론트엔드 (개발 중에는 별도 포트에서 Vite dev server + 프록시로 서버 API 호출)
cd client
npm install
npm run dev

# 프론트엔드 프로덕션 빌드 (Server의 wwwroot로 산출물 복사)
npm run build
```

## 코딩 컨벤션

**C# (Analyzer, Server, Mcp)**
- `<Nullable>enable</Nullable>`, file-scoped namespace, `ImplicitUsings` 사용.
- Server는 컨트롤러 클래스 없이 Minimal API 엔드포인트 그룹(`MapGroup`)으로 작성한다. 엔드포인트 정의와 처리 로직을 분리해서 `Endpoints/` 아래 기능별 파일로 나눈다.
- Analyzer는 Server/Mcp에 대한 프로젝트 참조를 가지지 않는다. 순수 라이브러리로 유지해서 CLI나 다른 호스트에서도 재사용 가능하게 한다.
- 비동기 I/O(파일, 프로세스 실행)는 항상 `async`/`await`로 작성하고 `.Result`/`.Wait()`를 쓰지 않는다.
- 외부 프로세스 실행(3단계의 `claude` CLI 호출 등)은 `System.Diagnostics.Process`를 직접 감싸는 얇은 래퍼 클래스를 통해서만 수행하고, 테스트에서 모킹 가능하게 인터페이스로 추상화한다.

**TypeScript/React (client)**
- `strict: true`. 함수형 컴포넌트 + 훅만 사용하고 클래스 컴포넌트를 쓰지 않는다.
- 그래프/코드 뷰 상태는 최상위 `App.tsx`에서 관리하고 props로 내려준다. 초기 단계에서는 별도 상태관리 라이브러리(Redux 등)를 도입하지 않는다.
- 배치 계산(`graph/` 아래)은 React에 의존하지 않는 **순수 함수**로 두고 단위 테스트를 붙인다. 컴포넌트는 그 결과를 그리기만 한다.
- 클래스 다이어그램과 전체 흐름도는 Cytoscape가 아니라 DOM + SVG로 직접 그린다(멤버 줄마다 클릭·강조가 필요해 캔버스 노드로는 표현이 어렵다). 호출 흐름 보기는 Cytoscape 그대로 둔다.
- 서버 API 호출은 반드시 `api/client.ts`를 통해서만 하고, 컴포넌트에서 `fetch`를 직접 호출하지 않는다.

## 데이터 계약

그래프 JSON 스키마, API 엔드포인트 계약, `tracelens.config.json` 스키마의 최신 정의는 task.md에 있다(6단계에서 추가된 `dataAccess`·`middleware` 섹션과 `db` 설정은 task3.md 2장). 스키마를 변경할 때는 Analyzer/Server/client 세 곳의 타입 정의를 함께 갱신하고 해당 task 문서도 갱신한다. 현재 스키마는 **v6**이다.

## 진행 상태 확인

task.md·task2.md·task3.md의 각 단계 체크리스트가 현재 진행 상태를 나타낸다. 새 작업을 시작하기 전에 세 문서를 먼저 읽고 지금이 몇 단계인지, 어떤 항목이 남았는지 확인한 뒤 그 단계 범위 안에서만 작업한다. 다음 단계의 기능을 앞당겨 구현하지 않는다.

## 분석 대상과 이 저장소를 혼동하지 않기

이 저장소는 도구 자체의 코드다. 사용자가 분석하는 대상(예: 사내 ASP.NET Core 서비스)은 별도 경로의 다른 솔루션이며, 분석 결과물(`.tracelens/graph.json`, `.tracelens/snapshot.json`)은 이 저장소가 아니라 **분석 대상 솔루션 폴더 아래**에 생성된다. 이 저장소 안에 분석 결과 예시가 필요하면 `tests/TraceLens.Analyzer.Tests/Fixtures` 아래의 샘플 솔루션을 사용한다.
