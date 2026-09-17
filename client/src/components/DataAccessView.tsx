import { useMemo } from 'react'
import {
  buildAccessMatrix,
  buildEntryDataFlow,
  buildFailGrid,
  compensationLabelKeys,
  filterFindings,
  severityLabelKeys,
} from '../graph/dataView'
import { useI18n } from '../i18n/I18nProvider'
import type { TranslationKey } from '../i18n/ko'
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
  const { t } = useI18n()
  return (
    <span className={`sev ${muted ? 'muted' : severity.toLowerCase()}`}>{t(severityLabelKeys[severity])}</span>
  )
}

/** 저장소 배지. 데이터소스 이름과 읽기/쓰기를 한 줄에 보여준다. */
function AccessTags({ access }: { access: DataAccess }) {
  const { t } = useI18n()
  const operation = access.operation === 'Read' ? 'read' : access.operation === 'Delete' ? 'write' : 'write'
  return (
    <>
      <span className={`tag ${access.storage === 'Cache' ? 'redis' : 'shard'}`}>{access.dataSource}</span>
      <span className={`tag ${operation}`}>{access.operation}</span>
      {access.inTransaction && <span className="tag tx">{t('data.inTransaction')}</span>}
      {access.inLoop && <span className="tag write">{t('data.inLoop')}</span>}
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
  const { t } = useI18n()
  const entryNodeId = view.entryNodeId ?? entryPoints[0]?.nodeId ?? null
  const entry = entryPoints.find((item) => item.nodeId === entryNodeId) ?? null
  const steps = useMemo(
    () => (entryNodeId ? buildEntryDataFlow(graph, entryNodeId) : []),
    [graph, entryNodeId])

  if (!entry) {
    return <div className="panel-message">{t('data.pickEntryPoint')}</div>
  }

  const dataSources = [...new Set(steps.flatMap((step) => step.accesses.map((access) => access.dataSource)))]

  return (
    <div className="data-view">
      <div className="route-head">
        <span className={`http ${entry.httpMethod.toLowerCase()}`}>{entry.httpMethod}</span>
        <h3>{entry.route}</h3>
        <span className="route-path">{t('data.methodsTouchingStore', { count: steps.length })}</span>
        <span className="route-tags">
          {dataSources.map((name) => <span className="tag shard" key={name}>{name}</span>)}
        </span>
      </div>

      {steps.length === 0 && (
        <div className="panel-message">{t('data.noAccessForEntry')}</div>
      )}

      {steps.map((step, index) => (
        <div key={step.nodeId}>
          <div className="stage">
            <div className="stage-label">{index === 0 ? t('data.entryStage') : t('data.depthStage', { depth: step.depth })}</div>
            <div className="stage-cards">
              <button
                type="button"
                className="card"
                onClick={() => onSelect({ type: 'method', key: step.nodeId })}
              >
                <div className="card-top">
                  <span className="card-title">{step.displayName}</span>
                  <span className="tag">{t('data.siteCount', { count: step.accesses.length })}</span>
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
        {t('data.routeHint')}
        <button type="button" className="btn sm" onClick={() => onChangeView({ sub: 'comp' })}>
          {t('data.seeWritePaths')}
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
  const { t } = useI18n()
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

  const filters: { key: DataViewState['mapFilter']; labelKey: TranslationKey }[] = [
    { key: 'all', labelKey: 'data.filter.all' },
    { key: 'write', labelKey: 'data.filter.write' },
    { key: 'flagged', labelKey: 'data.filter.flagged' },
  ]

  return (
    <div className="data-view">
      <div className="grid-title">
        {t('data.mapTitle')}
        <span className="grid-filters">
          {filters.map((filter) => (
            <button
              type="button"
              key={filter.key}
              className={`chipbtn${view.mapFilter === filter.key ? ' active' : ''}`}
              onClick={() => onChangeView({ mapFilter: filter.key })}
            >
              {t(filter.labelKey)}
            </button>
          ))}
        </span>
      </div>

      {matrix.rows.length === 0 ? (
        <div className="panel-message">{t('data.noMatchingAccess')}</div>
      ) : (
        <div className="matrix-wrap">
          <table className="matrix">
            <thead>
              <tr>
                <th style={{ minWidth: 190 }}>{t('data.classTargetHeader')}</th>
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
        <span><span className="cell r">R</span> {t('data.legendRead')}</span>
        <span><span className="cell w">W</span> {t('data.legendWrite')}</span>
        <span><span className="cell d">D</span> {t('data.legendDelete')}</span>
        <span><span className="cell r flag">R</span> {t('data.legendFlagged')}</span>
      </div>

      {cacheKeys.length > 0 && (
        <>
          <div className="grid-title" style={{ marginTop: 18 }}>{t('data.cacheKeyTitle')}</div>
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
        <div className="view-hint">{t('data.selectedTarget', { target: selection.key })}</div>
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
  const { t } = useI18n()
  if (data.transactions.length === 0) {
    return <div className="panel-message">{t('data.noTransactions')}</div>
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
              {!transaction.hasCommit && <span className="sev warn">{t('data.noCommit')}</span>}
            </div>
            <div className="tx-steps">
              <div className="tx-step conn">
                {t('data.connectionCount', { count: transaction.dataSources.length })}
                <span className="meta">{transaction.dataSources.join(' · ') || t('data.unknownSource')}</span>
              </div>
              {transaction.externalCalls.map((call) => (
                <div className="tx-step ext" key={call}>{t('data.externalCall')}<span className="meta">{call}</span></div>
              ))}
              {transaction.cacheCalls.map((call) => (
                <div className="tx-step query" key={call}>{t('data.cacheCall')}<span className="meta">{call}</span></div>
              ))}
              <div className="tx-step commit">
                {transaction.hasCommit ? 'CommitAsync()' : t('data.commitNotFound')}
              </div>
            </div>
            {transaction.dataSources.length > 1 && (
              <div className="tx-flag bad">{t('data.txMultiConnection')}</div>
            )}
            {transaction.externalCalls.length > 0 && (
              <div className="tx-flag warn">{t('data.txExternal')}</div>
            )}
            {tone === 'ok' && (
              <div className="tx-flag ok">{t('data.txOk')}</div>
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
  const { t } = useI18n()
  if (data.writePaths.length === 0) {
    return <div className="panel-message">{t('data.noWritePaths')}</div>
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
                  ? <span className="tag tx">{t('data.inTransaction')}</span>
                  : <span className="tag write">{t('data.noTransaction')}</span>}
                <span className="tag">{t('data.writeSteps', { count: path.steps.length })}</span>
                {worst && <SeverityBadge severity={worst} />}
                <span className="chipbtn">{open ? t('data.collapse') : t('data.expand')}</span>
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
  const { t } = useI18n()
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
              <span className={`comp ${tone}`}>{t(compensationLabelKeys[step.compensation])}</span>
            </div>
          )
        })}
      </div>

      {path.inTransaction ? (
        <div className="tx-flag ok">{t('data.allInTransaction')}</div>
      ) : (
        <>
          <div className="grid-title">{t('data.failGridTitle')}</div>
          <table className="failgrid">
            <thead>
              <tr>
                <th style={{ minWidth: 130 }}>{t('data.failPoint')}</th>
                {path.steps.map((step) => (
                  <th key={step.order}>{step.order}. {shorten(step.label)}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {grid.map((row, index) => (
                <tr key={path.steps[index].order}>
                  <th>{t('data.failsAt', { order: index + 1 })}</th>
                  {row.map((cell, cellIndex) => (
                    <td key={path.steps[cellIndex].order}>
                      <span className={`fc ${cell.kind}`}>{t(cell.labelKey)}</span>
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
          <div className="failgrid-legend">
            <span><span className="fc stays">{t('data.cell.stays')}</span> {t('data.legendStays')}</span>
            <span><span className="fc undone">{t('data.cell.undone')}</span> {t('data.legendUndone')}</span>
            <span><span className="fc harmless">{t('data.cell.harmless')}</span> {t('data.legendHarmless')}</span>
            <span><span className="fc notrun">{t('data.cell.notrun')}</span> {t('data.legendNotRun')}</span>
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
                {t('data.findingDetail', { rule: finding.ruleId })}
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
  const { t } = useI18n()
  if (findings.length === 0) {
    return <div className="panel-message">{t('data.noMatchingFinding')}</div>
  }

  return (
    <div className="data-view">
      <table className="findings">
        <thead>
          <tr>
            <th>{t('data.colRule')}</th>
            <th>{t('data.colSeverity')}</th>
            <th>{t('data.colWhat')}</th>
            <th>{t('data.colWhere')}</th>
            <th>{t('data.colConfidence')}</th>
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
              <td className="conf">{finding.confidence === 'Certain' ? t('data.certain') : t('data.estimated')}</td>
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
