# task2.md — 클래스 다이어그램 · 전체 흐름도 구현 계획

`docs/structure-view-mockup.html` 목업을 실제 제품 화면으로 옮기기 위한 작업 문서다.
데이터 계약과 단계별 구현 상세는 아래에 있고, **진행 상태는 이 문서 상단의 태스크 목록으로만 관리한다**(끝낸 항목은 `[x]`로 바꾼다).

기존 1~4단계 내용은 `task.md`에, 설계 제약은 `CLAUDE.md`에 있다. 이 문서는 그 위에 얹는 5단계 작업이다.

---

## 태스크 목록

### A. 데이터 계약 (스키마 v4)

- [x] **A-1** `GraphClass`에 타입 메타(`typeKind`, `isAbstract`, `isStatic`) 추가
- [x] **A-2** `GraphClass`에 상속/구현 정보(`baseTypeId`, `baseTypeName`, `interfaceIds`, `interfaceNames`) 추가
- [x] **A-3** `GraphClass.members` 추가 — 필드·프로퍼티·메서드의 이름/타입/접근제한자/줄 번호/`nodeId`
- [x] **A-4** `GraphNode`에 `accessibility`, `returnType`, `parameterTypes` 추가
- [x] **A-5** `schemaVersion` 4로 올리고, v3 이하 문서를 읽을 때의 하위 호환 처리(빈 members·null 상속) 작성
- [x] **A-6** `tracelens.config.json`에 `includeMembers`(기본 `true`), `includeInterfaces`(기본 `true`) 추가
- [x] **A-7** `task.md`의 0장(데이터 계약)에 v4 스키마 반영

### B. Analyzer

- [x] **B-1** `SolutionScanner`가 인터페이스 선언도 수집(현재 제외됨) — `typeKind: "Interface"`, `kind: "Interface"`
- [x] **B-2** 멤버 수집기 추가 — 필드/프로퍼티/메서드/생성자를 `GraphClassMember`로 변환(접근제한자, 타입, 줄 번호)
- [x] **B-3** 멤버 타입 문자열에서 솔루션 내부 타입 id 해석(`IReadOnlyList<Order>` → `MyApp.Domain.Order`)
- [x] **B-4** `baseTypeId`/`interfaceIds` 채우기(솔루션 밖 타입은 이름만 남기고 id는 null)
- [x] **B-5** partial 클래스의 멤버 병합, 컴파일러 생성 멤버(자동 프로퍼티 backing field 등) 제외
- [x] **B-6** `NodeClassifier`에 인터페이스 기본 분류 규칙 추가(`kindRules`에 안 걸리면 `Interface`)
- [x] **B-7** 멤버 수집이 분석 시간에 주는 영향 측정(사내 솔루션 기준 전/후 초 단위 기록)
- [x] **B-8** 단위 테스트 — 픽스처에 인터페이스·상속·필드 주입을 추가하고 members/상속/구현이 기대대로 나오는지 검증

### C. Server

- [x] **C-1** `GET /api/graph`가 v4 문서를 그대로 반환하는지 확인(직렬화 옵션·크기 로그)
- [x] **C-2** `GET /api/graph/classes` 요약에 `typeKind`, `memberCount` 추가
- [x] **C-3** `GET /api/graph/namespaces` 추가 — `{ namespace, classCount, methodCount, kinds[] }[]`
- [x] **C-4** `POST /api/analyze` 응답에 `interfaceCount`, `memberCount` 추가
- [x] **C-5** graph.json 크기가 커진 만큼 응답 gzip 압축 확인(로컬이라 기본 꺼져 있으면 켠다)

### D. 클라이언트 공통 — 보기 전환과 레이아웃

- [x] **D-1** 상단 보기 전환 툴바 추가 — `호출 흐름 | 클래스 다이어그램 | 전체 흐름도`
- [x] **D-2** 좌측 패널을 보기별로 교체(진입점 목록 / 네임스페이스 목록 / 진입점 목록+전체 지도)
- [x] **D-3** 기존 `진입점 | 클래스` 탭 제거, 클래스 탭이 하던 1홉 관계도는 상세 패널의 "1홉만 보기"로 이동
- [x] **D-4** 3분할 스플리터 — 패널 사이 드래그로 좌/우 폭 조절, 더블클릭 시 기본값 복귀
- [x] **D-5** 반응형 — 페이지가 창 폭·높이를 그대로 쓰고, 창 크기 변경 시 다이어그램 재배치(디바운스 120ms)
- [x] **D-6** 보기 전환·필터 상태를 `localStorage`에 저장해서 새로고침 후 복원
- [x] **D-7** 재분석 후에도 현재 보기/선택/필터 유지

### E. 클래스 다이어그램

- [x] **E-1** `deriveRelations` — 상속/구현/연관/의존 네 종류 관계를 클래스 목록에서 파생(순수 함수 + 테스트)
- [x] **E-2** UML 박스 컴포넌트 — 이름(스테레오타입·네임스페이스) / 필드 / 메서드 3단, 멤버 줄 클릭 선택
- [x] **E-3** **트리(계층) 배치 엔진** — 층 나누기 → 더미 삽입 → 바리센터 정렬 → 좌표 배치(순수 함수 + 테스트)
- [x] **E-4** 진행 방향 전환(`→` LR / `↓` TB)과 선 라우팅(더미 경유 곡선, 되돌아가는 선은 옆면 연결)
- [x] **E-5** 관계별 선 스타일 + 화살촉(상속·구현은 빈 삼각형, 연관은 열린 화살표, 의존은 점선 화살표)
- [x] **E-6** 패키지 배치 모드 — 네임스페이스 격자 + 패널 폭에 맞춘 줄바꿈
- [x] **E-7** 툴바 컨트롤 — 배치/방향, 멤버(필드·메서드), 관계 4종 토글, 선택 1홉만, 줌 50/75/100/맞춤
- [x] **E-8** 좌측 네임스페이스 패널 — 체크박스 표시/숨김, 이름 클릭 시 스크롤+선택, 클래스 검색
- [x] **E-9** 선택 강조 — 선택한 클래스에 붙은 선만 강조, 관련 없는 박스는 흐리게
- [x] **E-10** 대규모 솔루션 기본값 — 클래스 150개 초과 시 멤버 접힘·의존 끔으로 시작하고 안내 문구 표시

### F. 전체 흐름도

- [x] **F-1** 전체 진입점 지도 — 컨트롤러 박스에 라우트를 줄로 붙이고 클래스 단위로 아래 흐름 연결
- [x] **F-2** 진입점 흐름 — 선택한 진입점에서 도달 가능한 메서드를 클래스로 묶어 층 배치
- [x] **F-3** 깊이 제한 컨트롤(2/3/4/전체)과 깊이 밖 호출 `+n` 표시
- [x] **F-4** 라우트 줄 → 진입점 흐름, breadcrumb으로 지도 복귀
- [x] **F-5** 재귀·순환 호출 방어(방문 집합, 자기 호출 표시)

### G. 상세 패널 · 질문 패널 연동

- [x] **G-1** 클래스 상세 — 스테레오타입/분류/네임스페이스/멤버 목록/관계 4종/외부 상위 타입
- [x] **G-2** 메서드 상세 — 시그니처·접근제한자·반환타입, 호출/피호출, 소스 미리보기
- [x] **G-3** 네임스페이스 상세 — 클래스 목록, 나가고 들어오는 의존
- [x] **G-4** 상세 패널의 이동 버튼(`흐름 보기`, `다이어그램에서 보기`, `1홉만 보기`, `패키지로 이동`)
- [x] **G-5** 질문 패널 캡처 컨텍스트를 클래스/네임스페이스 선택에도 대응

### H. 마무리

- [x] **H-1** `CLAUDE.md` 설계 제약 1번 수정 — 클래스 다이어그램·전체 흐름도의 예외와 그 대신 두는 안전장치 명시
- [x] **H-2** `README.md`에 새 보기 3종 스크린샷과 설명 추가
- [x] **H-3** 클라이언트 단위 테스트 — 관계 파생, 층 나누기, 더미 삽입, 교차 수 감소 검증
- [x] **H-4** 실제 사내 솔루션으로 렌더 성능 측정(클래스 수 / 첫 렌더 ms / 배치 계산 ms)
- [x] **H-5** `working_log.md` 기록 및 `task.md` 5단계 항목과 상호 링크

---

## 1. 데이터 계약 — 스키마 v4

### 1.1 왜 필요한가

지금 `graph.json`에는 **호출 엣지와 클래스 위치**만 있다. 목업의 클래스 다이어그램은 UML 박스를 그리므로 다음이 더 필요하다.

| 필요한 것 | 지금 상태 |
|---|---|
| 인터페이스 | `classes`에서 제외되어 아예 없다 |
| 필드·프로퍼티(이름·타입·접근제한자) | 없다 |
| 메서드의 접근제한자·반환타입 | 없다(`displayName`만 있다) |
| 상속·구현 관계 | 없다(DI 해석에만 내부적으로 쓰고 버린다) |

### 1.2 GraphClass (변경)

```jsonc
{
  "id": "MyApp.Application.Services.OrderService",
  "name": "OrderService",
  "namespace": "MyApp.Application.Services",
  "kind": "Service",                    // 기존 kindRules 결과
  "typeKind": "Class",                  // Class | Record | Struct | Interface  (A-1)
  "isAbstract": false,                  // (A-1)
  "isStatic": false,                    // (A-1)
  "baseTypeId": null,                   // 솔루션 안 클래스면 그 id, 아니면 null (A-2)
  "baseTypeName": "ControllerBase",     // 표시용. 외부 타입도 이름은 남긴다 (A-2)
  "interfaceIds": ["MyApp.Application.Abstractions.IOrderRepository"],
  "interfaceNames": ["IOrderRepository"],
  "file": "src/MyApp.Application/Services/OrderService.cs",
  "startLine": 12,
  "endLine": 140,
  "members": [                          // (A-3)
    {
      "memberKind": "Field",            // Field | Property | Method | Constructor
      "name": "_repository",
      "type": "IOrderRepository",       // 표시용 타입 문자열(제네릭 포함)
      "typeIds": ["MyApp.Application.Abstractions.IOrderRepository"],
      "accessibility": "Private",       // Public | Protected | Internal | Private
      "isStatic": false,
      "startLine": 14,
      "endLine": 14,
      "nodeId": null                    // 실행 가능한 멤버면 nodes[].id, 아니면 null
    },
    {
      "memberKind": "Method",
      "name": "CreateOrderAsync",
      "signature": "CreateOrderAsync(CreateOrderRequest)",
      "type": "Task<Order>",            // 메서드는 반환 타입을 type에 담는다
      "typeIds": ["MyApp.Domain.Order"],
      "accessibility": "Public",
      "isStatic": false,
      "startLine": 31,
      "endLine": 79,
      "nodeId": "MyApp.Application.Services.OrderService.CreateOrderAsync(MyApp.Contracts.CreateOrderRequest)"
    }
  ],
  "methodIds": ["…"]                    // 기존 필드 유지(실행 가능한 노드만)
}
```

**`members`와 `nodes`의 역할 분리**가 이번 스키마의 핵심이다.

- `members` = **화면에 그리는 모델**. 인터페이스 메서드처럼 본문이 없어 노드가 아닌 멤버도 들어간다.
- `nodes` = **실행 그래프**. 기존 정의 그대로(본문이 있는 멤버만).
- 둘을 잇는 것이 `member.nodeId`다. 멤버 줄을 클릭했을 때 `nodeId`가 있으면 호출 그래프·코드 패널로 이어지고, 없으면 선언 위치만 보여준다.

### 1.3 GraphNode (변경, A-4)

```jsonc
{
  "id": "…", "kind": "Service", "displayName": "OrderService.CreateOrderAsync",
  "namespace": "…", "classId": "…", "file": "…", "startLine": 31, "endLine": 79,
  "accessibility": "Public",
  "returnType": "Task<Order>",
  "parameterTypes": ["MyApp.Contracts.CreateOrderRequest"]
}
```

### 1.4 하위 호환 (A-5)

- `schemaVersion: 4`. v3 이하를 읽으면 `members`는 빈 배열, `baseTypeId`/`interfaceIds`는 null·빈 배열, `typeKind`는 `"Class"`로 채운다.
- 클라이언트는 `members.length === 0`인 클래스가 대부분이면 **"다시 분석해야 클래스 다이어그램을 그릴 수 있다"** 안내를 띄운다(빈 박스만 잔뜩 그리지 않는다).

### 1.5 설정 (A-6)

```jsonc
{
  "includeMembers": true,     // false면 members를 비워서 graph.json을 줄인다
  "includeInterfaces": true   // false면 인터페이스를 classes에서 제외(v3 동작)
}
```

---

## 2. Analyzer 구현 상세 (B)

대상 파일: `src/TraceLens.Analyzer/SolutionScanner.cs`, `Models/GraphClass.cs`, `Models/GraphNode.cs`, `NodeClassifier.cs`

### 2.1 인터페이스 수집 (B-1)

`SolutionScanner`가 지금은 `InterfaceDeclarationSyntax`를 건너뛴다. 이를 포함시키되 구분을 남긴다.

- `typeKind = Interface`, 기본 `kind = Interface`(B-6).
- 인터페이스 메서드는 **본문이 없으므로 노드로 만들지 않는다**. `members`에만 담는다.
- `obj`/`bin` 아래 자동 생성 소스 제외 규칙은 그대로 적용한다.

### 2.2 멤버 수집 (B-2, B-5)

`INamedTypeSymbol.GetMembers()`를 순회하며 아래 규칙으로 `GraphClassMember`를 만든다.

| 심볼 | memberKind | 비고 |
|---|---|---|
| `IFieldSymbol` (`IsImplicitlyDeclared == false`) | Field | 자동 프로퍼티 backing field는 `IsImplicitlyDeclared`로 걸러진다 |
| `IPropertySymbol` | Property | 자동 프로퍼티도 **멤버로는 포함**(노드는 여전히 본문 있는 것만) |
| `IMethodSymbol` (`MethodKind.Ordinary`) | Method | `get_`/`set_`/`add_`/`remove_` 접근자는 제외 |
| `IMethodSymbol` (`MethodKind.Constructor`, 명시 선언) | Constructor | 컴파일러 기본 생성자는 제외 |

- 줄 번호는 `symbol.DeclaringSyntaxReferences[0]`에서 얻고, 없으면 타입 선언 위치로 대체한다.
- partial 클래스는 심볼 하나에 모든 멤버가 모이므로 자연히 병합된다. 선언 위치는 첫 번째 `DeclaringSyntaxReferences` 기준(기존 규칙 유지).
- 정렬은 **선언 순서**가 아니라 `Field → Property → Constructor → Method`, 그 안에서는 소스 줄 번호 순으로 한다(다이어그램에서 읽기 좋다).

### 2.3 타입 문자열과 타입 id (B-3)

- 표시용 `type`은 `ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)` — `IReadOnlyList<Order>`처럼 짧게 나온다.
- `typeIds`는 그 타입에서 **솔루션 안에 선언된 타입만** 뽑아 배열로 담는다. 제네릭 인자도 재귀로 훑는다.
  - `Task<Order>` → `["MyApp.Domain.Order"]`
  - `Dictionary<string, Payment>` → `["MyApp.Domain.Payment"]`
  - `int`, `string`, `Guid` → `[]`
- 배열/nullable(`Order[]`, `Order?`)은 원소 타입으로 환원한다.

### 2.4 상속·구현 (B-4)

- `baseTypeId`: `symbol.BaseType`이 솔루션 안 타입이면 그 클래스 id, 아니면 null. `object`는 무시한다.
- `baseTypeName`: 외부 타입이어도 이름은 남긴다(`ControllerBase`, `DbContext`). 화면에서 "외부 상위 타입"으로 보여준다.
- `interfaceIds`: `symbol.Interfaces`(직접 구현만, `AllInterfaces` 아님) 중 솔루션 안 인터페이스.
  - 직접 구현만 쓰는 이유는 `AllInterfaces`를 쓰면 상위 인터페이스까지 선이 중복으로 그려지기 때문이다.

### 2.6 멤버 수집 비용 실측 (B-7)

사내 솔루션에는 이 환경에서 접근할 수 없어서, **이 저장소 자체(`TraceLens.sln`)** 와 테스트 픽스처를 대상으로 쟀다. 같은 방법(`tracelens.config.json`에 `includeMembers`를 켜고/끄고 `POST /api/analyze`를 반복)을 사내 솔루션에도 그대로 쓸 수 있다.

| 대상 | 클래스 | 멤버 | 분석 시간(멤버 켬) | 분석 시간(멤버 끔) | graph.json |
|---|---|---|---|---|---|
| TraceLens.sln | 83(인터페이스 5) | 397 | 3.3s · 4.0s | 2.7s · 3.1s | 450KB ↔ 283KB |
| Fixtures/FixtureApp.sln | 20(인터페이스 4) | 34 | 3.9s | — | 37KB |

- 멤버 수집 자체는 **5~18ms**다(서버 로그의 `멤버 N개를 Xms에 모았다`). 전체 분석 시간 차이(약 0.3~0.9s)는 대부분 직렬화·파일 쓰기와 실행마다 흔들리는 MSBuild 편차다.
- 비용은 시간이 아니라 **크기**다. `graph.json`이 283KB → 450KB로 약 1.6배가 된다. 그래서 500KB를 넘으면 서버가 `includeMembers`를 끄라고 경고 로그를 남기고, `/api/graph` 응답은 gzip으로 압축한다(450KB → 40KB).

### 2.5 테스트 (B-8)

`tests/TraceLens.Analyzer.Tests/Fixtures/FixtureApp`에 다음을 추가하고 검증한다.

- 인터페이스 1개 + 그 구현 1개 → `interfaceIds`가 채워지고 인터페이스가 `classes`에 뜬다
- 추상 기반 클래스 1개 + 파생 2개 → `baseTypeId`, `isAbstract`
- 필드로 인터페이스를 주입받는 서비스 → 멤버 `typeIds`에 인터페이스 id
- 자동 프로퍼티만 있는 record → `members`에는 프로퍼티가 있고 `nodes`에는 없다
- `includeMembers: false` → `members`가 빈 배열

---

## 3. Server 구현 상세 (C)

대상 파일: `src/TraceLens.Server/Endpoints/GraphEndpoints.cs`, `AnalyzeEndpoints.cs`

- **C-2** `/api/graph/classes` 요약에 `typeKind`, `memberCount`를 더한다(좌측 목록에서 인터페이스 아이콘 구분용).
- **C-3** `/api/graph/namespaces`를 새로 만든다. 좌측 네임스페이스 패널이 전체 그래프를 받기 전에도 그려지도록 하는 용도다.

```jsonc
[{ "namespace": "MyApp.Application.Services", "classCount": 4, "methodCount": 7,
   "kinds": ["Service", "Method"] }]
```

- **C-5** `members` 때문에 graph.json이 커진다. 로컬 전용이라 네트워크는 문제가 아니지만, 응답 크기를 로그로 남겨 임계치를 파악해 둔다(예: 500KB 넘으면 `includeMembers` 안내).

---

## 4. 클라이언트 공통 (D)

### 4.1 보기 전환 (D-1, D-2, D-3)

`App.tsx`의 상태에 `viewMode: 'callflow' | 'classmap' | 'flowmap'`를 둔다. 보기를 바꾸면 좌측 패널·가운데 캔버스·breadcrumb·상세 패널이 한 세트로 바뀐다.

| viewMode | 좌측 패널 | 가운데 |
|---|---|---|
| `callflow` | 진입점 목록 | 기존 단계식 호출 흐름(변경 없음) |
| `classmap` | 네임스페이스 목록(체크박스 + 검색) | 클래스 다이어그램 |
| `flowmap` | 전체 지도 + 진입점 목록(컨트롤러별) | 진입점 지도 / 진입점 흐름 |

기존 `진입점 | 클래스` 탭은 없앤다. 클래스 탭이 하던 1홉 관계도(`getClassRelationWindow`)는 **클래스 다이어그램의 "선택 1홉만" 토글**로 흡수하고, 상세 패널에 버튼을 남긴다.

### 4.2 3분할 스플리터 (D-4)

```css
.workspace{
  --col-left:272px; --col-right:392px;
  display:grid;
  grid-template-columns:var(--col-left) 8px minmax(360px,1fr) 8px var(--col-right);
  height:calc(100vh - 190px);
}
```

- 스플리터는 `pointerdown` → `setPointerCapture` → `pointermove`로 폭을 갱신한다.
- 가운데가 `MIN_CENTER`(360px) 아래로 내려가지 않도록 좌우 폭을 클램프한다. 창이 줄어들 때도 같은 클램프를 다시 돌린다.
- 더블클릭하면 기본 폭으로 되돌린다.
- React에서는 폭을 state가 아니라 **ref + CSS 변수 직접 수정**으로 다뤄야 드래그 중 리렌더가 안 생긴다. 드래그가 끝날 때만 state에 반영한다.

### 4.3 반응형 (D-5)

- 페이지 컨테이너의 `max-width`를 없애고 `height`를 `100vh` 기준으로 계산한다.
- `window.resize`를 120ms 디바운스로 받아 배치를 다시 계산한다. 패키지 배치와 "맞춤" 줌은 패널 폭에 의존하므로 이때 값이 바뀐다.
- 최소 폭은 1040px로 두고 그 아래는 가로 스크롤을 허용한다(로컬 개발 도구라 모바일 대응은 하지 않는다).

### 4.4 상태 저장 (D-6, D-7)

`localStorage`에 저장할 키: `viewMode`, `layout`, `treeDir`, `showFields`, `showMethods`, `rel`, `zoom`, `hiddenNs`, `depth`, 마지막 선택(`sel`). 재분석 후에는 id가 그대로 살아 있으면 선택을 복원하고, 사라졌으면 해당 보기의 기본 화면으로 되돌린다.

---

## 5. 클래스 다이어그램 (E)

새 파일 구성 제안:

```
client/src/graph/
  relations.ts      # E-1 관계 파생 (순수)
  treeLayout.ts     # E-3, E-4 계층 배치 (순수)
  packageLayout.ts  # E-6 패키지 배치 (순수)
client/src/components/
  DiagramCanvas.tsx # 배치 결과(박스+선)를 DOM/SVG로 그리는 공용 캔버스
  UmlClassBox.tsx   # E-2 UML 박스
  ClassDiagram.tsx  # 툴바 + 캔버스 조립
```

렌더러는 **Cytoscape가 아니라 직접 그린다**. UML 박스는 멤버 줄마다 클릭·강조가 필요한 HTML 구조라 캔버스 기반 Cytoscape 노드로는 표현이 어렵다. 기존 호출 흐름 보기(`GraphView.tsx`)는 Cytoscape 그대로 두고, 새 두 보기만 `DiagramCanvas`를 쓴다. 나중에 셋을 하나로 합칠지는 H 단계에서 판단한다.

### 5.1 관계 파생 (E-1)

서버는 관계를 계산하지 않는다. 토글을 켜고 끌 때 즉시 반응해야 하므로 클라이언트에서 파생한다.

```ts
type RelationType = 'inherit' | 'implement' | 'assoc' | 'depend'
interface ClassRelation { from: string; to: string; type: RelationType; label?: string }
```

1. **inherit** — `class.baseTypeId`가 있으면 `{ from: 클래스, to: 상위, type: 'inherit' }`
2. **implement** — `class.interfaceIds`의 각 항목
3. **assoc** — `class.members[].typeIds`의 각 항목(자기 자신 제외). `label`은 멤버 이름
4. **depend** — 기존 호출 엣지를 클래스 단위로 접은 것에서 **1~3에 이미 있는 (from,to) 쌍을 뺀 나머지**

4번의 차감이 중요하다. 필드로 들고 있으면서 호출도 하는 관계(대부분의 DI 주입)가 선 두 개로 겹쳐 그려지는 것을 막는다. 예제 데이터에서 호출 엣지 27개 중 의존으로 남는 것은 10개뿐이다.

### 5.2 UML 박스 (E-2)

3단 구성이고, 각 단은 토글로 접힌다.

```
┌──────────────────────────────┐
│ «interface» · Application.Ab…│  ← 캡션(스테레오타입 · 네임스페이스)
│ IOrderRepository             │  ← 이름(abstract면 기울임)
├──────────────────────────────┤
│ − _db : AppDbContext         │  ← 필드/프로퍼티
├──────────────────────────────┤
│ + AddAsync(Order) : Task     │  ← 메서드(클릭하면 그 노드 선택)
└──────────────────────────────┘
```

- 상단 테두리 색은 기존 `kind` 색을 그대로 쓴다(Controller/Service/Repository/…).
- 트리 배치에서는 패키지 박스가 없으므로 캡션에 네임스페이스를 적고, 패키지 배치에서는 생략한다.
- 박스 폭은 가장 긴 멤버 문자열로 계산하고 184~336px로 클램프, 넘치면 말줄임한다.
- 높이 = 캡션 유무(30/44) + 필드 단(6 + 줄수×16) + 메서드 단(6 + 줄수×16).

### 5.3 트리 배치 엔진 (E-3) — 이번 요청의 핵심

목업에서 선이 엉켰던 이유는 클래스를 네임스페이스 격자에 넣고 아무 데나 선을 이었기 때문이다. **Sugiyama 방식의 계층 배치**로 바꾸면 선이 대부분 한 방향으로 흐르고 교차가 크게 준다. 4단계로 구현한다.

**1) 층 나누기.** 관계를 방향 있는 그래프로 보고 최장 경로 기준으로 층을 매긴다.

- 상속·구현은 **방향을 뒤집어서** 층을 잡는다. 상위 타입이 위(또는 왼쪽)에 오게 하기 위해서다. 화살촉은 그대로 상위 타입 쪽에 붙인다.
- 순환은 위상 정렬(Kahn)로 처리하고, 정렬에서 남은 노드는 뒤에 붙여 임의 층을 준다. 이 노드로 들어가는 되돌아가는 선은 4)에서 따로 그린다.
- **관계가 하나도 없는 클래스는 마지막 층에 모아 둔다.** 안 그러면 층 0에 몰려 첫 줄이 지나치게 길어진다.

**2) 더미 삽입.** 층을 2개 이상 건너뛰는 선은 중간 층마다 폭 22px짜리 더미 노드를 넣고 그 점을 지나가게 한다. 이게 없으면 긴 선이 다른 박스를 가로질러 지나간다.

**3) 순서 정하기(바리센터).** 층마다 노드 순서를 바꿔가며 교차를 줄인다.

```
for it in 0..3:
  downward = (it % 2 == 0)
  for each layer (downward면 1..n, 아니면 n-1..0):
    각 노드의 정렬 키 = 인접 층에 있는 이웃들의 현재 인덱스 평균
    그 키로 층 안을 정렬하고 인덱스를 갱신
```

이웃 목록은 더미를 포함한 확장 그래프에서 만든다(더미도 정렬에 참여해야 긴 선이 곧게 펴진다).

**4) 좌표 배치.** 층은 진행축으로 쌓고, 층 안에서는 이웃 중심으로 당긴다.

```
진행축(main)  = LR이면 x, TB면 y   → 층마다 최대 크기 + 간격만큼 누적
교차축(cross) = LR이면 y, TB면 x
for it in 0..5:
  각 층에 대해: 원하는 위치 = 인접 층 이웃들의 교차축 중심 평균
    ① 왼쪽부터: pos = max(원하는 위치, 앞 노드 끝 + 간격)   (밀어내기)
    ② 오른쪽부터: 여유가 있으면 원하는 위치로 당긴다       (되당기기)
```

밀어내기만 하면 전부 왼쪽으로 쏠리고, 되당기기까지 해야 부모 아래로 자식이 가운데 정렬된다.

**5) 선 그리기(E-4).** 층을 따라가는 선은 `시작 박스의 진행축 끝면 → 더미들 → 도착 박스의 시작면`을 3차 베지어로 잇는다. 제어점은 진행축 방향으로만 뻗어서 세로/가로 흐름이 유지된다. 되돌아가는 선(순환)은 두 박스의 마주 보는 옆면을 잇는 기존 곡선을 쓴다.

**방향 기본값은 `→`(LR)다.** UML 박스는 세로보다 가로로 넓어서, 같은 데이터에서 목업 기준 LR은 2327×1738, TB는 3536×1204가 나온다. 패널이 가로로 긴 것을 감안하면 LR이 "맞춤" 배율에서 더 크게 보인다. 상속 위주로 볼 때는 TB가 자연스러우므로 토글로 남긴다.

### 5.4 밀도 조절 (E-7, E-10)

전체를 한 화면에 그리는 게 목적이므로 노드를 줄이는 대신 **보는 밀도를 조절하는 손잡이**를 둔다.

| 컨트롤 | 효과 |
|---|---|
| 멤버: 필드 / 메서드 | 끄면 박스 높이가 30~44px로 줄어 전체 면적이 1/3 이하가 된다 |
| 관계: 상속·구현·연관·의존 | 의존만 꺼도 선이 30% 줄어든다(기본값: 의존 꺼짐) |
| 선택 1홉만 | 선택 클래스와 직접 이어진 것만 남긴다 |
| 네임스페이스 체크박스 | 패키지 단위로 통째로 뺀다 |
| 줌 50/75/100/맞춤 | 맞춤은 전체가 화면에 들어오는 배율 |

**E-10**: 클래스가 150개를 넘으면 멤버 접힘 + 의존 끔 + 줌 맞춤으로 시작하고, "네임스페이스를 골라 좁혀 보라"는 안내를 한 줄 띄운다.

### 5.5 렌더 성능 실측 (H-4)

`TraceLens.sln`을 분석한 실제 `graph.json`(클래스 83·노드 209·엣지 273·멤버 397)으로 잰 값이다. 배치는 순수 함수라 Node에서 그대로 돌릴 수 있고, 렌더는 같은 컴포넌트를 `renderToStaticMarkup`으로 한 번 그린 시간이다.

| 단계 | 시간 |
|---|---|
| 관계 파생(`deriveRelations`) | 0.7ms |
| 트리 배치(`layoutTree`, LR) | 3.2ms |
| 패키지 배치(`layoutPackages`) | 7.7ms |
| 흐름도 배치(`layoutFlow`) | 1.9ms |
| 클래스 다이어그램 렌더(83박스 · 30선) | 22.4ms |

- 병목은 배치가 아니라 렌더다. 박스 하나가 DOM 요소 여러 개(캡션 + 멤버 줄)라서 멤버를 끄면 눈에 띄게 가벼워진다 — E-10의 기본값을 멤버 접힘으로 잡은 이유다.
- 기본 관계 설정(의존 끔)에서 이 저장소는 선이 30개뿐이다. 주 생성자를 많이 써서 필드가 거의 없기 때문이고, 필드 주입이 흔한 코드베이스에서는 연관 선이 훨씬 많아진다.
- 브라우저에서 픽스처(20 클래스)와 이 저장소(83 클래스)를 실제로 띄워 확인했다. 사내 솔루션(수백 클래스) 수치는 접근 권한이 없어 재지 못했다.

---

## 6. 전체 흐름도 (F)

### 6.1 전체 진입점 지도 (F-1)

- 컨트롤러 클래스는 박스 안에 **라우트를 줄로** 넣는다(`POST /api/orders` 형태, 클릭하면 그 진입점 흐름으로).
- 나머지 클래스는 이름 + 분류만 있는 작은 박스로 두고, 호출 엣지를 클래스 단위로 접어 잇는다.
- 배치는 클래스 다이어그램과 같은 계층 엔진을 재사용한다(더미·바리센터 포함).

### 6.2 진입점 흐름 (F-2, F-3, F-5)

- 선택한 진입점에서 BFS로 도달 깊이를 계산하고, `depth <= limit`인 메서드만 남긴다.
- 남은 메서드를 **클래스로 묶어 하나의 박스**로 만들고, 박스의 층은 그 안 메서드의 최소 깊이로 정한다.
- 선은 메서드 줄 ↔ 메서드 줄로 잇는다(어느 메서드가 어느 메서드를 부르는지가 보여야 한다).
- 깊이 제한에 걸려 안 보이는 하위 호출은 그 줄 오른쪽에 `+n`으로 남긴다.
- 방문 집합으로 순환을 막고, 자기 자신을 부르는 재귀는 줄에 표시만 한다.

---

## 7. 상세·질문 패널 (G)

- 선택 대상은 `{ type: 'class' | 'method' | 'namespace', key }` 세 종류다.
- 클래스 상세에는 관계 4종을 각각 칩 목록으로 보여주고, 칩을 누르면 그 클래스로 선택이 옮겨간다.
- 멤버 줄에 `nodeId`가 있으면 코드 패널이 그 메서드 본문을, 없으면(인터페이스 선언 등) 선언 줄을 보여준다.
- 질문 패널의 캡처 컨텍스트는 클래스/네임스페이스 선택에도 대응한다.

```
파일: src/MyApp.Application/Services/OrderService.cs
라인: 12-140
심볼: MyApp.Application.Services.OrderService
```

---

## 8. 완료 기준

검증은 **테스트 픽스처 솔루션**(`tests/TraceLens.Analyzer.Tests/Fixtures/FixtureApp.sln`, 클래스 20개·진입점 3개)과 **이 저장소 자체**(`TraceLens.sln`, 클래스 83개)를 브라우저에서 직접 띄워서 했다. 사내 솔루션에는 이 환경에서 접근할 수 없어 확인하지 못했다 — 아래 첫 항목은 픽스처 기준이다.

- [x] 솔루션을 분석하면 클래스 다이어그램에 인터페이스·상속·구현·필드 주입이 실제로 그려진다(픽스처에서 확인, 사내 솔루션 미검증)
- [x] 트리 배치에서 선이 한 방향으로 흐르고, 같은 데이터를 격자 배치로 볼 때보다 교차가 눈에 띄게 적다
- [x] 네임스페이스 체크박스·관계 토글·멤버 토글·줌이 즉시 반영된다
- [x] 전체 흐름도에서 라우트 줄을 누르면 그 진입점의 전 구간이 뜨고, 깊이 조절이 동작한다
- [x] 패널 경계를 드래그해 3분할 폭을 바꿀 수 있고, 창을 키우면 그만큼 넓게 보인다
- [x] 클래스 박스·멤버 줄을 클릭하면 기존 코드 패널·질문 패널이 그대로 동작한다
- [x] 재분석 후에도 보고 있던 보기와 선택이 유지된다

---

## 9. 확정한 것

| # | 정할 것 | 확정 |
|---|---|---|
| 1 | `CLAUDE.md` 제약 1번을 클래스 다이어그램에 한해 풀지 | **푼다.** 클래스 다이어그램·전체 흐름도만 전체를 그리고, 대신 밀도 조절 손잡이를 의무화했다(CLAUDE.md 제약 1번) |
| 2 | 필드에 기본 타입까지 보여줄지 | **전부 표시.** 다만 `typeIds`(연관 선)는 솔루션 안 타입만 담으므로 `Guid`·`string`은 선이 되지 않는다 |
| 3 | 관계 기본값 | 상속·구현·연관 켬, **의존 끔** |
| 4 | 트리 진행 방향 기본값 | `→`(LR) |
| 5 | 전체 흐름도 기본 깊이 | 3 |
| 6 | 대규모 솔루션 임계치와 그때의 기본값 | 클래스 **150개** 초과 시 멤버 접힘 + 의존 끔 + 줌 0.5로 시작하고 안내 한 줄 |
| 7 | 호출 흐름 보기를 새 캔버스로 통합할지 | **Cytoscape로 남긴다.** 단계식 윈도잉은 노드 수가 적어 자동 배치로 충분하고, 통합 이득이 작다 |
| 8 | 연관(assoc)을 어느 멤버에서 뽑을지 | **필드·프로퍼티만.** 메서드 반환 타입까지 넣으면 DTO로 향하는 선이 폭발하고, 그 관계는 의존(호출)으로 이미 표현된다 |

### 남은 이야깃거리

- 관계가 하나도 없는 클래스는 마지막 층에 한 줄로 모인다. 이 저장소(83개 중 53개가 그렇다)에서는 그 줄이 세로로 길어져서 "맞춤" 줌이 0.25까지 내려간다. 격자로 접어 넣는 편이 나은지는 사내 솔루션에서 더 써 보고 판단한다.
- `analysisScope: entryPoints`에서는 도달 못 한 메서드도 `member.nodeId`를 그대로 들고 있다(기존 `methodIds`와 같은 규칙). 클라이언트는 노드를 못 찾으면 선언 줄로 대신 보여준다.
