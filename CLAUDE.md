# TraceLens
이 문서는 TraceLens 저장소에서 작업하는 AI 코딩 에이전트(Claude Code 포함)를 위한 지침이다. 사람이 보는 개요는 README.md, 단계별 구현 상세와 체크리스트는 task.md를 참고한다.

## 프로젝트 한 줄 설명
ASP.NET Core Web API 코드베이스의 호출 흐름을 진입점 기준 그래프로 시각화하고, 그래프에서 선택한 코드 위치를 Claude Code에게 곧바로 질의할 수 있게 해주는 로컬 개발 도구다. 데모용이 아니라 실전에서 계속 쓰는 것을 목표로 개발한다.

## 반드시 지켜야 할 설계 제약
아래 다섯 가지는 이미 논의를 거쳐 확정된 방향이다. 별도 지시가 없는 한 이 범위를 벗어나는 기능을 임의로 추가하지 않는다.

1. **그래프 규모.** 그래프는 항상 하나의 포커스(HTTP 진입점 또는 메뉴에서 고른 클래스) 기준으로 단계식으로 보여준다. 전체 그래프를 한 화면에 다 그리는 UI를 만들지 않는다. 탐색 축은 이 두 가지(진입점 목록, 클래스 목록)뿐이고 파일 트리 탐색기는 만들지 않는다. 자세한 방식은 task.md의 "포커스 기반 윈도잉"을 따른다.
2. **엣지 범위.** 그래프의 엣지는 메서드 호출 관계로만 구성한다. 생성자(`new X()`)도 메서드 호출로 보고 포함하며(`includeObjectCreation`으로 끌 수 있다), 프로퍼티 읽기/쓰기 같은 접근은 엣지로 만들지 않는다. DI 의존 관계나 이벤트/메시지 발행-구독 흐름을 엣지로 표현하는 기능을 넣지 않는다. DI 등록 정보(`AddScoped`/`AddSingleton`/`TryAdd*` 등)는 인터페이스 호출을 실제 구현체 메서드로 정확히 연결하는 내부 해석 로직에만 사용하고, 등록을 찾지 못하면 솔루션 안 구현체가 하나뿐일 때만 그 구현체로 잇는다.
3. **최신 상태 유지.** 파일 저장을 감지해서 자동으로 재분석하는 기능을 넣지 않는다. 재분석은 항상 사용자가 명시적으로 실행한다(버튼 클릭 또는 API 호출). 대신 마지막 분석 시점과 현재 코드가 같은지 항상 확인할 수 있는 신선도 표시를 유지한다.
4. **로컬 전용.** 그래프 서버와 (3단계의) MCP 연동은 `localhost`에서만 동작한다. 원격에서 접근 가능하게 만드는 옵션, 인증 없이 외부 바인딩을 허용하는 설정을 추가하지 않는다.
5. **개발 순서.** 단계적으로 개발하되, 각 단계가 끝나면 실제로 켜서 쓸 수 있는 상태여야 한다. "임시로 써보는 데모"가 아니라 계속 쓸 도구를 목표로 코드 품질(에러 처리, 최소한의 테스트)을 유지한다.

## 저장소 구조

```
tracelens/
├── CLAUDE.md
├── run.ps1                       # 빌드 + 실행을 한 번에 하는 스크립트
├── README.md
├── task.md
├── TraceLens.sln
├── src/
│   ├── TraceLens.Analyzer/       # Roslyn 기반 분석 라이브러리 (UI/서버에 대한 의존성 없음)
│   │   ├── SolutionLoader.cs
│   │   ├── EntryPointScanner.cs
│   │   ├── SolutionScanner.cs            # 솔루션 전체 클래스/메서드 스캔 (4단계)
│   │   ├── InvocationResolver.cs         # 호출식 → 심볼 해석 (스캔/BFS 공용)
│   │   ├── CallGraphWalker.cs            # 진입점 BFS (analysisScope=entryPoints)
│   │   ├── DiResolver.cs
│   │   ├── NodeClassifier.cs
│   │   ├── SnapshotHasher.cs
│   │   └── Models/
│   │       ├── GraphNode.cs
│   │       ├── GraphEdge.cs
│   │       ├── GraphClass.cs
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
        │   ├── GraphView.tsx             # Cytoscape.js 래퍼
        │   ├── CodePanel.tsx             # Monaco Editor 래퍼
        │   ├── AskPanel.tsx
        │   ├── PathPicker.tsx             # 분석 대상 선택 대화상자
        │   └── FreshnessBadge.tsx
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
- 서버 API 호출은 반드시 `api/client.ts`를 통해서만 하고, 컴포넌트에서 `fetch`를 직접 호출하지 않는다.

## 데이터 계약

그래프 JSON 스키마, API 엔드포인트 계약, `tracelens.config.json` 스키마의 최신 정의는 task.md에 있다. 스키마를 변경할 때는 Analyzer/Server/client 세 곳의 타입 정의를 함께 갱신하고 task.md도 갱신한다.

## 진행 상태 확인

task.md의 각 단계 체크리스트가 현재 진행 상태를 나타낸다. 새 작업을 시작하기 전에 task.md를 먼저 읽고 지금이 몇 단계인지, 어떤 항목이 남았는지 확인한 뒤 그 단계 범위 안에서만 작업한다. 다음 단계의 기능을 앞당겨 구현하지 않는다.

## 분석 대상과 이 저장소를 혼동하지 않기

이 저장소는 도구 자체의 코드다. 사용자가 분석하는 대상(예: 사내 ASP.NET Core 서비스)은 별도 경로의 다른 솔루션이며, 분석 결과물(`.tracelens/graph.json`, `.tracelens/snapshot.json`)은 이 저장소가 아니라 **분석 대상 솔루션 폴더 아래**에 생성된다. 이 저장소 안에 분석 결과 예시가 필요하면 `tests/TraceLens.Analyzer.Tests/Fixtures` 아래의 샘플 솔루션을 사용한다.
