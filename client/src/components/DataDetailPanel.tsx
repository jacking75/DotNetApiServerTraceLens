import { compensationLabels, dataSourceTone, severityLabels } from '../graph/dataView'
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
}

/**
 * DB·캐시와 미들웨어 보기에서 고른 항목의 상세.
 * 점검 항목은 "무엇이 걸렸나 → 판정 근거 → 왜 문제인가 → 제안" 순서로 보여준다.
 */
export function DataDetailPanel({ graph, selection, onSelect, onShowFlow }: DataDetailPanelProps) {
  const data = graph.dataAccess
  const pipeline = graph.middleware

  if (selection.type === 'finding') {
    const finding = findFinding(data, pipeline, selection.key)
    if (!finding) return <div className="panel-message">이 점검 항목을 찾지 못했다. 다시 분석하면 갱신된다.</div>
    return <FindingDetail finding={finding} data={data} pipeline={pipeline} onSelect={onSelect} onShowFlow={onShowFlow} />
  }

  if (selection.type === 'writePath') {
    const path = data?.writePaths.find((item) => item.id === selection.key)
    if (!path) return <div className="panel-message">이 쓰기 경로를 찾지 못했다.</div>
    return (
      <div className="detail-body">
        <div className="detail-title">
          {path.inTransaction
            ? <span className="sev ok">정상</span>
            : <span className="sev critical">보상 확인</span>}
          <h3>{path.methodDisplayName}</h3>
        </div>
        <div className="detail-path">{path.file}:{path.startLine}</div>
        <Section title="판정"><p>{path.note}</p></Section>
        <Section title="단계별 되돌림">
          <div className="chain">
            {path.steps.map((step) => (
              <div
                className={`chain-step ${step.compensation === 'No' || step.compensation === 'PartialPaths' || step.compensation === 'Irreversible' ? 'bad' : step.compensation === 'NotNeeded' ? '' : 'ok'}`}
                key={step.order}
              >
                {step.order}. {step.label}
                <span className="note mono">{compensationLabels[step.compensation]}</span>
                <span className="note">{step.compensationNote}</span>
              </div>
            ))}
          </div>
        </Section>
        <div className="detail-actions">
          <button type="button" className="btn sm" onClick={() => onShowFlow(path.methodNodeId)}>
            호출 흐름에서 보기
          </button>
        </div>
      </div>
    )
  }

  if (selection.type === 'dataSource') {
    const source = data?.dataSources.find((item) => item.name === selection.key)
    if (!source) return <div className="panel-message">이 데이터소스를 찾지 못했다.</div>
    const accesses = data?.accesses.filter((access) => access.dataSource === source.name) ?? []
    return (
      <div className="detail-body">
        <div className="detail-title">
          <span className={`ds-dot ${dataSourceTone(source.kind)}`} />
          <h3>{source.name}</h3>
        </div>
        <div className="detail-path">
          {source.description}
          {source.readOnly ? ' · 읽기 전용' : ''}
          {source.writeOnly ? ' · 쓰기 전용' : ''}
        </div>
        <Section title="접근 지점">
          <p>
            코드에서 {source.accessCount}곳이 이 저장소를 건드린다
            (읽기 {accesses.filter((item) => item.operation === 'Read').length} ·
            쓰기 {accesses.filter((item) => item.operation !== 'Read').length}).
          </p>
        </Section>
        <Section title="설정">
          <p>
            {source.kind === 'shard'
              ? `tracelens.config.json에서 샤드 ${source.shardCount}개로 잡았다. 샤드 번호는 런타임에 정해지므로 이 화면은 "어떤 키로 골랐는가"까지만 확정한다.`
              : source.kind === 'cache'
                ? '캐시로 잡은 데이터소스다. 키는 보간 문자열을 패턴으로 정규화해 모은다.'
                : '단일 데이터소스다. 샤드로 보려면 설정의 dataSources에서 kind를 shard로 바꾼다.'}
          </p>
        </Section>
      </div>
    )
  }

  if (selection.type === 'storageTarget') {
    const accesses = data?.accesses.filter((access) => access.target === selection.key) ?? []
    if (accesses.length === 0) return <div className="panel-message">이 대상에 대한 접근을 찾지 못했다.</div>
    return (
      <div className="detail-body">
        <div className="detail-title">
          <span className={`ds-dot ${accesses[0].storage === 'Cache' ? 'redis' : 'shard'}`} />
          <h3 className="mono">{selection.key}</h3>
        </div>
        <div className="detail-path">{accesses[0].dataSource} · 접근 {accesses.length}곳</div>
        <Section title="이 대상을 쓰는 코드">
          {accesses.map((access) => (
            <div className="target-row" key={access.id}>
              <span className={`cell ${access.operation === 'Read' ? 'r' : access.operation === 'Delete' ? 'd' : 'w'}`}>
                {access.operation === 'Read' ? 'R' : access.operation === 'Delete' ? 'D' : 'W'}
              </span>
              <button type="button" className="link" onClick={() => onSelect({ type: 'method', key: access.methodNodeId })}>
                {access.methodDisplayName}
              </button>
              <span className="note">{access.file}:{access.line}</span>
              {access.confidence === 'Estimated' && <span className="chip small warn">추정</span>}
              {access.transactionWrapper.length > 0 && (
                <span className="chip small">트랜잭션 래퍼 {access.transactionWrapper}</span>
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
    if (!rule) return <div className="panel-message">이 규칙을 찾지 못했다.</div>
    const hits = [...(data?.findings ?? []), ...(pipeline?.findings ?? [])]
      .filter((finding) => finding.ruleId === rule.id)
    return (
      <div className="detail-body">
        <div className="detail-title">
          {rule.enabled ? <span className={`sev ${rule.severity.toLowerCase()}`}>{severityLabels[rule.severity]}</span>
            : <span className="sev muted">검사 안 함</span>}
          <h3>{rule.id}</h3>
        </div>
        <div className="detail-path">
          {rule.name} · 판정 {rule.judge === 'Certain' ? '확실' : '추정'}
          {rule.shardOnly ? ' · 샤딩 전용' : ''}
        </div>
        <Section title="규칙 설명"><p>{rule.description}</p></Section>
        {!rule.enabled && (
          <Section title="지금은 검사하지 않는다">
            <p>
              {rule.shardOnly
                ? '설정에 샤드 데이터소스가 없어 이 규칙군은 꺼져 있다. dataSources에서 kind를 shard로 잡으면 켜진다.'
                : '이번 단계에서는 구현하지 않은 규칙이다. 무엇을 검사하지 않는지 분명히 보이도록 목록에는 남겨 둔다.'}
            </p>
          </Section>
        )}
        <Section title={`걸린 항목 ${hits.length}건`}>
          {hits.length === 0 && <p>이 코드베이스에서는 걸린 항목이 없다.</p>}
          {hits.map((finding) => (
            <div key={finding.id} style={{ marginBottom: 6 }}>
              <button type="button" className="btn sm" onClick={() => onSelect({ type: 'finding', key: finding.id })}>
                {finding.file}:{finding.line}
              </button>
            </div>
          ))}
        </Section>
      </div>
    )
  }

  if (selection.type === 'middlewareStep') {
    const step = pipeline?.steps.find((item) => item.id === selection.key)
    if (!step) return <div className="panel-message">이 미들웨어 단계를 찾지 못했다.</div>
    const hits = pipeline?.findings.filter((finding) => finding.stepId === step.id) ?? []
    return (
      <div className="detail-body">
        <div className="detail-title">
          <h3>{step.name}</h3>
          {step.isDead && <span className="sev warn">실행되지 않음</span>}
        </div>
        <div className="detail-path">
          {step.implementationFile ?? '(프레임워크)'}
          <br />
          등록: {step.registrationFile}:{step.registrationLine}
        </div>
        <Section title="요청 시"><p>{step.pre ?? '—'}</p></Section>
        <Section title="응답 시 (next 이후)">
          <p>{step.post ?? '하는 일 없음 — next 호출이 마지막이다.'}</p>
        </Section>
        <Section title="단락">
          <p>
            {step.shortCircuit
              ? `조건: ${step.shortCircuit.condition || '조건을 읽지 못했다'} → ${step.shortCircuit.statusCode}로 끊는다. 아래 단계는 실행되지 않는다.`
              : '끊지 않는다 — 항상 next를 부른다.'}
          </p>
        </Section>
        {step.contextAccesses.length > 0 && (
          <Section title="HttpContext 접근">
            <div className="pill-list">
              {step.contextAccesses.map((access) => (
                <span className="pill" key={`${access.mode}${access.target}`}>
                  {access.mode === 'Read' ? '읽기' : '쓰기'} {access.target}
                </span>
              ))}
            </div>
          </Section>
        )}
        {step.storeAccesses.length > 0 && (
          <Section title="저장소 접근">
            <p>{step.storeAccesses.join(' · ')}</p>
            <p className="dim">DB · 캐시 보기에서도 같은 규칙으로 검사한다.</p>
          </Section>
        )}
        {step.note && <Section title="메모"><p>{step.note}</p></Section>}
        {hits.length > 0 && (
          <Section title={`점검 결과 ${hits.length}건`}>
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
              클래스 다이어그램에서 보기
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
}: {
  finding: Finding
  data: DataAccessDocument | null
  pipeline: MiddlewarePipelineDocument | null
  onSelect: (selection: Selection) => void
  onShowFlow: (nodeId: string) => void
}) {
  const rule = [...(data?.rules ?? []), ...(pipeline?.rules ?? [])].find((item) => item.id === finding.ruleId)
  return (
    <div className="detail-body">
      <div className="detail-title">
        <span className={`sev ${finding.severity.toLowerCase()}`}>{severityLabels[finding.severity]}</span>
        <h3>{rule?.name ?? finding.ruleId}</h3>
      </div>
      <div className="detail-path">{finding.symbol}<br />{finding.file}:{finding.line}</div>

      <Section title="무엇이 걸렸나"><p>{finding.what}</p></Section>
      <Section title={`판정 근거 · ${finding.confidence === 'Certain' ? '확실' : '추정'}`}>
        <div className="chain">
          {finding.evidence.map((item, index) => (
            <div className={`chain-step ${item.tone}`} key={`${item.head}${index}`}>
              {item.head}
              <span className="note mono">{item.detail}</span>
              {item.note && <span className="note">{item.note}</span>}
            </div>
          ))}
        </div>
      </Section>
      <Section title="왜 문제인가"><p>{finding.why}</p></Section>
      <Section title="제안"><p>{finding.fix}</p></Section>

      <div className="detail-actions">
        <button type="button" className="btn sm" onClick={() => onSelect({ type: 'rule', key: finding.ruleId })}>
          규칙 설명
        </button>
        {finding.nodeId && (
          <button type="button" className="btn sm" onClick={() => onShowFlow(finding.nodeId!)}>
            호출 흐름에서 보기
          </button>
        )}
        {finding.pathId && (
          <button type="button" className="btn sm" onClick={() => onSelect({ type: 'writePath', key: finding.pathId! })}>
            보상 격자에서 보기
          </button>
        )}
        {finding.stepId && pipeline?.steps.some((step) => step.id === finding.stepId) && (
          <button type="button" className="btn sm" onClick={() => onSelect({ type: 'middlewareStep', key: finding.stepId! })}>
            파이프라인에서 보기
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
