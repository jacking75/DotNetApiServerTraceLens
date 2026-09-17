import { useEffect } from 'react'
import { useI18n } from '../i18n/I18nProvider'
import type { AnalysisDiagnostics } from '../types/graph'

interface DiagnosticsPanelProps {
  diagnostics: AnalysisDiagnostics | null
  /** DB·캐시에서 인식했지만 해석하지 않은 입력. 빈 보기의 원인을 여기서 말한다. */
  dataAccessNotes: readonly string[]
  /** 요약 줄에서 온 부가 수치. 진단에는 없지만 같은 화면에서 보는 편이 낫다. */
  minimalApiCount: number
  entryPointCount: number
  onClose: () => void
}

/**
 * "이 그래프를 어디까지 믿을 수 있나"를 한 화면에 모은다.
 * 분석기가 이미 기록하지만 아무도 그리지 않던 필드(프로젝트 로드 결과·한계·제외 범위·분석 중 변경)를 전부 보여 준다.
 * 숫자는 분석기가 준 그대로 쓰고, 여기서 새로 계산하거나 반올림하지 않는다.
 */
export function DiagnosticsPanel({
  diagnostics,
  dataAccessNotes,
  minimalApiCount,
  entryPointCount,
  onClose,
}: DiagnosticsPanelProps) {
  const { t, formatPercent } = useI18n()

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const failedProjects = (diagnostics?.projects ?? []).filter(
    (project) => !project.loaded || project.errorCount > 0)

  return (
    <div className="modal-backdrop" role="presentation" onClick={onClose}>
      <div
        className="modal diagnostics-panel"
        role="dialog"
        aria-modal="true"
        aria-label={t('diagPanel.title')}
        onClick={(event) => event.stopPropagation()}
      >
        <header>
          <span>{t('diagPanel.title')}</span>
          <button type="button" className="link-button" onClick={onClose}>{t('ask.close')}</button>
        </header>

        {!diagnostics ? (
          <div className="panel-message">{t('diagPanel.missing')}</div>
        ) : (
          <>
            <section>
              <h4>{t('diagPanel.projects')}</h4>
              <div className="hint">
                {t('diagPanel.projectCounts', {
                  loaded: diagnostics.loadedProjects,
                  total: diagnostics.totalProjects,
                  errors: diagnostics.projectsWithCompileErrors,
                })}
              </div>
              {failedProjects.length === 0 ? (
                <div className="hint ok">{t('diagPanel.projectsOk')}</div>
              ) : (
                <>
                  <ul className="diag-projects">
                    {failedProjects.map((project) => (
                      <li key={project.path}>
                        <b>{project.name}</b>
                        <span className="chip small warn">
                          {project.loaded
                            ? t('diagPanel.errorCount', { count: project.errorCount })
                            : t('diagPanel.notLoaded')}
                        </span>
                        <ul>
                          {project.sampleErrors.slice(0, 3).map((line) => <li key={line}><code>{line}</code></li>)}
                        </ul>
                      </li>
                    ))}
                  </ul>
                  <div className="hint">{t('diagPanel.buildFirst')}</div>
                </>
              )}
            </section>

            <section>
              <h4>{t('diagPanel.calls')}</h4>
              <div className="hint">
                {t('summary.resolution', {
                  resolved: diagnostics.calls.resolved,
                  inspected: diagnostics.calls.inspected,
                  rate: formatPercent(diagnostics.calls.resolutionRate),
                })}
              </div>
              <ul className="diag-kv">
                <li>{t('detail.resolution.direct')}: {diagnostics.calls.direct}</li>
                <li>{t('detail.resolution.diRegistration')}: {diagnostics.calls.diRegistration}</li>
                <li>{t('detail.resolution.singleImplementation')}: {diagnostics.calls.singleImplementation}</li>
                <li>{t('detail.resolution.ambiguous')}: {diagnostics.calls.ambiguous}</li>
                <li>{t('detail.resolution.unresolved')}: {diagnostics.calls.unresolved}</li>
                <li>{t('detail.resolution.external')}: {diagnostics.calls.external}</li>
              </ul>
              <div className="hint">{t('diagPanel.externalExcluded', { count: diagnostics.calls.external })}</div>
              {diagnostics.calls.externalByNamespace?.length ? (
                <>
                  <h5>{t('diagPanel.externalByNamespace')}</h5>
                  <ul className="diag-kv">
                    {diagnostics.calls.externalByNamespace.map((group) => (
                      <li key={group.namespace}>
                        <code>{group.namespace}</code> {t('panel.count', { count: group.count })}
                        {group.sampleType && <span className="hint"> · <code>{group.sampleType}</code></span>}
                      </li>
                    ))}
                  </ul>
                </>
              ) : null}
            </section>

            <section>
              <h4>{t('diagPanel.scope')}</h4>
              <ul className="diag-kv">
                <li>{t('diagPanel.analysisScope', { scope: diagnostics.analysisScope })}</li>
                <li>{t('diagPanel.maxDepth', { depth: diagnostics.maxCallDepth })}
                  {diagnostics.depthLimitReached ? ` — ${t('diagPanel.depthReached')}` : ''}</li>
                <li>{t('summary.entryPoints', { count: entryPointCount })} · {t('summary.minimalApi', { count: minimalApiCount })}</li>
                {diagnostics.excludedNamespacePrefixes.length > 0 && (
                  <li>{t('diagPanel.excluded', { list: diagnostics.excludedNamespacePrefixes.join(', ') })}</li>
                )}
              </ul>
            </section>

            {diagnostics.entryPointCandidates?.length ? (
              <section>
                <h4>{t('diagPanel.entryPointCandidates')}</h4>
                <div className="hint">{t('diagPanel.entryPointCandidatesNote')}</div>
                <ul className="diag-kv">
                  {diagnostics.entryPointCandidates.map((group) => (
                    <li key={group.kind}>
                      <b>{group.kind}</b> {t('panel.count', { count: group.count })}
                      {group.samples.length > 0 && <span className="hint"> · {group.samples.join(', ')}</span>}
                    </li>
                  ))}
                </ul>
              </section>
            ) : null}

            {dataAccessNotes.length > 0 && (
              <section>
                <h4>{t('diagPanel.dataNotes')}</h4>
                <ul className="diag-kv">
                  {dataAccessNotes.map((line) => <li key={line}>{line}</li>)}
                </ul>
              </section>
            )}

            <section>
              <h4>{t('diagPanel.limitations')}</h4>
              {diagnostics.limitations.length === 0 ? (
                <div className="hint">{t('detail.none')}</div>
              ) : (
                <ul className="diag-kv">
                  {diagnostics.limitations.map((line) => <li key={line}>{line}</li>)}
                </ul>
              )}
            </section>

            {diagnostics.changedDuringAnalysis && (
              <section>
                <h4>{t('diagPanel.changedDuringAnalysis')}</h4>
                <ul className="diag-kv">
                  {diagnostics.changedDuringAnalysisFiles.map((file) => <li key={file}><code>{file}</code></li>)}
                </ul>
              </section>
            )}
          </>
        )}
      </div>
    </div>
  )
}
