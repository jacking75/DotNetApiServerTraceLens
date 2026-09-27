# 기여 안내

TraceLens는 MIT 라이선스의 로컬 개발 도구다. 지금 받고 싶은 기여는 세 가지로 좁힌다.

1. **분석 누락 신고** — 잡혔어야 할 진입점·호출이 안 잡히는 최소 재현 코드
2. **번역** — `client/src/i18n/ko.ts`가 원본이고 `en.ts`가 그 키로 타입이 제한된다
3. **사용 절차 개선** — 안내 문구, 레시피, 매뉴얼의 실제 절차

기능을 크게 늘리는 제안은 [CLAUDE.md](CLAUDE.md)의 설계 제약과 먼저 맞춰 본다. 특히 다음은 범위 밖이다.
파일 저장 감시에 따른 자동 재분석, 원격 분석 서버, 이벤트 구독을 호출 엣지로 만드는 것,
자체 코드 편집기·자동 커밋, 모든 언어 지원.

## 분석 누락을 신고할 때

아래 여섯 가지가 있으면 재현 픽스처로 바로 옮길 수 있다. **실제 사내 코드는 넣지 않는다.**

```text
1. TraceLens 버전       : tracelens --version 결과
2. .NET SDK             : dotnet --info 의 SDK 줄
3. 분석 대상 형식        : .sln / .csproj, 대상 프레임워크
4. 최소 코드            : 문제를 재현하는 가장 작은 익명화 코드
5. 기대한 결과          : "GET /api/orders 진입점이 잡혀야 한다" 처럼 구체적으로
6. 실제 결과            : tracelens analyze <대상> --json 출력과 화면 캡처
```

`tracelens doctor --target <대상> --json` 결과를 함께 붙이면 환경 문제와 분석 문제를 빨리 가를 수 있다.
doctor는 API 키나 전체 환경 변수를 수집하지 않는다.

## 개발 환경

```powershell
git clone https://github.com/jacking75/DotNetApiServerTraceLens.git tracelens
cd tracelens
.\run.ps1 -Dev        # API 서버(25011) + Vite 개발 서버(25012)
```

## 검증

바꾼 곳에 맞는 것만 고르면 된다. CI는 이 저장소의 실행 범위 밖이라 로컬 명령을 기준으로 한다.

```powershell
dotnet build TraceLens.sln            # 경고 0개를 유지한다
dotnet test  TraceLens.sln            # Analyzer · Server · Mcp 테스트

cd client
npm run lint                          # 훅 규칙·correctness (oxlint)
npm test                              # 배치·i18n·다이어그램 렌더·상태 저장 테스트
npm run build                         # tsc -b + vite build

cd ..
.\pack.ps1                            # 배포물을 만들고 체크섬·검증 절차를 남긴다
.\pack.ps1 -Runtimes @('win-x64','linux-x64','osx-arm64')   # RID별 ZIP (빌드만 한다)
.\pack-desktop.ps1                   # Windows 11 x64 WebView2 단일 EXE
```

데스크톱 개발·배포 절차는 [Windows 11 데스크톱 앱 안내](docs/desktop_kr.md)를 따른다.
평소 UI 작업은 `run.ps1 -Dev`로 하고, 단일 EXE는 배포 전에 만든다.
`pack.ps1`은 `artifacts/`를 다시 만들므로 두 배포물이 모두 필요하면 `pack-desktop.ps1`을 나중에 실행한다.

`pack.ps1`이 남기는 `manifest.txt`의 **수동 검증 절차 6단계**는 사람이 직접 한다. 자동화하지 않는다 —
"만들었다"와 "그 OS에서 동작한다"는 다른 말이기 때문이다. 5번(공백·한글이 든 경로)은 특히 빠뜨리기 쉽다:
`C:\테스트 폴더\My App`처럼 공백과 한글이 함께 든 경로에 풀어 doctor · analyze · serve · 소스 보기를 확인한다.
확인한 것만 README 지원 표에 적는다.

성능을 손댈 때는 재고 나서 고친다. 재는 명령은 두 개다.

```powershell
dotnet run --project src/TraceLens.Cli -- analyze <대상> --profile   # 단계별 시간과 최대 작업 집합
cd client; npm run measure:layout                                    # treeLayout·cullToViewport 배치 비용
```

`measure:layout`은 `vitest bench`라서 `npm test`에 끼지 않는다. 임계값 assert(성능 가드)를 두지 않는 이유는
기계마다 값이 달라 CI에서 깜빡이는 실패만 늘기 때문이다. 나온 수치는 CHANGELOG에 **측정 조건과 함께** 적는다.

> **타입 검사는 `npm run build`(= `tsc -b`)가 한다.** `tsc --noEmit`은 이 저장소의 tsconfig가
> 프로젝트 참조만 담고 있어서 아무것도 검사하지 않는다. 고친 뒤에는 `npm run build`로 확인한다.
>
> 린터는 ESLint가 아니라 **oxlint**다. typescript-eslint가 이 저장소의 TypeScript 버전을 아직 지원하지 않는다.
> 검사하는 것은 `react-hooks` 규칙과 correctness뿐이고, 서식 규칙은 넣지 않았다(설정은 `client/.oxlintrc.json`).

> **클라이언트를 고친 뒤 `tracelens serve`로 확인할 때는 순서에 주의한다.**
> CLI는 자기 출력 폴더에 복사된 `wwwroot`를 서빙하므로, `npm run build` 다음에 `dotnet build`를 한 번 더
> 해야 새 화면이 뜬다(`pack.ps1`은 이 순서를 지킨다). 순서를 신경 쓰기 싫으면 `run.ps1`을 쓰면 된다 —
> 이쪽은 Server 프로젝트의 `wwwroot`를 그대로 서빙한다.

분석기를 고쳤다면 **픽스처를 함께 늘린다.**

| 픽스처 | 무엇을 확인하나 |
|---|---|
| `tests/TraceLens.Analyzer.Tests/Fixtures` | 컨트롤러·DI 해석·클래스 메타 |
| `tests/TraceLens.Analyzer.Tests/FixturesData` | DB·캐시 접근과 보상 처리, 미들웨어 |
| `tests/TraceLens.Analyzer.Tests/FixturesMinimalApi` | Minimal API 등록, 그룹 접두사, 람다·로컬 함수 핸들러 |
| `samples/OrderApi` | 배포물의 예제이자 사람이 보는 회귀 대상 |

서버·Context를 고쳤다면 아래 테스트 클래스를 함께 본다.

| 테스트 | 무엇을 확인하나 |
|---|---|
| `AnalysisJobServiceTests` | 작업 시작·중복 거절·목록 상한 |
| `SnapshotEndpointsTests` | 기준 저장·목록·비교·삭제, 가져온 결과 거절 |
| `SourceAccessPolicyTests` | `..`·절대 경로·UNC·대소문자·심볼릭 링크 탈출, 오프라인 스냅샷 거부 |
| `ContextLibraryTests` | 신선도 5경로, 환경 점검, 공유 보고서, 기준 보관 |
| `RequestGuardsTests` | Origin 검사와 500 응답의 errorId |
| `DiResolverTests` | 자기 등록·팩토리·open generic·조건부·분기 등록 |
| `AnalyzerAccuracyTests` | 제네릭·확장 메서드 노드 ID, 절대 라우트, 설정 파일 검증 |
| `SemanticCacheTests` | 문서별 시맨틱 모델 공유·준비 상한 |

## 코딩 규칙

전체 규칙은 [CLAUDE.md](CLAUDE.md)에 있다. 자주 걸리는 것만 옮긴다.

- C#은 `Nullable enable`, file-scoped namespace. Analyzer는 Server/Mcp를 참조하지 않는다.
- 비동기 I/O는 `async`/`await`로만 쓰고 `.Result`/`.Wait()`를 쓰지 않는다.
- TypeScript는 `strict: true`, 함수형 컴포넌트와 훅만 쓴다. 서버 호출은 `api/client.ts`를 통해서만 한다.
- 배치 계산(`client/src/graph/`)은 React에 의존하지 않는 순수 함수로 두고 단위 테스트를 붙인다.
- 사용자에게 보이는 문자열은 `i18n` 사전에 넣는다. 심볼·라우트·코드·파일 경로는 번역하지 않는다.

### 규칙 문구는 어디에서 번역하나

분석기가 만드는 문자열은 세 종류이고 취급이 다르다.

| 문자열 | 어디에 있나 | 왜 |
|---|---|---|
| 규칙 카탈로그(42개의 이름·설명·그룹) | `client/src/i18n/ruleText.ts`에 규칙 ID로 영어를 둔다 | 고정된 유한 집합이라 미리 번역할 수 있다 |
| 개별 점검 결과(what · why · fix) | 번역하지 않는다. 분석기가 만든 그대로 보여 준다 | 실제 심볼·테이블 이름·줄 번호를 섞어 분석할 때 만든다. 미리 번역해 둘 수 없다 |
| `graph.json`에 저장되는 값 | 분석기의 언어 그대로 저장한다 | 분석기에 로케일을 넣으면 같은 코드가 언어에 따라 다른 `graph.json`이 되어 스냅샷 비교가 흔들린다 |

분석기에 규칙을 더하면 `ruleText.test.ts`가 실패한다. 그 테스트는 `DataRuleEngine.cs`·`MiddlewareScanner.cs`를
직접 읽어 규칙 ID를 뽑고 영어 표와 대조한다. 표가 조용히 뒤처지지 않게 하는 장치다.

## 판정 문구에 대한 약속

이 도구가 지키려는 선이다. 코드 리뷰에서도 이 기준으로 본다.

- 확정하지 못한 것은 **추정**이라고 쓰고 근거를 함께 남긴다.
- 검사하지 않은 규칙은 목록에서 지우지 않고 "꺼짐"으로 남긴다.
- "정확도 n%"처럼 분모가 없는 수치를 쓰지 않는다. 해석률은 분모를 함께 적는다.
- "호출자가 없다"를 "삭제해도 안전하다"로 바꿔 쓰지 않는다.
- "차이 없음"을 "안전" 또는 "테스트 통과"로 바꿔 쓰지 않는다.
- 측정하기 전에는 "토큰 n% 절약", "개발 속도 n배" 같은 문구를 쓰지 않는다.
