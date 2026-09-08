*[English](README.md) · 한국어*

# TraceLens

**ASP.NET Core Web API의 호출 경로와 코드 근거를 확인하고, AI 코딩 에이전트에 필요한 문맥을 전달하는 로컬 개발 도구다.**

소스를 **정적으로** 읽어 만든 결과다. 실행 중인 요청의 이력이나 시간(런타임 분산 추적)은 수집하지 않는다.
이름의 "Trace"는 코드의 호출 경로를 따라간다는 뜻이지 런타임 트레이싱이 아니다.

## 60~90초로 보는 흐름

```
1) tracelens analyze <내 .sln 또는 .csproj>       # 분석하고 .tracelens/graph.json에 저장
2) tracelens serve   --target <같은 경로>          # http://127.0.0.1:25011 에서 UI 열기
3) 진입점 선택 → 소스 근거 확인 → AI 문맥 복사     # 근거·한계까지 함께 복사된다
4) AI가 코드를 고친 뒤 → "재분석하고 비교"         # 무엇이 달라졌는지 확인
```

화면 녹화는 릴리스마다 새로 찍어 올린다. 이 문서의 명령과 화면은 현재 저장소(main) 기준이며,
아직 캡처가 없는 항목은 없는 대로 둔다(있는 것처럼 목업을 올리지 않는다).

## 빠른 시작

### 필요한 것

| 항목 | 필요 시점 |
|---|---|
| .NET 8 이상 SDK | 항상 필요하다. 분석 대상 프로젝트를 여는 데 쓴다. |
| Node.js 20 이상 | **소스에서 빌드할 때만** 필요하다. 배포물(ZIP·.NET tool)을 쓰면 필요 없다. |
| Claude Code CLI | 질문 패널의 "Claude Code로 실행"에만 필요하다. 문맥 복사는 없어도 된다. |
| Codex CLI · VS Code | TraceLens MCP를 직접 등록해 쓸 때만 필요하다([docs/mcp-setup_kr.md](docs/mcp-setup_kr.md)). |

### 배포물로 시작하기 (소스·Node.js 없이)

```powershell
# 저장소에서 만든 ZIP을 풀고
.\tracelens.exe doctor --target C:\repos\MyApp\MyApp.sln   # 환경부터 점검한다
.\tracelens.exe demo --dir C:\temp\tracelens-demo          # 예제를 복사한다
.\tracelens.exe analyze C:\temp\tracelens-demo\OrderApi\OrderApi.csproj
.\tracelens.exe serve   --target C:\temp\tracelens-demo\OrderApi\OrderApi.csproj
```

배포물을 직접 만들려면 저장소에서 `.\pack.ps1`을 실행한다.
`artifacts/`에 Windows x64 portable ZIP과 .NET tool 패키지, 그리고 체크섬·버전·수동 검증 절차를 적은
`manifest.txt`가 생긴다. 게시(nuget push 등)는 이 스크립트가 하지 않는다.

지원을 확인한 조합만 아래에 적는다. 확인하지 않은 운영체제는 넣지 않는다.

| 운영체제 | 확인 상태 |
|---|---|
| Windows 11 x64 · .NET 8 런타임 + .NET SDK | portable ZIP으로 doctor·analyze·serve·소스 보기까지 확인했다 |
| macOS · Linux | 아직 확인하지 않았다. 코드는 net8.0이라 동작할 가능성이 높지만 지원 완료로 표기하지 않는다 |

분석 대상 형식은 `.sln`과 `.csproj`다. `.slnx`는 지금 쓰는 MSBuildWorkspace가 열지 못해
아직 지원하지 않고, doctor가 그 이유를 알려 준다.

### 소스에서 실행하기

```powershell
git clone https://github.com/jacking75/DotNetApiServerTraceLens.git tracelens
cd tracelens
.\run.ps1
```

`run.ps1`은 클라이언트 의존성 설치 → 서버 빌드 → 클라이언트 프로덕션 빌드까지 한 번에 하고 서버를 띄운다.
브라우저에서 <http://127.0.0.1:25011> 하나만 열면 된다.

| 옵션 | 설명 |
|---|---|
| `.\run.ps1` | 클라이언트를 빌드해 서버 하나로 실행한다. 평소에 쓰는 방식이다. |
| `.\run.ps1 -Dev` | API 서버(25011)와 Vite 개발 서버(25012)를 함께 띄운다. 클라이언트를 고칠 때 쓴다. |
| `.\run.ps1 -SkipBuild` | 빌드를 건너뛰고 바로 실행한다(이때는 Node.js가 없어도 된다). |
| `.\run.ps1 -NoBrowser` | 브라우저를 자동으로 열지 않는다. |

### 명령 한눈에 보기

```text
tracelens doctor  [--target <경로>] [--json]   실행 환경을 점검한다. 설치·설정 변경은 하지 않는다.
tracelens analyze <경로> [--json]              한 번 분석하고 요약을 출력한다.
tracelens serve   [--target <경로>] [--port n] UI 서버를 띄운다(루프백 전용).
tracelens mcp     --graph <graph.json 경로>    로컬 stdio MCP 서버를 띄운다.
tracelens demo    [--dir <복사할 위치>]         예제를 복사하고 학습 순서를 안내한다.
```

## 지금 지원하는 범위

정적 분석이 확정할 수 있는 것과 없는 것을 구분해서 적는다.

| 대상 | 지원 | 비고 |
|---|---|---|
| 컨트롤러 진입점 | 지원 | `[HttpGet]` 계열 특성과 `[Route]`. 한 메서드에 라우트가 여러 개면 각각 다른 엔드포인트로 잡는다. |
| Minimal API 진입점 | 지원 | `MapGet/MapPost/MapPut/MapDelete/MapPatch/MapMethods/Map`, `MapGroup` 접두사(중첩 포함), 메서드 그룹·람다·로컬 함수 핸들러. |
| 확장 메서드 안의 등록 | 지원(추정) | `app.MapFeatureEndpoints()`처럼 숨은 등록을 제한 깊이(4)까지 따라간다. 접두사를 호출 지점에서 채웠다는 사실을 표시한다. |
| 동적 라우트 | 미지원 | 라우트가 상수가 아니면 소스에 적힌 문자열을 그대로 보여 주고 **추정**으로 표시한다. |
| 인터페이스 호출 | 지원 | DI 등록(`AddScoped<I, Impl>` 등)으로 구현체까지 잇는다. 등록이 없으면 솔루션 안 구현체가 하나뿐일 때만 잇고 **추정**으로 표시한다. |
| keyed·조건부 DI 등록 | 확정하지 않음 | 실행 시 어느 구현체가 쓰일지 정적으로 알 수 없어 엣지를 만들지 않고 이유를 남긴다. |
| DB·캐시 접근 | 지원(설정으로 조정) | SQL 리터럴과 캐시 호출에서 읽는다. 사내 래퍼는 `tracelens.config.json`의 `db.queryApis`에 적으면 된다. |
| reflection·이벤트 구독·메시지 핸들러 | 미지원 | 호출 엣지로 만들지 않는다. "호출자가 없다"가 "지워도 안전하다"는 뜻이 아니다. |

분석 결과에는 **무엇을 못 봤는지**가 함께 들어간다. 화면 위쪽 요약과 AI 문맥에
`해석률 = 해석한 지원 대상 호출 지점 / 검사한 지원 대상 호출 지점`, 컴파일 오류가 있는 프로젝트 수,
확정하지 못한 호출 지점 수가 표시된다. 이 값은 호출 "정확도"가 아니다.

## 여섯 가지 보기

상단 툴바에서 **호출 흐름 · 클래스 다이어그램 · 전체 흐름도 · 시작 흐름 · DB·캐시 · 미들웨어**를 오간다.
UI는 한국어와 영어를 지원하며, 오른쪽 위 언어 선택으로 바꾼다(브라우저 언어를 기본값으로 참고한다).

- **호출 흐름** — 진입점 하나를 골라 분기 지점까지 그리고 `⊕`로 한 단계씩 파고든다.
- **클래스 다이어그램** — 솔루션 전체를 UML 박스로 한 캔버스에 그리고, 멤버 접기·관계 4종 토글·네임스페이스 체크박스·줌으로 밀도를 조절한다. 클래스가 150개를 넘으면 접힌 상태로 시작한다.
- **전체 흐름도** — 진입점 지도에서 라우트 줄을 누르면 그 진입점의 메서드 단위 전 구간으로 들어간다.
- **시작 흐름** — Program.cs를 소스 순서대로 읽어 DI 등록(수명 포함)과 미들웨어 파이프라인 순서를 보여 준다.
- **DB · 캐시** — 저장소 접근 지도와, 트랜잭션 없이 쓰는 경로에서 무엇이 되돌려지지 않는지(보상 처리) 판정을 보여 준다.
- **미들웨어** — `app.UseXxx()` 순서와 각 미들웨어의 요청/응답 구간, 단락 지점을 보여 준다.

점검 결과는 항상 **근거와 확실도(확실/추정)**를 함께 보여 주고, 검사하지 않은 규칙도 "꺼짐"으로 남겨
무엇을 보지 않았는지 알 수 있게 한다.

## AI와 함께 쓰는 흐름

1. 화면에서 대상(진입점·메서드·클래스·점검 항목·쓰기 경로)을 고른다.
2. 질문 패널에서 **작업 목적**(코드 이해 / 변경 계획 / 수정 검토 / DB·캐시 조사)과 **길이**(짧게·균형·자세히)를 고른다.
3. 미리보기에서 포함된 파일, 소스 포함 여부, 길이, 생략한 항목을 확인한다.
4. **문맥 복사**로 쓰는 AI 도구에 붙여넣거나, **Claude Code로 실행**으로 그 자리에서 물어본다.

문맥에는 순서대로 이런 것이 들어간다.

1. 작업 목적과 질문, 선택한 코드의 식별자
2. 분석 시각·snapshotId·신선도·분석 범위와 중요한 미해석 사유
3. 선택한 메서드의 시그니처와 **검증된** 소스 구간(분석 당시와 같은 내용인지 표시한다)
4. 진입점에서 대상까지의 대표 호출 경로와 각 호출 지점의 근거(직접 호출 / DI 등록 / 유일 구현체 추정)
5. 직접 호출자·피호출자와 관련 DB·캐시·미들웨어 점검
6. 그래프에서 확인한 테스트 후보(테스트를 실행한 결과가 아니다)
7. 길이 제한으로 생략한 항목과 더 조회하는 방법

> 분석은 로컬에서만 하지만, **외부 AI에 질문하면 그 문맥이 해당 서비스로 전송된다.**
> 그래서 보내기 전에 미리보기를 두고, 소스 본문을 빼는 선택지와 파일 단위 제외를 제공한다.
> 비밀값 자동 탐지만으로 안전을 보장하지는 않는다.

### AI 도구에 직접 연결하기 (MCP)

TraceLens는 로컬 stdio MCP 서버를 제공한다. 질문 패널의 "MCP 연동 설정 보기"를 누르면
지금 대상과 설치된 호스트에 맞춘 설정 조각을 보여 준다(기존 설정을 덮어쓰지 않는다).
자세한 절차는 [docs/mcp-setup_kr.md](docs/mcp-setup_kr.md)에 있다.

| 도구 | 무엇을 돌려주나 |
|---|---|
| `get_analysis_status` | 분석 대상·시각·신선도·지원 기능·진단 요약 |
| `get_context` | UI 미리보기와 같은 작업 문맥(근거·경로·한계 포함) |
| `get_entrypoint_flow` | 진입점 하나의 제한된 호출 경로와 경계 |
| `get_findings` | DB·미들웨어 점검과 확실도, 꺼진 규칙 목록 |
| `get_impact` | 이 메서드를 바꾸면 영향받을 수 있는 진입점 후보와 근거 경로 |
| `compare_snapshots` | 보관한 기준과 지금 분석의 구조·본문·점검 차이 |
| `get_node` · `get_source` · `get_callers` · `get_callees` · `get_entrypoints` · `search_symbol` | 노드·소스·인접 호출·진입점·심볼 검색 |

모든 도구는 **이미 저장된 분석 결과만 읽고 재분석하지 않는다.** 응답에는 `snapshotId`가 들어가고,
후속 호출에 `expectedSnapshotId`를 넘기면 그 사이에 분석이 바뀌었는지 알 수 있다.

## 변경 영향과 분석 전후 비교

- **변경 영향**: 메서드를 고르고 "영향받는 API 찾기"를 누르면 호출 관계를 거슬러 올라가 도달하는 HTTP 진입점을 찾는다. 결과는 **후보**이며, reflection·외부 호출·이벤트 구독·런타임 DI 선택까지 증명하지 않는다.
- **분석 전후 비교**: 수정 전에 "변경 전으로 저장"으로 기준을 만들고, AI가 고친 뒤 "재분석하고 비교"를 누른다. 엔드포인트·메서드(시그니처/본문)·호출·DB 접근·점검의 차이를 보여 준다. 분석기·규칙·범위가 다르면 비교 조건이 다르다고 알린다. **"차이 없음"은 안전하다는 뜻도, 테스트가 통과한다는 뜻도 아니다.**

## 신선도와 재분석

파일 저장을 감시해서 자동으로 다시 분석하지 않는다. 재분석은 항상 사용자가 실행한다.
대신 마지막 분석과 현재 소스가 같은지 항상 확인할 수 있게 신선도를 표시한다.

- `fresh` — 기록한 입력과 지금 파일이 모두 같다.
- `stale` — 바뀐·지워진·**새로 생긴** 파일이나 설정 변경이 있다.
- `unknown` — 확인 범위를 벗어났다(옛 형식으로 저장된 분석 등). 이때는 `fresh`라고 단정하지 않는다.

## 비슷한 도구와의 관계

호출자 추적이나 영향 분석을 제공하는 상용·오픈소스 도구는 이미 여럿 있다.
예를 들어 NDepend는 호출자를 통한 영향 범위 탐색과 별도의 공식 MCP 서버를 제공한다
([영향 분석 문서](https://www.ndepend.com/docs/refactoring-impact-analysis),
[MCP 서버 저장소](https://github.com/ndepend/NDepend.MCP.Server) — 별도 라이선스/평가판이 필요하다).

TraceLens는 그보다 좁게, **ASP.NET Core 요청 중심의 탐색 · 로컬 정적 분석 · 근거가 붙은 AI 문맥 전달 ·
DB·캐시 보상 처리 확인**을 하나의 사용 흐름으로 묶는 데 집중한다(MIT 라이선스).
이는 제품 포지셔닝이며 다른 도구보다 우월하다는 측정 결과가 아니다.

## 문서

- [docs/manual_kr.html](docs/manual_kr.html) — 화면 구성, 여섯 보기, AI 연동, 실제 사용 절차를 한 곳에 모은 사용 설명서
- [docs/mcp-setup_kr.md](docs/mcp-setup_kr.md) — Claude Code · Codex CLI · VS Code에 MCP 등록하기
- [docs/data-contract.md](docs/data-contract.md) — `graph.json` 스키마와 API·MCP 계약, 버전 이행 규칙
- [CONTRIBUTING.md](CONTRIBUTING.md) — 분석 누락을 신고하는 방법과 기여 범위
- [CHANGELOG.md](CHANGELOG.md) — 릴리스 노트(수동 갱신)
- [CLAUDE.md](CLAUDE.md) — AI 코딩 에이전트를 위한 저장소 지침과 설계 제약

## 기술 구성

| 영역 | 선택 | 비고 |
|---|---|---|
| 분석 | C# / Roslyn | 메서드 호출 관계와 파일·줄 정보를 정확히 뽑는다 |
| 공통 조회 | `TraceLens.Context` | 색인·문맥·영향·비교·소스 접근 정책. Server와 MCP가 함께 쓴다 |
| 서버 | ASP.NET Core Minimal API | 루프백에만 바인딩한다. 원격 접근 옵션은 두지 않는다 |
| 프런트엔드 | React + TypeScript (Vite) | 호출 흐름은 Cytoscape.js, 나머지 다이어그램은 DOM+SVG, 코드 보기는 Monaco |
| 저장 | `.tracelens/` 아래 JSON | 임시 파일에 쓰고 검증한 뒤 교체한다 |
| 에이전트 연동 | 로컬 stdio MCP + 선택적 Claude Code CLI 호출 | 원격 노출 없음 |

## 라이선스

MIT. [LICENSE](LICENSE)를 참고한다.
