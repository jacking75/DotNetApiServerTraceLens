# 6단계 — DB · 캐시 보기와 미들웨어 보기

이 문서는 6단계의 범위와 체크리스트다. 화면 모양은 `docs/db-view-mockup.html`, `docs/middleware-view-mockup.html`에서 확정했고
여기서는 **무엇을 어떻게 뽑아내는지**와 **어디까지 잡고 어디부터 못 잡는지**를 적는다.
0~5단계는 `task.md`, `task2.md`를 본다.

## 0. 왜 필요한가

호출 흐름·클래스 다이어그램은 "코드가 서로를 어떻게 부르는가"를 보여준다. 실무에서 사고가 나는 자리는 그 아래다.

- 트랜잭션 없이 저장소를 여러 번 바꾸고, **중간에 실패하면 앞의 것을 사람이 되돌려야 하는데** 그 코드가 빠져 있다.
- 캐시를 먼저 갱신하고 DB가 실패해서 둘이 어긋난다.
- 미들웨어 순서가 틀려 인증이 적용되지 않거나, 등록만 하고 실행되지 않는 미들웨어가 있다.

이 세 가지를 눈으로 확인할 수 있게 하는 것이 6단계다.

## 1. 범위 — 확정한 것

| # | 정할 것 | 확정 |
|---|---|---|
| 1 | 샤딩 | **선택.** `dataSources`에 `"kind": "shard"`가 하나라도 있을 때만 SHARD 규칙군과 "샤드 키" 단계가 켜진다. 설정이 없으면 꺼진 채로 나머지가 그대로 동작한다 |
| 2 | Redis | **저장소로 함께 본다.** 키를 패턴(`user:{userNo}:inv`)으로 정규화해 테이블과 같은 자리에 놓는다 |
| 3 | 보상 처리 | **트랜잭션이 없는 쓰기 경로만** 격자를 만든다. 트랜잭션 안에 있으면 "트랜잭션이 처리"로 끝낸다 |
| 4 | 보상 판정 | 실패 경로(`catch` · 조기 `return`)에서 **역연산**을 찾는다. 짝은 설정(`compensation.pairs`) + 이름 규칙(Insert↔Delete, Add↔Remove, Increase↔Decrease, Consume↔Restore, Set↔Remove) |
| 5 | 미들웨어 | 시작 흐름이 이미 읽은 `app.UseXxx()` 순서를 그대로 쓰고, 사용자 미들웨어는 `InvokeAsync` 본문을 열어 `_next` 앞뒤로 나눈다 |
| 6 | 단락(short-circuit) | `_next`를 부르지 않고 끝나는 분기 + 그 분기가 넣는 `StatusCode` 상수 |
| 7 | 보기 위치 | 상단 메뉴 다섯째 `DB · 캐시`, 여섯째 `미들웨어` |
| 8 | 규모 손잡이 | 접근 지도는 "쓰기만 / 걸린 것만 / 전체" 세 가지와 데이터소스 필터. 점검 결과는 심각도·확실도·무시 필터 |

## 2. 데이터 계약 (스키마 v6)

`graph.json`에 두 섹션이 추가된다. v5 이하 문서를 읽으면 둘 다 `null`이다.

```jsonc
{
  "schemaVersion": 6,
  "dataAccess": {
    "shardingEnabled": false,
    "dataSources": [{ "name": "GameDb", "kind": "Single", "shardCount": 0, "readOnly": false, "accessCount": 12 }],
    "accesses": [{
      "id": "a1", "storage": "Database", "dataSource": "GameDb", "target": "user_item",
      "operation": "Write", "statement": "UPDATE user_item SET ...",
      "methodNodeId": "...", "methodDisplayName": "ItemRepository.AddItemAsync",
      "classId": "...", "file": "...", "line": 71, "inTransaction": true, "inLoop": false
    }],
    "transactions": [{ "id": "t1", "methodNodeId": "...", "dataSources": ["GameDb", "LogDb"], "hasCommit": true, "externalCalls": ["HttpClient.PostAsync"] }],
    "writePaths": [{
      "id": "p1", "methodNodeId": "...", "inTransaction": false,
      "steps": [{ "order": 1, "label": "...", "compensation": "Yes", "compensationNote": "catch에서 RestoreAsync 호출" }],
      "findingIds": ["f3"]
    }],
    "findings": [{ "id": "f3", "ruleId": "COMP-002", "severity": "Critical", "confidence": "Certain",
                   "what": "...", "why": "...", "fix": "...", "evidence": [{ "tone": "bad", "head": "...", "detail": "...", "note": "..." }] }],
    "rules": [{ "id": "COMP-002", "group": "보상", "name": "...", "severity": "Critical", "judge": "Certain", "shardOnly": false, "enabled": true }]
  },
  "middleware": {
    "programFile": "src/Api/Program.cs",
    "steps": [{
      "id": "m3", "order": 3, "name": "SessionValidationMiddleware", "type": "Custom",
      "registrationFile": "Program.cs", "registrationLine": 63, "classId": "...", "implementationFile": "...", "implementationLine": 26,
      "pre": "...", "post": null, "shortCircuit": { "statusCode": "401", "condition": "세션 없음", "line": 39 },
      "contextAccesses": [{ "mode": "Write", "target": "Items[\"UserNo\"]" }],
      "storeAccesses": ["Redis · GET session:{token}"],
      "isTerminal": false, "isDead": false, "condition": null, "filters": []
    }],
    "findings": [ ... ], "rules": [ ... ]
  }
}
```

설정(`tracelens.config.json`)에 추가되는 부분:

```jsonc
{
  "includeDataAccess": true,
  "includeMiddleware": true,
  "db": {
    "queryApis": ["Dapper.SqlMapper.*"],
    "cacheApis": ["StackExchange.Redis.IDatabase.*"],
    "connectionFactories": ["*.IShardConnectionFactory.OpenAsync"],
    "shardKeyParameters": ["userNo", "uid"],
    "dataSources": {
      "GameDb": { "kind": "shard", "shardCount": 8, "typePatterns": ["*ShardConnectionFactory"] },
      "LogDb":  { "kind": "single", "writeOnly": true, "typePatterns": ["*LogConnectionFactory"] }
    },
    "compensation": {
      "pairs": [{ "do": "AddItemAsync", "undo": "RemoveItemAsync" }],
      "irreversible": ["*PushClient.SendAsync"],
      "idempotent": ["*Cache.RemoveAsync"]
    }
  }
}
```

설정이 하나도 없어도 동작한다. 그때는 SQL 리터럴·캐시 타입 이름·팩토리 이름에서 이름을 지어낸다(샤딩은 꺼짐).

## 3. 어떻게 뽑아내나

### 3.1 원시 접근(primitive) 수집
- 호출식의 인자에 **SQL로 보이는 문자열**(`SELECT|INSERT|UPDATE|DELETE|MERGE`로 시작)이 있으면 DB 접근으로 본다. 동사에서 읽기/쓰기, `FROM`/`INTO`/`UPDATE`/`JOIN` 뒤에서 테이블 이름을 뽑는다.
- 설정한 `queryApis`에 걸리는 호출도 DB 접근으로 본다(프로시저 이름만 넘기는 래퍼용).
- 캐시는 타입 이름(`*Cache`, `IDatabase`, `IDistributedCache`)이나 설정으로 판정하고, 메서드 이름에서 명령(GET/SET/DEL/INCR/SET NX)을 정한다. 키는 보간 문자열을 `{식별자}`로 정규화한다.
- 커넥션은 `Open*`/`Create*Connection` 호출에서 잡고, 인자에 있는 식별자를 **샤드 키**로 기억한다.

### 3.2 전파(propagate)
서비스 코드는 SQL을 직접 쓰지 않고 리포지터리를 부른다. 그래서 원시 접근을 **호출 그래프를 따라 위로 전파**해
"이 메서드를 부르면 결국 무엇을 읽고 쓰는가"를 메서드마다 요약한다(깊이 6, 순환 방지).

### 3.3 쓰기 경로와 보상 판정
한 메서드 안에서 **상태를 바꾸는 호출**(전파 요약에 쓰기가 있는 호출 + 캐시 쓰기 + 되돌릴 수 없는 외부 호출)을 소스 순서로 모은다.
그 수가 2개 이상이고 전부가 한 트랜잭션 안에 있지 않으면 **쓰기 경로**가 된다. 단계마다:

| 판정 | 뜻 |
|---|---|
| `Transaction` | 트랜잭션 안 — 실패하면 함께 롤백된다 |
| `Yes` | 실패 경로 전부에서 역연산을 찾았다 |
| `PartialPaths` | `catch`에는 있는데 조기 반환 경로에는 없다(= COMP-006) |
| `No` | 역연산을 찾지 못했다 |
| `NotNeeded` | 재실행해도 안전(캐시 무효화 · 로그 쓰기) |
| `Irreversible` | 취소 수단이 없다(푸시·메일·결제) |

화면의 격자는 이 판정으로 만든다 — `j < i`면 `Yes/Transaction`은 "되돌림", `No/PartialPaths/Irreversible`은 "남는다".

### 3.4 미들웨어
- 순서는 시작 흐름이 읽은 `app.*` 호출 순서를 그대로 쓴다. `UseMiddleware<T>()`와 `app.Use(...)` 인라인 모두 잡는다.
- 사용자 미들웨어는 `InvokeAsync`/`Invoke` 본문에서 `await _next(...)` 위치를 찾아 앞이 "요청 시", 뒤가 "응답 시"다.
- `_next`를 부르지 않고 끝나는 분기를 단락으로 보고, 그 분기 안의 `StatusCode = 401` 상수를 응답 코드로 읽는다.
- `context.Items[...]`·`Response.Headers[...]`·`context.User` 접근을 읽기/쓰기로 모은다.
- 터미널(`MapControllers`/`MapGet`/`UseEndpoints`) 뒤에 등록된 미들웨어는 **실행되지 않음**으로 표시한다.

## 4. 규칙 카탈로그 (이번 단계에서 구현하는 것)

| 그룹 | 규칙 |
|---|---|
| 샤딩(샤딩 켤 때만) | `SHARD-001` 전 샤드 순회 · `SHARD-002` 커넥션 유저 ≠ 쿼리 유저(추정) · `SHARD-004` 하드코딩 샤드 번호 · `SHARD-005` 샤드 키 출처 미검증(추정) |
| 트랜잭션 | `TX-001` 트랜잭션 안 외부 호출 · `TX-003` 여러 커넥션에 걸침 |
| 보상 | `COMP-001` 트랜잭션 없이 쓰기 2개 이상 · `COMP-002` 되돌리지 않는 선행 쓰기 · `COMP-003` 보상 실패 무시 · `COMP-005` 되돌릴 수 없는 작업 뒤 실패 가능 쓰기 · `COMP-006` 조기 반환 경로에 보상 없음 |
| 캐시 | `CACHE-002` 캐시 먼저 갱신 후 DB 실패 방치 · `CACHE-003` TTL 없는 SET · `CACHE-004` KEYS/FLUSH · `CACHE-005` 반복문 안 GET · `CACHE-007` 트랜잭션 안 캐시 호출 |
| 커넥션 | `CONN-001` using 없는 커넥션 · `CONN-002` DbContext/커넥션 Singleton 등록 |
| 읽기·쓰기 | `RW-001` 읽기 전용 데이터소스에 쓰기 |
| 성능 | `PERF-001` 반복문 안 쿼리 · `PERF-002` `.Result`/`.Wait()` |
| 보안 | `SEC-001` 문자열 결합 SQL |
| 미들웨어 | `MW-001` 예외 처리 위치 · `MW-002` 인증·인가·라우팅 순서 · `MW-003` UseCors 위치 · `MW-004` 생성자 Scoped 주입 · `MW-005` next도 응답도 없는 분기 · `MW-006` 응답 시작 후 헤더 수정 · `MW-008` 동기 대기 · `MW-012` 실행되지 않는 미들웨어 |

**이번 단계에서 넣지 않는 것**(목업에는 있었지만 정적으로 판단이 어렵거나 스키마 파일이 필요해 뒤로 미룬다):
`TX-002`, `CACHE-001`, `CACHE-006`, `CACHE-008`, `COMP-004`, `RW-002`, `IDX-*`, `MAP-*`, `MW-007`, `MW-009`, `MW-010`, `MW-011`.
화면의 규칙 목록에는 이들도 `enabled: false`로 함께 실어 "아직 검사하지 않는다"를 분명히 보여준다.

## 5. 못 하는 것 — 화면에 그대로 적는다

- 런타임 값으로 갈리는 분기(`if (_options.Enabled)`)는 조건부로만 표시하고 값은 모른다.
- 샤드 번호는 런타임에 정해진다. "어떤 키로 골랐는가"까지만 확정한다.
- 리플렉션·동적 SQL 조립은 따라가지 않는다.
- 확신이 없는 판정은 `Confidence.Estimated`로 남기고 화면에서 "추정"으로 표시한다. "확실한 것만" 토글로 접을 수 있다.

## 6. 체크리스트

### A. Analyzer
- [x] A-1 `Models/DataAccess.cs`, `Models/MiddlewarePipeline.cs`, `Models/Findings.cs` 추가
- [x] A-2 `TraceLensConfig`에 `includeDataAccess`/`includeMiddleware`/`db` 섹션 추가(무설정 기본값 포함)
- [x] A-3 `DataAccessScanner` — 원시 접근 수집 + 메서드별 구조 정보(트랜잭션·루프·using·catch)
- [x] A-4 호출 그래프를 따라 접근 요약 전파
- [x] A-5 `CompensationAnalyzer` — 쓰기 경로와 단계별 보상 판정
- [x] A-6 `DataRuleEngine` — 4장의 DB·캐시 규칙
- [x] A-7 `MiddlewareScanner` — 파이프라인 단계·단락·컨텍스트 접근 + MW 규칙
- [x] A-8 `GraphDocument` 스키마 v6, `TraceLensAnalyzer` 배선, DI 등록

### B. Server
- [x] B-1 `/api/graph`가 새 섹션을 그대로 실어 보낸다(별도 엔드포인트 없음)
- [x] B-2 분석 요약에 점검 건수 추가

### C. Client
- [x] C-1 타입·`normalizeGraph`·`viewState`에 두 보기 추가
- [x] C-2 `ViewToolbar`에 `DB · 캐시`, `미들웨어` 추가
- [x] C-3 `DataAccessView` — 실행 경로 · 접근 지도 · 트랜잭션 · 보상 처리 · 점검 결과
- [x] C-4 `MiddlewareView` — 파이프라인 · 단락 지점 · 점검 결과
      (목업의 "엔드포인트별 적용" 매트릭스는 뺐다. 조건부 등록(`UseWhen`·`if`)이 없으면 모든 칸이 ●로 같아서
      실제 데이터에서는 아무것도 알려주지 않는다. 조건은 파이프라인 카드의 "조건부" 배지로 보여준다.)
- [x] C-5 좌측 패널(진입점/데이터소스/테이블·키/점검, 파이프라인/진입점/점검)
- [x] C-6 상세 패널·질문 패널 연결(선택 종류 4개 추가)
- [x] C-7 목업 CSS를 `styles.css`로 이식

### D. 테스트·검증
- [x] D-1 `FixtureDataApp` 픽스처 솔루션 추가(보상 누락·캐시 선행·트랜잭션 걸침·미들웨어 순서 문제 포함)
- [x] D-2 Analyzer 통합 테스트(쓰기 경로·보상 판정·규칙별 검출)
- [x] D-3 미들웨어 파이프라인 테스트(순서·단락·죽은 미들웨어)
- [x] D-4 클라이언트 순수 함수 테스트(격자 계산)
- [x] D-5 브라우저로 픽스처와 이 저장소를 띄워 확인

### E. 이번 단계에서 하지 않은 것
- 점검 항목 "의도된 것으로 표시"(`.tracelens/db-baseline.json`)는 넣지 않았다. 규칙별 확실도 표시와 "확실한 것만" 토글로
  먼저 소음을 줄여 보고, 실제로 오탐이 남는지 본 뒤에 붙인다.
- 목업의 샤딩 on/off 토글은 화면에서 바꾸는 것이 아니라 설정에서 읽은 값을 보여주기만 한다(구성 칩).

## 7. 완료 기준

- [x] 픽스처 솔루션을 분석하면 보상 처리 보기에 쓰기 경로가 뜨고, 격자의 빨간 칸이 실제 코드의 누락과 일치한다
- [x] 샤딩 설정을 켜고 끄면 SHARD 규칙군과 "샤드 키" 단계가 함께 켜지고 꺼진다
- [x] 미들웨어 보기에서 순서 문제·단락·실행되지 않는 미들웨어가 표시된다
- [x] 점검 항목을 고르면 근거·코드·제안이 뜨고, 질문 패널로 그대로 이어진다
- [x] 재분석 후에도 보고 있던 보기와 선택이 유지된다
