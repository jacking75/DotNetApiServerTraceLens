import { useMemo, useState } from 'react'
import { dataSourceTone } from '../graph/dataView'
import type { EntryGroupBy, EntrySort } from '../graph/entryGroups'
import type { DataViewState } from '../state/viewState'
import type {
  DataAccessDocument,
  EntryPoint,
  FindingSeverity,
  MiddlewarePipelineDocument,
  RuleInfo,
  Selection,
} from '../types/graph'
import { useI18n } from '../i18n/I18nProvider'
import { ruleText } from '../i18n/ruleText'
import { EntryPointList } from './EntryPointList'
import { MiddlewareStepRow } from './MiddlewareView'

type DataTab = 'entries' | 'sources' | 'targets' | 'rules'

interface DataSidePanelProps {
  data: DataAccessDocument
  entryPoints: EntryPoint[]
  view: DataViewState
  onChangeView: (patch: Partial<DataViewState>) => void
  selection: Selection | null
  onSelect: (selection: Selection) => void
  classIdOfMethod?: (nodeId: string) => string | undefined
  entrySort: EntrySort
  onChangeEntrySort: (sort: EntrySort) => void
  entryGroupBy: EntryGroupBy
}

export function DataSidePanel({
  data,
  entryPoints,
  view,
  onChangeView,
  selection,
  onSelect,
  classIdOfMethod,
  entrySort,
  onChangeEntrySort,
  entryGroupBy,
}: DataSidePanelProps) {
  const [tab, setTab] = useState<DataTab>('entries')
  const [search, setSearch] = useState('')

  const targets = useMemo(() => {
    const map = new Map<string, { name: string; dataSource: string; storage: string; count: number }>()
    for (const access of data.accesses) {
      if (!access.target) continue
      const entry = map.get(access.target)
        ?? { name: access.target, dataSource: access.dataSource, storage: access.storage, count: 0 }
      entry.count += 1
      map.set(access.target, entry)
    }

    return [...map.values()].sort((left, right) => left.name.localeCompare(right.name))
  }, [data.accesses])

  const ruleCounts = useMemo(() => {
    const counts = new Map<string, number>()
    for (const finding of data.findings) counts.set(finding.ruleId, (counts.get(finding.ruleId) ?? 0) + 1)
    return counts
  }, [data.findings])

  const { t, lang } = useI18n()
  /** DB·캐시 보기는 노드 ID로 고른다. 같은 노드의 엔드포인트 중 첫 번째를 선택 표시로 쓴다. */
  const selectedEndpointId = view.entryNodeId
    ? entryPoints.find((item) => item.nodeId === view.entryNodeId)?.endpointId ?? null
    : null
  const keyword = search.trim().toLowerCase()
  const matches = (value: string) => keyword.length === 0 || value.toLowerCase().includes(keyword)

  return (
    <>
      <div className="side-tabs">
        <button type="button" className={tab === 'entries' ? 'active' : ''} onClick={() => setTab('entries')}>
          {t('side.tab.entries')}
        </button>
        <button type="button" className={tab === 'sources' ? 'active' : ''} onClick={() => setTab('sources')}>
          {t('side.tab.sources')}
        </button>
        <button type="button" className={tab === 'targets' ? 'active' : ''} onClick={() => setTab('targets')}>
          {t('side.tab.targets')}
        </button>
        <button type="button" className={tab === 'rules' ? 'active' : ''} onClick={() => setTab('rules')}>
          {t('side.tab.rules')}
        </button>
      </div>
      {tab === 'entries' && (
        <EntryPointList
          entryPoints={entryPoints}
          selectedEndpointId={selectedEndpointId}
          onSelect={(entry) => {
            onChangeView({ sub: 'route', entryNodeId: entry.nodeId })
            onSelect({ type: 'method', key: entry.nodeId })
          }}
          emptyMessage={t('side.entryPointsEmpty')}
          classIdOfMethod={classIdOfMethod}
          sort={entrySort}
          onChangeSort={onChangeEntrySort}
          groupBy={entryGroupBy}
        />
      )}
      {tab !== 'entries' && (
      <>
      <input
        className="side-search"
        value={search}
        onChange={(event) => setSearch(event.target.value)}
        placeholder={t('side.search')}
      />
      <div className="side-list">
        {tab === 'sources' && data.dataSources.filter((source) => matches(source.name)).map((source) => (
          <button
            type="button"
            key={source.name}
            className={`side-item${selection?.type === 'dataSource' && selection.key === source.name ? ' selected' : ''}`}
            onClick={() => onSelect({ type: 'dataSource', key: source.name })}
          >
            <span className={`ds-dot ${dataSourceTone(source.kind)}`} />
            <span className="grow">
              {source.name}
              <span className="sub">
                {source.description}
                {source.readOnly ? t('side.readOnly') : ''}
                {source.writeOnly ? t('side.writeOnly') : ''}
              </span>
            </span>
            <span className="count">{source.accessCount}</span>
          </button>
        ))}

        {tab === 'targets' && targets.filter((target) => matches(target.name)).map((target) => (
          <button
            type="button"
            key={target.name}
            className={`side-item${selection?.type === 'storageTarget' && selection.key === target.name ? ' selected' : ''}`}
            onClick={() => onSelect({ type: 'storageTarget', key: target.name })}
          >
            <span className={`ds-dot ${target.storage === 'Cache' ? 'redis' : 'shard'}`} />
            <span className="grow">
              {target.name}
              <span className="sub">{t('side.accessCount', { source: target.dataSource, count: target.count })}</span>
            </span>
          </button>
        ))}

        {tab === 'rules' && (
          <RuleList
            rules={data.rules.filter((rule) => matches(`${rule.id} ${rule.name} ${ruleText(rule, lang).name}`))}
            counts={ruleCounts}
            selection={selection}
            onSelect={onSelect}
          />
        )}
      </div>
      </>
      )}
    </>
  )
}

interface MiddlewareSidePanelProps {
  pipeline: MiddlewarePipelineDocument
  selection: Selection | null
  onSelect: (selection: Selection) => void
}

export function MiddlewareSidePanel({ pipeline, selection, onSelect }: MiddlewareSidePanelProps) {
  const { t } = useI18n()
  const [tab, setTab] = useState<'pipeline' | 'rules'>('pipeline')
  const severityByStep = useMemo(() => {
    const map = new Map<string, FindingSeverity>()
    for (const finding of pipeline.findings) {
      if (!finding.stepId) continue
      const current = map.get(finding.stepId)
      if (current === 'Critical') continue
      map.set(finding.stepId, finding.severity)
    }

    return map
  }, [pipeline.findings])
  const ruleCounts = useMemo(() => {
    const counts = new Map<string, number>()
    for (const finding of pipeline.findings) counts.set(finding.ruleId, (counts.get(finding.ruleId) ?? 0) + 1)
    return counts
  }, [pipeline.findings])

  return (
    <>
      <div className="side-tabs two">
        <button type="button" className={tab === 'pipeline' ? 'active' : ''} onClick={() => setTab('pipeline')}>
          {t('side.tab.pipeline')}
        </button>
        <button type="button" className={tab === 'rules' ? 'active' : ''} onClick={() => setTab('rules')}>
          {t('side.tab.rules')}
        </button>
      </div>
      <div className="side-list">
        {tab === 'pipeline' && pipeline.steps.map((step) => (
          <MiddlewareStepRow
            key={step.id}
            step={step}
            selected={selection?.type === 'middlewareStep' && selection.key === step.id}
            severity={severityByStep.get(step.id) ?? null}
            onSelect={() => onSelect({ type: 'middlewareStep', key: step.id })}
          />
        ))}
        {tab === 'rules' && (
          <RuleList rules={pipeline.rules} counts={ruleCounts} selection={selection} onSelect={onSelect} />
        )}
      </div>
    </>
  )
}

function RuleList({
  rules,
  counts,
  selection,
  onSelect,
}: {
  rules: RuleInfo[]
  counts: Map<string, number>
  selection: Selection | null
  onSelect: (selection: Selection) => void
}) {
  const { t, lang } = useI18n()
  const hit = rules.filter((rule) => (counts.get(rule.id) ?? 0) > 0)
  const clean = rules.filter((rule) => (counts.get(rule.id) ?? 0) === 0 && rule.enabled)
  const off = rules.filter((rule) => !rule.enabled)

  const row = (rule: RuleInfo, dim: boolean, label: string) => (
    <button
      type="button"
      key={rule.id}
      className={`side-item${selection?.type === 'rule' && selection.key === rule.id ? ' selected' : ''}`}
      style={dim ? { opacity: 0.55 } : undefined}
      onClick={() => onSelect({ type: 'rule', key: rule.id })}
    >
      <span className="grow">
        <span className="rule-id">{rule.id}</span>
        <span className="sub">{ruleText(rule, lang).name}</span>
      </span>
      <span className="count">{label}</span>
    </button>
  )

  return (
    <>
      {hit.length > 0 && <div className="side-group">{t('side.rulesHit')}</div>}
      {hit.map((rule) => row(rule, false, String(counts.get(rule.id))))}
      {clean.length > 0 && <div className="side-group">{t('side.rulesClean')}</div>}
      {clean.map((rule) => row(rule, true, '0'))}
      {off.length > 0 && <div className="side-group">{t('side.rulesOff')}</div>}
      {off.map((rule) => row(rule, true, rule.shardOnly ? t('side.ruleShardOnly') : t('side.ruleOff')))}
    </>
  )
}
