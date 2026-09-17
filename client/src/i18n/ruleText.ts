import type { RuleInfo } from '../types/graph'
import type { Language } from './I18nProvider'

/**
 * 규칙 카탈로그의 영어 문구.
 *
 * 규칙 이름·설명은 분석기가 그래프에 한국어로 적어 보낸다. 분석기에는 로케일이 없고, 로케일을 넣으면
 * 같은 코드가 언어에 따라 다른 graph.json이 되어 스냅샷 비교가 흔들린다. 그래서 **카탈로그 42개만**
 * 규칙 ID로 대응하는 영어 문구를 클라이언트에 두고, 영어로 볼 때 그 자리에서 바꿔 끼운다.
 *
 * 개별 점검 결과(what · why · fix)는 여기에 없다. 그 문장은 분석할 때 실제 심볼·테이블 이름·줄 번호를
 * 섞어 만들기 때문에 미리 번역해 둘 수 없다. 영어 화면에서도 점검 결과 본문은 분석기가 만든 그대로
 * 나온다 — 번역한 척하지 않는다.
 */
interface RuleEnglish {
  group: string
  name: string
  description: string
}

const english: Readonly<Record<string, RuleEnglish>> = {
  'SHARD-001': {
    group: 'Sharding',
    name: 'Iterates every shard with no shard key',
    description: 'Opens a connection inside a loop and runs the same query on every shard. May be intentional for an aggregate.',
  },
  'SHARD-002': {
    group: 'Sharding',
    name: 'Connection user differs from the queried user',
    description: 'Opens the connection with userNo A and passes a different number as a parameter. If they land on different shards, that data is not on this connection.',
  },
  'SHARD-004': {
    group: 'Sharding',
    name: 'Hard-coded shard number',
    description: 'Passes a shard number as a constant. Outside an operations tool this is almost always a mistake.',
  },
  'SHARD-005': {
    group: 'Sharding',
    name: 'Shard key origin is not verified',
    description: 'Uses a value from the request body or query string without checking it against the authenticated identity.',
  },
  'TX-001': {
    group: 'Transaction',
    name: 'External call inside a transaction',
    description: 'Awaits an external call such as HTTP before committing. Lock hold time becomes tied to another service’s response time.',
  },
  'TX-002': {
    group: 'Transaction',
    name: 'Path with neither commit nor rollback',
    description: 'An exception path leaves the scope without committing or rolling back.',
  },
  'TX-003': {
    group: 'Transaction',
    name: 'One transaction spans several connections',
    description: 'Another connection is opened inside the boundary. Without a distributed transaction only one side may be applied.',
  },
  'COMP-001': {
    group: 'Compensation',
    name: 'Two or more writes with no transaction',
    description: 'Writes that could be grouped are scattered. This is the list of paths whose compensation is worth reading.',
  },
  'COMP-002': {
    group: 'Compensation',
    name: 'Earlier write is not undone on failure',
    description: 'If a later step fails the earlier one stays. No inverse operation was found on the failure path.',
  },
  'COMP-003': {
    group: 'Compensation',
    name: 'Compensation failure is ignored or masked by an exception',
    description: 'The undo call runs without a try. An exception during compensation buries the original exception too.',
  },
  'COMP-004': {
    group: 'Compensation',
    name: 'Compensation is not in reverse order',
    description: 'Undoing from the last action backwards is the safe order.',
  },
  'COMP-005': {
    group: 'Compensation',
    name: 'A failable write after an irreversible action',
    description: 'Does something that cannot be cancelled — a push, a payment — and only then performs a write that can fail.',
  },
  'COMP-006': {
    group: 'Compensation',
    name: 'Early-return path has no compensation',
    description: 'The catch block undoes the work, but the path that returns a value does not.',
  },
  'CACHE-001': {
    group: 'Cache',
    name: 'No cache invalidation after a DB write',
    description: 'Something reads a cache key holding the same data, but the write path never invalidates it.',
  },
  'CACHE-002': {
    group: 'Cache',
    name: 'Cache updated first, DB failure not undone',
    description: 'The cache changes first; if the DB write that follows fails, the two stay out of sync.',
  },
  'CACHE-003': {
    group: 'Cache',
    name: 'SET without a TTL',
    description: 'Keys with no expiry pile up. For a lock this becomes a permanent lock. Calls whose key and expiry cannot be settled at this site (through a cache wrapper declared in the solution) are not checked.',
  },
  'CACHE-004': {
    group: 'Cache',
    name: 'Full-scan commands such as KEYS or FLUSH',
    description: 'Blocks a production Redis.',
  },
  'CACHE-005': {
    group: 'Cache',
    name: 'Cache read inside a loop',
    description: 'Round trips grow with the iteration count. This is where MGET belongs.',
  },
  'CACHE-006': {
    group: 'Cache',
    name: 'Distributed lock is not released on some path',
    description: 'A lock taken with SET NX is not released on the exception path.',
  },
  'CACHE-007': {
    group: 'Cache',
    name: 'Cache call inside a transaction',
    description: 'Waits for a cache round trip while a DB transaction is open.',
  },
  'CACHE-008': {
    group: 'Cache',
    name: 'Inconsistent key naming',
    description: 'Keys pointing at the same data are built in two different shapes.',
  },
  'CONN-001': {
    group: 'Connection',
    name: 'Connection opened without using',
    description: 'An IDisposable is opened without a using and no Dispose call is visible.',
  },
  'CONN-002': {
    group: 'Connection',
    name: 'DbContext or connection registered as Singleton',
    description: 'Read straight from the DI registration in the startup flow. Concurrent requests would share one instance.',
  },
  'RW-001': {
    group: 'Read/write',
    name: 'Write to a read-only data source',
    description: 'Runs INSERT/UPDATE/DELETE against a data source marked readOnly in the config.',
  },
  'RW-002': {
    group: 'Read/write',
    name: 'Plain lookup sent to the primary',
    description: 'Points out read-only paths that could move to a replica.',
  },
  'PERF-001': {
    group: 'Performance',
    name: 'Query inside a loop (N+1)',
    description: 'Runs a query inside a loop.',
  },
  'PERF-002': {
    group: 'Performance',
    name: 'Async API called through .Result / .Wait()',
    description: 'Blocks the thread pool. Under load this leads to connection starvation.',
  },
  'SEC-001': {
    group: 'Schema & security',
    name: 'SQL built by string concatenation',
    description: 'Builds SQL by concatenation or interpolation rather than parameters. Statements composed through a builder chain are not raw SQL and are not checked here.',
  },
  'IDX-001': {
    group: 'Schema & security',
    name: 'Lookup on a column with no index',
    description: 'Can only be checked when schema.sql is read alongside the code.',
  },
  'MAP-001': {
    group: 'Schema & security',
    name: 'Stored procedure parameter mismatch',
    description: 'The procedure definition in the DDL and the parameters the code passes do not match.',
  },
  'MW-001': {
    group: 'Order',
    name: 'Exception-handling middleware is not near the front',
    description: 'Exceptions from middleware registered before the one that catches them are not caught.',
  },
  'MW-002': {
    group: 'Order',
    name: 'Authentication, authorization and routing are out of order',
    description: 'Departing from UseRouting → UseAuthentication → UseAuthorization means the policy is not applied.',
  },
  'MW-003': {
    group: 'Order',
    name: 'UseCors is in the wrong place',
    description: 'Placed before UseRouting, per-endpoint CORS policies do not apply.',
  },
  'MW-004': {
    group: 'Configuration',
    name: 'Scoped service injected into a middleware constructor',
    description: 'Middleware is built once as a Singleton. A Scoped service taken through the constructor freezes at the first request.',
  },
  'MW-005': {
    group: 'Behaviour',
    name: 'Branch that neither calls next nor writes a response',
    description: 'The request ends quietly and an empty 200 goes out.',
  },
  'MW-006': {
    group: 'Behaviour',
    name: 'Headers modified after the response started',
    description: 'Touches Response.Headers after next. If sending has already begun this throws.',
  },
  'MW-007': {
    group: 'Behaviour',
    name: 'Request body re-read without buffering',
    description: 'Request.Body can only be read once.',
  },
  'MW-008': {
    group: 'Behaviour',
    name: 'Synchronous wait inside middleware',
    description: 'Every request passes through here, so this leads to thread-pool starvation.',
  },
  'MW-009': {
    group: 'Configuration',
    name: 'A light endpoint passes through every heavy middleware',
    description: 'Health checks and metrics go through authentication and session lookups as well.',
  },
  'MW-010': {
    group: 'Behaviour',
    name: 'Swallows an exception and turns it into a normal response',
    description: 'The catch only logs and returns 200.',
  },
  'MW-011': {
    group: 'Configuration',
    name: 'UseWhen condition does not match any route',
    description: 'No endpoint corresponds to the conditional path.',
  },
  'MW-012': {
    group: 'Order',
    name: 'Registered but never executed middleware',
    description: 'Registered after a terminal (UseEndpoints, app.Run(delegate)) so it is not in the execution order. An endpoint registration such as MapControllers is not a terminal, so middleware after it does run.',
  },
}

/**
 * 화면에 쓸 규칙 문구. 한국어면 분석기가 준 값을 그대로 쓰고, 영어면 위 표로 바꿔 끼운다.
 * 표에 없는 규칙 ID(분석기가 새 규칙을 더한 경우)는 분석기 문구를 그대로 둔다 — 빈칸으로 만들지 않는다.
 */
export function ruleText(rule: RuleInfo, language: Language): RuleEnglish {
  if (language !== 'en') return { group: rule.group, name: rule.name, description: rule.description }
  return english[rule.id] ?? { group: rule.group, name: rule.name, description: rule.description }
}

/** 영어 문구가 준비된 규칙 ID. 카탈로그가 늘어났는지 테스트에서 확인한다. */
export const translatedRuleIds: readonly string[] = Object.keys(english)
