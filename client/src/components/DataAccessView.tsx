import { useMemo } from 'react'
import {
  buildAccessMatrix,
  buildEntryDataFlow,
  buildFailGrid,
  compensationLabels,
  filterFindings,
  severityLabels,
} from '../graph/dataView'
import type { DataViewState } from '../state/viewState'
import type {
  DataAccess,
  DataAccessDocument,
  EntryPoint,
  Finding,
  FindingSeverity,
  GraphDocument,
  Selection,
  WritePath,
} from '../types/graph'

interface DataAccessViewProps {
  graph: GraphDocument
  data: DataAccessDocument
  entryPoints: EntryPoint[]
  view: DataViewState
  onChangeView: (patch: Partial<DataViewState>) => void
  selection: Selection | null
  onSelect: (selection: Selection) => void
}

export function SeverityBadge({ severity, muted }: { severity: FindingSeverity; muted?: boolean }) {
  return (
    <span className={`sev ${muted ? 'muted' : severity.toLowerCase()}`}>{severityLabels[severity]}</span>
  )
}

/** 저장소 배지. 데이터소스 이름과 읽기/쓰기를 한 줄에 보여준다. */
function AccessTags({ access }: { access: DataAccess }) {
  const operation = access.operation === 'Read' ? 'read' : access.operation === 'Delete' ? 'write' : 'write'
  return (
    <>
      <span className={`tag ${access.storage === 'Cache' ? 'redis' : 'shard'}`}>{access.dataSource}</span>
      <span className={`tag ${operation}`}>{access.operation}</span>
      {access.inTransaction && <span className="tag tx">트랜잭션</span>}
      {access.inLoop && <span className="tag write">반복문</span>}
    </>
  )
}

export function DataAccessView({
  graph,
  data,
  entryPoints,
  view,
  onChangeView,
  selection,
  onSelect,
}: DataAccessViewProps) {
  const findings = useMemo(
    () => filterFindings(data.findings, { severity: view.severity, onlyCertain: view.onlyCertain }),
    [data.findings, view.severity, view.onlyCertain])
  const findingById = useMemo(() => new Map(data.findings.map((item) => [item.id, item])), [data.findings])

  if (view.sub === 'route') {
    return <RouteView graph={graph} entryPoints={entryPoints} view={view} onChangeView={onChangeView} onSelect={onSelect} />
  }

  if (view.sub === 'map') {
    return <MapView data={data} view={view} onChangeView={onChangeView} selection={selection} onSelect={onSelect} />
  }

  if (view.sub === 'tx') {
    return <TransactionView data={data} onSelect={onSelect} />
  }

  if (view.sub === 'comp') {
    return (
      <CompensationView
        data={data}
        view={view}
        onChangeView={onChangeView}
        findingById={findingById}
        onSelect={onSelect}
      />
    )
  }

  return <FindingTable findings={findings} selection={selection} onSelect={onSelect} />
}

/* ------------------------------------------------------------------ 실행 경로 */

function RouteView({
  graph,
  entryPoints,
  view,
  onChangeView,
  onSelect,
}: {
  graph: GraphDocument
  entryPoints: EntryPoint[]
  view: DataViewState
  onChangeView: (patch: Partial<DataViewState>) => void
  onSelect: (selection: Selection) => void
}) {
  const entryNodeId = view.entryNodeId ?? entryPoints[0]?.nodeId ?? null
  const entry = entryPoints.find((item) => item.nodeId === entryNodeId) ?? null
  const steps = useMemo(
    () => (entryNodeId ? buildEntryDataFlow(graph, entryNodeId) : []),
    [graph, entryNodeId])

  if (!entry) {
    return <div className="panel-message">진입점을 고르면 그 요청이 건드리는 저장소를 순서대로 보여준다.</div>
  }

  const dataSources = [...new Set(steps.flatMap((step) => step.accesses.map((access) => access.dataSource)))]

  return (
    <div className="data-view">
      <div className="route-head">
        <span className={`http ${entry.httpMethod.toLowerCase()}`}>{entry.httpMethod}</span>
        <h3>{entry.route}</h3>
        <span className="route-path">저장소에 닿는 메서드 {steps.length}곳</span>
        <span className="route-tags">
          {dataSources.map((name) => <span className="tag shard" key={name}>{name}</span>)}
        </span>
      </div>

      {steps.length === 0 && (
        <div className="panel-message">이 진입점에서는 DB·캐시 접근을 찾지 못했다.</div>
      )}

      {steps.map((step, index) => (
        <div key={step.nodeId}>
          <div className="stage">
            <div className="stage-label">{index === 0 ? '진입점' : `${step.depth}단계`}</div>
            <div className="stage-cards">
              <button
                type="button"
                className="card"
                onClick={() => onSelect({ type: 'method', key: step.nodeId })}
              >
                <div className="card-top">
                  <span className="card-title">{step.displayName}</span>
                  <span className="tag">{step.accesses.length}곳</span>
                </div>
                <div className="loc">{step.file}</div>
              </button>
              {step.accesses.map((access) => (
                <button
                  type="button"
                  key={access.id}
                  className="card sub-card"
                  onClick={() => onSelect({ type: 'storageTarget', key: access.target })}
                >
                  <div className="card-top">
                    <span className="card-title">{access.target || access.statement}</span>
                    <AccessTags access={access} />
                  </div>
                  <div className="sql">{access.statement}</div>
                  <div className="loc">{access.file}:{access.line}</div>
                </button>
              ))}
            </div>
          </div>
          {index < steps.length - 1 && <div className="arrow">↓</div>}
        </div>
      ))}

      <div className="view-hint">
        호출 그래프를 따라 6단계까지 훑는다. 인터페이스 호출은 DI 등록으로 구현체를 찾아 잇는다.
        <button type="button" className="btn sm" onClick={() => onChangeView({ sub: 'comp' })}>
          이 코드베이스의 쓰기 경로 보기
        </button>
      </div>
    </div>
  )
}

/* ------------------------------------------------------------------ 접근 지도 */

function MapView({
  data,
  view,
  onChangeView,
  selection,
  onSelect,
}: {
  data: DataAccessDocument
  view: DataViewState
  onChangeView: (patch: Partial<DataViewState>) => void
  selection: Selection | null
  onSelect: (selection: Selection) => void
}) {
  const matrix = useMemo(() => buildAccessMatrix(data, view.mapFilter), [data, view.mapFilter])
  const cacheKeys = useMemo(() => {
    const keys = new Map<string, { key: string; operations: Set<string>; users: Set<string> }>()
    for (const access of data.accesses) {
      if (access.storage !== 'Cache' || !access.target) continue
      const entry = keys.get(access.target) ?? { key: access.target, operations: new Set(), users: new Set() }
      entry.operations.add(access.statement.split(' ')[0])
      entry.users.add(access.classId.split('.').pop() ?? access.classId)
      keys.set(access.target, entry)
    }

    return [...keys.values()]
  }, [data.accesses])

  const filters: { key: DataViewState['mapFilter']; label: string }[] = [
    { key: 'all', label: '전체' },
    { key: 'write', label: '쓰기만' },
    { key: 'flagged', label: '걸린 것만' },
  ]

  return (
    <div className="data-view">
      <div className="grid-title">
        클래스 · 저장 대상
        <span className="grid-filters">
          {filters.map((filter) => (
            <button
              type="button"
              key={filter.key}
              className={`chipbtn${view.mapFilter === filter.key ? ' active' : ''}`}
              onClick={() => onChangeView({ mapFilter: filter.key })}
            >
              {filter.label}
            </button>
          ))}
        </span>
      </div>

      {matrix.rows.length === 0 ? (
        <div className="panel-message">거른 조건에 맞는 접근이 없다.</div>
      ) : (
        <div className="matrix-wrap">
          <table className="matrix">
            <thead>
              <tr>
                <th style={{ minWidth: 190 }}>클래스 · 대상</th>
                {matrix.targets.map((target) => (
                  <th key={target.name}>
                    {target.name}
                    <small>{target.dataSource}</small>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {matrix.rows.map((row) => (
                <tr key={row.classId}>
                  <th>{row.className}</th>
                  {matrix.targets.map((target) => {
                    const cell = row.cells[target.name]
                    if (!cell) return <td key={target.name}>·</td>
                    const className = `cell ${cell.operations.toLowerCase()}${cell.findingId ? ' flag' : ''}`
                    return (
                      <td key={target.name}>
                        {cell.findingId ? (
                          <button
                            type="button"
                            className={className}
                            onClick={() => onSelect({ type: 'finding', key: cell.findingId! })}
                          >
                            {cell.operations}
                          </button>
                        ) : (
                          <button
                            type="button"
                            className={className}
                            onClick={() => onSelect({ type: 'storageTarget', key: target.name })}
                          >
                            {cell.operations}
                          </button>
                        )}
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="matrix-legend">
        <span><span className="cell r">R</span> 읽기</span>
        <span><span className="cell w">W</span> 쓰기</span>
        <span><span className="cell d">D</span> 삭제</span>
        <span><span className="cell r flag">R</span> 규칙에 걸린 칸 — 누르면 상세로</span>
      </div>

      {cacheKeys.length > 0 && (
        <>
          <div className="grid-title" style={{ marginTop: 18 }}>캐시 키 패턴 · 쓰는 곳</div>
          <div className="keylist">
            {cacheKeys.map((item) => (
              <button
                type="button"
                className="keyrow"
                key={item.key}
                onClick={() => onSelect({ type: 'storageTarget', key: item.key })}
              >
                <span className="k">{item.key}</span>
                <span className="tag redis">{[...item.operations].join(' · ')}</span>
                <span className="u">{[...item.users].join(' · ')}</span>
              </button>
            ))}
          </div>
        </>
      )}

      {selection?.type === 'storageTarget' && (
        <div className="view-hint">선택한 대상: {selection.key}</div>
      )}
    </div>
  )
}

/* ------------------------------------------------------------------ 트랜잭션 */

function TransactionView({
  data,
  onSelect,
}: {
  data: DataAccessDocument
  onSelect: (selection: Selection) => void
}) {
  if (data.transactions.length === 0) {
    return <div className="panel-message">트랜잭션을 여는 코드를 찾지 못했다. 쓰기 경로는 “보상 처리”에서 본다.</div>
  }

  return (
    <div className="data-view">
      {data.transactions.map((transaction) => {
        const tone = transaction.dataSources.length > 1
          ? 'bad'
          : transaction.externalCalls.length > 0 || transaction.cacheCalls.length > 0 ? 'warn' : 'ok'
        return (
          <div className={`tx-card ${tone}`} key={transaction.id}>
            <div className="tx-head">
              <button
                type="button"
                className="name"
                onClick={() => onSelect({ type: 'method', key: transaction.methodNodeId })}
              >
                {transaction.methodDisplayName}
              </button>
              <span className="loc">{transaction.file}:{transaction.startLine}</span>
              {!transaction.hasCommit && <span className="sev warn">커밋 호출 없음</span>}
            </div>
            <div className="tx-steps">
              <div className="tx-step conn">
                커넥션 {transaction.dataSources.length}개
                <span className="meta">{transaction.dataSources.join(' · ') || '확인 못 함'}</span>
              </div>
              {transaction.externalCalls.map((call) => (
                <div className="tx-step ext" key={call}>외부 호출<span className="meta">{call}</span></div>
              ))}
              {transaction.cacheCalls.map((call) => (
                <div className="tx-step query" key={call}>캐시 호출<span className="meta">{call}</span></div>
              ))}
              <div className="tx-step commit">
                {transaction.hasCommit ? 'CommitAsync()' : '커밋 호출을 찾지 못했다'}
              </div>
            </div>
            {transaction.dataSources.length > 1 && (
              <div className="tx-flag bad">
                커넥션이 둘 이상이라 한쪽만 반영될 수 있다 — TX-003
              </div>
            )}
            {transaction.externalCalls.length > 0 && (
              <div className="tx-flag warn">커밋 전에 외부 응답을 기다린다 — TX-001</div>
            )}
            {tone === 'ok' && (
              <div className="tx-flag ok">커넥션 1개 · 외부 호출 없음 — 이 모양이 기준이다</div>
            )}
          </div>
        )
      })}
    </div>
  )
}

/* ------------------------------------------------------------------ 보상 처리 */

function CompensationView({
  data,
  view,
  onChangeView,
  findingById,
  onSelect,
}: {
  data: DataAccessDocument
  view: DataViewState
  onChangeView: (patch: Partial<DataViewState>) => void
  findingById: Map<string, Finding>
  onSelect: (selection: Selection) => void
}) {
  if (data.writePaths.length === 0) {
    return <div className="panel-message">쓰기를 두 번 이상 하는 경로를 찾지 못했다.</div>
  }

  const openId = view.pathId ?? data.writePaths.find((path) => !path.inTransaction)?.id ?? data.writePaths[0].id

  return (
    <div className="data-view">
      {data.writePaths.map((path) => {
        const open = path.id === openId
        const worst = worstSeverity(path, findingById)
        return (
          <div className={`path-card ${path.inTransaction ? 'ok' : worst === 'Critical' ? 'bad' : 'warn'}`} key={path.id}>
            <button
              type="button"
              className="path-head"
              onClick={() => {
                onChangeView({ pathId: open ? null : path.id })
                onSelect({ type: 'writePath', key: path.id })
              }}
            >
              <span className="name">{path.methodDisplayName}</span>
              <span className="loc">{path.file}:{path.startLine}</span>
              <span className="path-tags">
                {path.inTransaction
                  ? <span className="tag tx">트랜잭션</span>
                  : <span className="tag write">트랜잭션 없음</span>}
                <span className="tag">쓰기 {path.steps.length}단계</span>
                {worst && <SeverityBadge severity={worst} />}
                <span className="chipbtn">{open ? '접기' : '펼치기'}</span>
              </span>
            </button>
            <div className="path-note">{path.note}</div>
            {open && <PathDetail path={path} findingById={findingById} onSelect={onSelect} />}
          </div>
        )
      })}
    </div>
  )
}

function PathDetail({
  path,
  findingById,
  onSelect,
}: {
  path: WritePath
  findingById: Map<string, Finding>
  onSelect: (selection: Selection) => void
}) {
  const grid = useMemo(() => buildFailGrid(path), [path])

  return (
    <>
      <div className="wsteps">
        {path.steps.map((step) => {
          const bad = step.compensation === 'No' || step.compensation === 'PartialPaths' || step.compensation === 'Irreversible'
          const tone = step.compensation === 'Yes' || step.compensation === 'Transaction'
            ? 'yes'
            : step.compensation === 'NotNeeded' ? 'na' : step.compensation === 'No' ? 'no' : 'never'
          return (
            <div className={`wstep${bad ? ' bad' : ''}`} key={step.order}>
              <span className="idx">{step.order}</span>
              <span className="body">
                <span className="t">{step.label}</span>
                <span className="m">{step.detail}</span>
                <span className="m note">{step.compensationNote}</span>
              </span>
              <span className={`comp ${tone}`}>{compensationLabels[step.compensation]}</span>
            </div>
          )
        })}
      </div>

      {path.inTransaction ? (
        <div className="tx-flag ok">
          모든 쓰기가 한 트랜잭션 안에 있다 — 실패하면 함께 롤백되므로 보상 격자가 필요 없다.
        </div>
      ) : (
        <>
          <div className="grid-title">실패 지점 × 단계 — 그때 저장소에 남는 것</div>
          <table className="failgrid">
            <thead>
              <tr>
                <th style={{ minWidth: 130 }}>실패 지점</th>
                {path.steps.map((step) => (
                  <th key={step.order}>{step.order}. {shorten(step.label)}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {grid.map((row, index) => (
                <tr key={path.steps[index].order}>
                  <th>{index + 1}번에서 실패</th>
                  {row.map((cell, cellIndex) => (
                    <td key={path.steps[cellIndex].order}>
                      <span className={`fc ${cell.kind}`}>{cell.label}</span>
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
          <div className="failgrid-legend">
            <span><span className="fc stays">남는다</span> 되돌리는 코드를 찾지 못했다</span>
            <span><span className="fc undone">되돌림</span> 실패 경로에서 역연산을 찾았다</span>
            <span><span className="fc harmless">무해</span> 재실행해도 안전하다</span>
            <span><span className="fc notrun">미실행</span> 아직 실행 전</span>
          </div>
        </>
      )}

      {path.findingIds.length > 0 && (
        <div className="detail-actions" style={{ marginTop: 10 }}>
          {path.findingIds.map((id) => {
            const finding = findingById.get(id)
            if (!finding) return null
            return (
              <button type="button" className="btn sm" key={id} onClick={() => onSelect({ type: 'finding', key: id })}>
                {finding.ruleId} 상세
              </button>
            )
          })}
        </div>
      )}
    </>
  )
}

/* ------------------------------------------------------------------ 점검 결과 */

export function FindingTable({
  findings,
  selection,
  onSelect,
}: {
  findings: Finding[]
  selection: Selection | null
  onSelect: (selection: Selection) => void
}) {
  if (findings.length === 0) {
    return <div className="panel-message">거른 조건에 맞는 항목이 없다.</div>
  }

  return (
    <div className="data-view">
      <table className="findings">
        <thead>
          <tr>
            <th>규칙</th>
            <th>심각도</th>
            <th>내용</th>
            <th>위치</th>
            <th>확실도</th>
          </tr>
        </thead>
        <tbody>
          {findings.map((finding) => (
            <tr
              key={finding.id}
              className={`row${selection?.type === 'finding' && selection.key === finding.id ? ' selected' : ''}`}
              onClick={() => onSelect({ type: 'finding', key: finding.id })}
            >
              <td className="rule">{finding.ruleId}</td>
              <td><SeverityBadge severity={finding.severity} /></td>
              <td className="what">{finding.what}</td>
              <td className="where">{finding.file}:{finding.line}</td>
              <td className="conf">{finding.confidence === 'Certain' ? '확실' : '추정'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function worstSeverity(path: WritePath, findingById: Map<string, Finding>): FindingSeverity | null {
  let worst: FindingSeverity | null = null
  for (const id of path.findingIds) {
    const severity = findingById.get(id)?.severity
    if (!severity) continue
    if (severity === 'Critical') return 'Critical'
    if (severity === 'Warning' || worst === null) worst = severity
  }

  return worst
}

const shorten = (value: string) => (value.length <= 16 ? value : `${value.slice(0, 16)}…`)
