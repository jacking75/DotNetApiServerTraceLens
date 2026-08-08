# TraceLens

ASP.NET Core Web API 코드베이스의 호출 흐름을 그래프로 탐색하고, 선택한 코드 위치를 Claude Code에게 곧바로 질의할 수 있는 로컬 개발 도구다.

## 이 도구가 하는 일

HTTP 엔드포인트(진입점)부터 시작해서 컨트롤러 → 서비스 → 리포지토리로 이어지는 실제 메서드 호출 흐름을 그래프로 보여준다. 그래프의 노드를 클릭하면 해당 위치의 실제 소스 코드가 옆에 뜨고, 필요하면 그 노드 내부의 더 세부적인 호출 흐름으로 드릴다운할 수 있다. 그래프에서 코드 위치를 고른 상태로 질문을 입력하면 파일 경로·라인 범위·심볼명이 자동으로 캡처되어 Claude Code에게 전달된다.

## 왜 만들었나

큰 ASP.NET Core 코드베이스를 새로 파악하거나 다른 사람에게 설명할 때, 코드를 눈으로 따라가며 구조를 재구성하는 데 시간이 많이 든다. 기존 오픈소스 도구들은 아키텍처 다이어그램은 잘 그려주지만 "그 노드를 클릭하면 실제 코드가 보이고, 그 자리에서 바로 AI 에이전트에게 물어볼 수 있는" 흐름까지는 지원하지 않아서 직접 만들기로 했다.

## 핵심 기능

- 진입점(HTTP 엔드포인트) 기준으로 단계식으로 펼쳐지는 호출 흐름 그래프
- 노드 클릭 시 실제 소스 코드로 드릴다운, breadcrumb으로 이전 단계 이동
- 선택한 위치를 Claude Code에게 질의 — 클립보드 복사 / 로컬 MCP 직접 연동 / 에디터에서 열기, 세 가지 방식 지원
- 마지막 분석 시점과 현재 코드가 같은지 보여주는 신선도 표시, 필요할 때만 수동으로 재분석

## 아키텍처

```mermaid
flowchart TB
    subgraph Browser["브라우저 (React + TypeScript)"]
        GV[GraphView\nCytoscape.js]
        CP[CodePanel\nMonaco Editor]
        AP[AskPanel]
        FB[FreshnessBadge]
    end

    subgraph Local["로컬 머신 (localhost 전용)"]
        Server[TraceLens.Server\nASP.NET Core Minimal API]
        Analyzer[TraceLens.Analyzer\nRoslyn 기반 분석기]
        Mcp["TraceLens.Mcp\n(3단계) 로컬 전용 MCP 서버"]
        CC[Claude Code CLI]
    end

    Target[(분석 대상\nASP.NET Core 솔루션)]
    Graph[(.tracelens/graph.json\n.tracelens/snapshot.json)]

    GV -->|GET /api/graph| Server
    CP -->|GET /api/source| Server
    AP -->|POST /api/ask| Server
    FB -->|GET /api/freshness| Server
    Server -->|분석 실행| Analyzer
    Analyzer -->|파싱| Target
    Analyzer -->|결과 저장| Graph
    Server -->|결과 로드| Graph
    Server -.->|3단계: 헤드리스 호출| CC
    CC -.->|3단계: 도구 조회| Mcp
    Mcp -.->|소스/노드 조회| Graph
```

## 기술 스택

| 영역 | 선택 | 비고 |
|---|---|---|
| 분석기 | C# / Roslyn (Microsoft.CodeAnalysis) | 메서드 호출 관계와 파일·라인 정보를 정확히 추출 |
| 서버 | ASP.NET Core Minimal API | 그래프 서빙, 소스 서빙, React 정적 파일 호스팅을 겸함 |
| 프론트엔드 | React + TypeScript (Vite) | Cytoscape.js(그래프), Monaco Editor(코드 뷰) |
| 데이터 저장 | JSON 파일 (`.tracelens/` 아래) | 구현 단순함 우선, 필요해지면 이후 교체 검토 |
| 에이전트 연동 | Claude Code CLI + 로컬 MCP 서버 | 3단계에서 추가, 원격 노출 없이 로컬에서만 동작 |

## 시작하기

### 요구사항

- .NET 8 이상 SDK
- Node.js 20 이상
- Claude Code CLI (3단계 기능을 쓸 경우)

### 설치 및 실행

```bash
git clone <repo-url> tracelens
cd tracelens

# 백엔드
dotnet build
dotnet run --project src/TraceLens.Server

# 프론트엔드 (다른 터미널에서)
cd client
npm install
npm run dev
```

브라우저에서 `http://localhost:5173`(개발 모드) 또는 `dotnet run`이 띄운 서버 주소로 접속한다. 분석할 ASP.NET Core 솔루션의 `.sln` 경로를 입력하면 첫 분석이 시작된다.

## 폴더 구조

세부 폴더 구조와 각 파일의 책임은 CLAUDE.md의 "저장소 구조" 절을 참고한다.

## 현재 개발 단계

전체 단계 구성과 각 단계의 상세 작업 목록, 완료 기준은 task.md에 정리되어 있다. 새로 합류하는 사람은 task.md의 체크리스트를 보면 현재 어디까지 진행됐는지 알 수 있다.

## 설계 배경

이 도구를 만들기 전에 정리한 인터랙티브 목업과 논의 과정을 `docs/concept-mockup.html`에 남겨둔다. 그래프 드릴다운과 Claude Code 연동 UX가 실제로 어떤 느낌인지 확인하려면 그 파일을 브라우저로 열어보면 된다.
