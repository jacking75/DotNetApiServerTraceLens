import { compensationLabelKeys, dataSourceTone, severityLabelKeys } from '../graph/dataView'
import { useI18n } from '../i18n/I18nProvider'
import { ruleText } from '../i18n/ruleText'
import { SourceLink, type SourceLocation } from './SourceLink'
import type {
  DataAccessDocument,
  Finding,
  GraphDocument,
  MiddlewarePipelineDocument,
  Selection,
} from '../types/graph'

interface DataDetailPanelProps {
  graph: GraphDocument
  selection: Selection
  onSelect: (selection: Selection) => void
  onShowFlow: (nodeId: string) => void
  /** `파일:줄`을 누르면 코드 보기를 그 위치로 옮긴다. */
  onOpenSource: (location: SourceLocation) => void
  /** Shift+클릭·컨텍스트 메뉴로 에디터에서 연다. */
  onOpenInEditor: (location: SourceLocation) => void
}

/**
 * DB·캐시와 미들웨어 보기에서 고른 항목의 상세.
 * 점검 항목은 "무엇이 걸렸나 → 판정 근거 → 왜 문제인가 → 제안" 순서로 보여준다.
 */
export function DataDetailPanel({
  graph,
  selection,
  onSelect,
  onShowFlow,
  onOpenSource,
  onOpenInEditor,
}: DataDetailPanelProps) {
  const { t, lang } = useI18n()
  const data = graph.dataAccess
  const pipeline = graph.middleware

  if (selection.type === 'finding') {
    const finding = findFinding(data, pipeline, selection.key)
    if (!finding) return <div className="panel-message">{t('dd.findingMissing')}</div>
    return (
      <FindingDetail
        finding={finding}
        data={data}
        pipeline={pipeline}
        onSelect={onSelect}
        onShowFlow={onShowFlow}
        onOpenSource={onOpenSource}
        onOpenInEditor={onOpenInEditor}
      />
    )
  }

  if (selection.type === 'writePath') {
    const path = data?.writePaths.find((item) => item.id === selection.key)
    if (!path) return <div className="panel-message">{t('dd.pathMissing')}</div>
    return (
      <div className="detail-body">
        <div className="detail-title">
          {path.inTransaction
            ? <span className="sev ok">{t('dd.ok')}</span>
            : <span className="sev critical">{t('dd.checkCompensation')}</span>}
          <h3>{path.methodDisplayName}</h3>
        </div>
        <div className="detail-path">
          <SourceLink
            file={path.file}
            line={path.startLine}
            onOpen={onOpenSource}
            onOpenInEditor={onOpenInEditor}
          />
        </div>
        <Section title={t('dd.verdict')}><p>{path.note}</p></Section>
        <Section title={t('dd.stepByStep')}>
          <div className="chain">
            {path.steps.map((step) => (
              <div
                className={`chain-step ${step.compensation === 'No' || step.compensation === 'PartialPaths' || step.compensation === 'Irreversible' ? 'bad' : step.compensation === 'NotNeeded' ? '' : 'ok'}`}
                key={step.order}
              >
                {step.order}. {step.label}
                <span className="note mono">{t(compensationLabelKeys[step.compensation])}</span>
                <span className="note">{step.compensationNote}</span>
              </div>
            ))}
          </div>
        </Section>
        <div className="detail-actions">
          <button type="button" className="btn sm" onClick={() => onShowFlow(path.methodNodeId)}>
            {t('dd.showInCallFlow')}
          </button>
        </div>
      </div>
    )
  }

  if (selection.type === 'dataSource') {
    const source = data?.dataSources.find((item) => item.name === selection.key)
    if (!source) return <div className="panel-message">{t('dd.sourceMissing')}</div>
    const accesses = data?.accesses.filter((access) => access.dataSource === source.name) ?? []
    return (
      <div className="detail-body">
        <div className="detail-title">
          <span className={`ds-dot ${dataSourceTone(source.kind)}`} />
          <h3>{source.name}</h3>
        </div>
        <div className="detail-path">
          {source.description}
          {source.readOnly ? t('side.readOnly') : ''}
          {source.writeOnly ? t('side.writeOnly') : ''}
        </div>
        <Section title={t('dd.accessSites')}>
          <p>
            {t('dd.accessSummary', {
              count: source.accessCount,
              reads: accesses.filter((item) => item.operation === 'Read').length,
              writes: accesses.filter((item) => item.operation !== 'Read').length,
            })}
          </p>
        </Section>
        <Section title={t('dd.settings')}>
          <p>
            {source.kind === 'shard'
              ? t('dd.shardNote', { count: source.shardCount })
              : source.kind === 'cache'
                ? t('dd.cacheNote')
                : t('dd.singleNote')}
          </p>
        </Section>
      </div>
    )
  }

  if (selection.type === 'storageTarget') {
    const accesses = data?.accesses.filter((access) => access.target === selection.key) ?? []
    if (accesses.length === 0) return <div className="panel-message">{t('dd.targetMissing')}</div>
    return (
      <div className="detail-body">
        <div className="detail-title">
          <span className={`ds-dot ${accesses[0].storage === 'Cache' ? 'redis' : 'shard'}`} />
          <h3 className="mono">{selection.key}</h3>
        </div>
        <div className="detail-path">{t('dd.targetSummary', { source: accesses[0].dataSource, count: accesses.length })}</div>
        <Section title={t('dd.usedBy')}>
          {accesses.map((access) => (
            <div className="target-row" key={access.id}>
              <span className={`cell ${access.operation === 'Read' ? 'r' : access.operation === 'Delete' ? 'd' : 'w'}`}>
                {access.operation === 'Read' ? 'R' : access.operation === 'Delete' ? 'D' : 'W'}
              </span>
              <button type="button" className="link" onClick={() => onSelect({ type: 'method', key: access.methodNodeId })}>
                {access.methodDisplayName}
              </button>
              <SourceLink
                file={access.file}
                line={access.line}
                onOpen={onOpenSource}
                onOpenInEditor={onOpenInEditor}
              />
              {access.confidence === 'Estimated' && <span className="chip small warn">{t('dd.estimated')}</span>}
              {access.transactionWrapper.length > 0 && (
                <span className="chip small">{t('dd.transactionWrapper', { name: access.transactionWrapper })}</span>
              )}
              {access.note.length > 0 && <div className="note">{access.note}</div>}
            </div>
          ))}
        </Section>
      </div>
    )
  }

  if (selection.type === 'rule') {
    const rule = [...(data?.rules ?? []), ...(pipeline?.rules ?? [])].find((item) => item.id === selection.key)
    if (!rule) return <div className="panel-message">{t('dd.ruleMissing')}</div>
    const hits = [...(data?.findings ?? []), ...(pipeline?.findings ?? [])]
      .filter((finding) => finding.ruleId === rule.id)
    return (
      <div className="detail-body">
        <div className="detail-title">
          {rule.enabled ? <span className={`sev ${rule.severity.toLowerCase()}`}>{t(severityLabelKeys[rule.severity])}</span>
            : <span className="sev muted">{t('dd.notChecked')}</span>}
          <h3>{rule.id}</h3>
        </div>
        <div className="detail-path">
          {t('dd.ruleJudge', {
            name: ruleText(rule, lang).name,
            confidence: rule.judge === 'Certain' ? t('data.certain') : t('data.estimated'),
          })}
          {rule.shardOnly ? t('dd.shardOnly') : ''}
        </div>
        <Section title={t('dd.ruleDescription')}>
          <p>{ruleText(rule, lang).description}</p>
          {/* "검사했는데 안 걸렸다"와 "그 입력을 애초에 읽지 않았다"는 다르다. 범위를 한 줄로 적는다. */}
          <p className="dim">{t(`rule.scope.${rule.scope ?? 'SqlAndConfiguredApis'}` as const)}</p>
        </Section>
        {!rule.enabled && (
          <Section title={t('dd.notCheckedTitle')}>
            <p>
              {rule.shardOnly
                ? t('dd.shardOffNote')
                : t('dd.notImplementedNote')}
            </p>
          </Section>
        )}
        <Section title={t('dd.hits', { count: hits.length })}>
          {hits.length === 0 && <p>{t('dd.noHits')}</p>}
          {hits.map((finding) => (
            <div key={finding.id} style={{ marginBottom: 6 }}>
              <button type="button" className="btn sm" onClick={() => onSelect({ type: 'finding', key: finding.id })}>
                <SourceLink
                  file={finding.file}
                  line={finding.line}
                  onOpen={onOpenSource}
                  onOpenInEditor={onOpenInEditor}
                />
              </button>
            </div>
          ))}
        </Section>
      </div>
    )
  }

  if (selection.type === 'mvcFilter') {
    const filter = (pipeline?.filters ?? []).find((item) => item.id === selection.key)
    if (!filter) return <div className="panel-message">{t('dd.filterMissing')}</div>
    return (
      <div className="detail-body">
        <div className="detail-title">
          <h3>{filter.name}</h3>
          <span className="chip small">{t(`mw.filterScope.${filter.scope}` as const)}</span>
          {filter.confidence === 'Estimated' && <span className="chip small warn">{t('dd.estimated')}</span>}
        </div>
        <div className="detail-path">
          {filter.implementationFile
            ? (
              <SourceLink
                file={filter.implementationFile}
                line={filter.implementationLine}
                onOpen={onOpenSource}
                onOpenInEditor={onOpenInEditor}
              />
            )
            : t('dd.frameworkImpl')}
          <br />
          {t('dd.registeredAtLabel')}{' '}
          <SourceLink
            file={filter.registrationFile}
            line={filter.registrationLine}
            onOpen={onOpenSource}
            onOpenInEditor={onOpenInEditor}
          />
        </div>
        <Section title={t('dd.filterKinds')}>
          <p>{filter.kinds.length > 0 ? filter.kinds.join(' · ') : t('dd.filterKindUnknown')}</p>
        </Section>
        {filter.appliesTo && <Section title={t('dd.filterAppliesTo')}><p>{filter.appliesTo}</p></Section>}
        <Section title={t('dd.onRequest')}><p>{filter.pre ?? '—'}</p></Section>
        <Section title={t('dd.onResponse')}><p>{filter.post ?? t('dd.noPost')}</p></Section>
        <Section title={t('dd.shortCircuit')}>
          <p>
            {filter.shortCircuit
              ? t('dd.shortCircuitNote', {
                condition: filter.shortCircuit.condition || t('mw.conditionUnknown'),
                status: filter.shortCircuit.statusCode,
              })
              : t('dd.noShortCircuit')}
          </p>
        </Section>
        {filter.note && <Section title={t('dd.note')}><p>{filter.note}</p></Section>}
      </div>
    )
  }

  if (selection.type === 'middlewareStep') {
    const step = pipeline?.steps.find((item) => item.id === selection.key)
    if (!step) return <div className="panel-message">{t('dd.stepMissing')}</div>
    const hits = pipeline?.findings.filter((finding) => finding.stepId === step.id) ?? []
    return (
      <div className="detail-body">
        <div className="detail-title">
          <h3>{step.name}</h3>
          {step.isDead && <span className="sev warn">{t('mw.dead')}</span>}
          {/* 종단 단계 뒤의 미들웨어는 요청을 보지 못한다. 순서를 읽을 때 기준이 되는 표시다. */}
          {step.isTerminal && <span className="chip small" title={t('mw.terminalTitle')}>{t('mw.terminal')}</span>}
        </div>
        <div className="detail-path">
          {step.implementationFile
            ? (
              <SourceLink
                file={step.implementationFile}
                line={step.implementationLine}
                onOpen={onOpenSource}
                onOpenInEditor={onOpenInEditor}
              />
            )
            : t('dd.frameworkImpl')}
          <br />
          {t('dd.registeredAtLabel')}{' '}
          <SourceLink
            file={step.registrationFile}
            line={step.registrationLine}
            onOpen={onOpenSource}
            onOpenInEditor={onOpenInEditor}
          />
        </div>
        <Section title={t('dd.onRequest')}><p>{step.pre ?? '—'}</p></Section>
        <Section title={t('dd.onResponse')}>
          <p>{step.post ?? t('dd.noPost')}</p>
        </Section>
        <Section title={t('dd.shortCircuit')}>
          <p>
            {step.shortCircuit
              ? t('dd.shortCircuitNote', {
                condition: step.shortCircuit.condition || t('mw.conditionUnknown'),
                status: step.shortCircuit.statusCode,
              })
              : t('dd.noShortCircuit')}
          </p>
        </Section>
        {step.contextAccesses.length > 0 && (
          <Section title={t('dd.contextAccess')}>
            <div className="pill-list">
              {step.contextAccesses.map((access) => (
                <span className="pill" key={`${access.mode}${access.target}`}>
                  {access.mode === 'Read' ? t('mw.read') : t('mw.write')} {access.target}
                </span>
              ))}
            </div>
          </Section>
        )}
        {step.filters.length > 0 && (
          <Section title={t('dd.endpointFilters')}>
            {step.filters.map((filter) => (
              <div className="target-row" key={`${filter.name}${filter.detail}`}>
                <span className="cell">{filter.name}</span>
                <span className="note">{filter.detail}</span>
                {filter.statusCode && (
                  <span className="chip small">{t('dd.filterStatus', { status: filter.statusCode })}</span>
                )}
              </div>
            ))}
            <p className="dim">{t('dd.endpointFiltersNote')}</p>
          </Section>
        )}
        {step.storeAccesses.length > 0 && (
          <Section title={t('dd.storeAccess')}>
            <p>{step.storeAccesses.join(' · ')}</p>
            <p className="dim">{t('dd.storeAccessNote')}</p>
          </Section>
        )}
        {step.note && <Section title={t('dd.note')}><p>{step.note}</p></Section>}
        {hits.length > 0 && (
          <Section title={t('dd.checkResults', { count: hits.length })}>
            {hits.map((finding) => (
              <div key={finding.id} style={{ marginBottom: 6 }}>
                <button type="button" className="btn sm" onClick={() => onSelect({ type: 'finding', key: finding.id })}>
                  {finding.ruleId}
                </button>{' '}
                <span className="dim">{finding.what}</span>
              </div>
            ))}
          </Section>
        )}
        {step.classId && (
          <div className="detail-actions">
            <button type="button" className="btn sm" onClick={() => onSelect({ type: 'class', key: step.classId! })}>
              {t('dd.showInClassDiagram')}
            </button>
          </div>
        )}
      </div>
    )
  }

  return null
}

function FindingDetail({
  finding,
  data,
  pipeline,
  onSelect,
  onShowFlow,
  onOpenSource,
  onOpenInEditor,
}: {
  finding: Finding
  data: DataAccessDocument | null
  pipeline: MiddlewarePipelineDocument | null
  onSelect: (selection: Selection) => void
  onShowFlow: (nodeId: string) => void
  onOpenSource: (location: SourceLocation) => void
  onOpenInEditor: (location: SourceLocation) => void
}) {
  const { t, lang } = useI18n()
  const rule = [...(data?.rules ?? []), ...(pipeline?.rules ?? [])].find((item) => item.id === finding.ruleId)
  return (
    <div className="detail-body">
      <div className="detail-title">
        <span className={`sev ${finding.severity.toLowerCase()}`}>{t(severityLabelKeys[finding.severity])}</span>
        {/* 규칙 이름은 한곳(ruleText)에서만 만든다. rule.name은 서버가 준 한국어 원문이다. */}
        <h3>{rule ? ruleText(rule, lang).name : finding.ruleId}</h3>
      </div>
      <div className="detail-path">
        {finding.symbol}<br />
        <SourceLink
          file={finding.file}
          line={finding.line}
          onOpen={onOpenSource}
          onOpenInEditor={onOpenInEditor}
        />
      </div>

      <Section title={t('dd.whatFlagged')}><p>{finding.what}</p></Section>
      <Section title={t('dd.evidence', { confidence: finding.confidence === 'Certain' ? t('data.certain') : t('data.estimated') })}>
        <div className="chain">
          {finding.evidence.map((item, index) => (
            <div className={`chain-step ${item.tone}`} key={`${item.head}${index}`}>
              {item.head}
              <span className="note mono">{item.detail}</span>
              {item.file && (
                <SourceLink
                  file={item.file}
                  line={item.line}
                  onOpen={onOpenSource}
                  onOpenInEditor={onOpenInEditor}
                />
              )}
              {item.note && <span className="note">{item.note}</span>}
            </div>
          ))}
        </div>
      </Section>
      <Section title={t('dd.whyProblem')}><p>{finding.why}</p></Section>
      <Section title={t('dd.suggestion')}><p>{finding.fix}</p></Section>

      <div className="detail-actions">
        <button type="button" className="btn sm" onClick={() => onSelect({ type: 'rule', key: finding.ruleId })}>
          {t('dd.ruleDescription')}
        </button>
        {finding.nodeId && (
          <button type="button" className="btn sm" onClick={() => onShowFlow(finding.nodeId!)}>
            {t('dd.showInCallFlow')}
          </button>
        )}
        {finding.pathId && (
          <button type="button" className="btn sm" onClick={() => onSelect({ type: 'writePath', key: finding.pathId! })}>
            {t('dd.showInFailGrid')}
          </button>
        )}
        {finding.stepId && pipeline?.steps.some((step) => step.id === finding.stepId) && (
          <button type="button" className="btn sm" onClick={() => onSelect({ type: 'middlewareStep', key: finding.stepId! })}>
            {t('dd.showInPipeline')}
          </button>
        )}
      </div>
    </div>
  )
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="detail-section">
      <h4>{title}</h4>
      {children}
    </div>
  )
}

function findFinding(
  data: DataAccessDocument | null,
  pipeline: MiddlewarePipelineDocument | null,
  id: string,
): Finding | undefined {
  return data?.findings.find((item) => item.id === id) ?? pipeline?.findings.find((item) => item.id === id)
}
