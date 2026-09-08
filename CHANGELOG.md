# 릴리스 노트

수동으로 갱신한다. 실제로 확인한 것만 적고, 확인하지 않은 지원 범위는 넣지 않는다.

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
