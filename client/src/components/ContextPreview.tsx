import { useState } from 'react'
import { useI18n } from '../i18n/I18nProvider'
import type { ContextDocument } from '../types/context'

interface ContextPreviewProps {
  context: ContextDocument
  /** 문맥에서 뺀 파일. 체크를 끄면 그 파일의 소스가 문맥에 들어가지 않는다. */
  excludedFiles?: readonly string[]
  onToggleFile?: (file: string, include: boolean) => void
}

/**
 * AI에 전달할 문맥의 미리보기다.
 * 포함한 파일, 소스 포함 여부, 길이, 제외 항목을 먼저 보여 준다 —
 * 외부 AI로 보내면 그 내용이 전송되므로 사용자가 미리 확인할 수 있어야 한다.
 */
export function ContextPreview({ context, excludedFiles = [], onToggleFile }: ContextPreviewProps) {
  const { t, formatPercent } = useI18n()
  const [showMarkdown, setShowMarkdown] = useState(false)

  return (
    <div className="context-preview">
      <div className="context-meta">
        <span className={context.containsSource ? 'chip warn' : 'chip'}>
          {context.containsSource ? t('context.hasSource') : t('context.noSource')}
        </span>
        <span className="chip">{t('context.files', { count: context.files.length })}</span>
        <span className="chip" title={t('context.tokensNote')}>
          {t('context.length', { chars: context.characters, tokens: context.estimatedTokens })}
        </span>
        <span className={`chip freshness-${context.freshness.status}`}>{context.freshness.status}</span>
      </div>

      {context.containsSource && <div className="hint warn">{t('context.sendWarning')}</div>}
      {!context.containsSource && context.omitted.sections.includes('sourceSnippets') && (
        <div className="hint">{t('context.staleWarning')}</div>
      )}

      <div className="context-analysis hint">
        {context.analysis.inspectedCallSites > 0 && (
          <div title={t('summary.resolutionHint')}>
            {t('summary.resolution', {
              resolved: context.analysis.resolvedCallSites,
              inspected: context.analysis.inspectedCallSites,
              rate: formatPercent(context.analysis.resolutionRate),
            })}
          </div>
        )}
      </div>

      {context.files.length > 0 && (
        <ul className="context-files">
          {context.files.map((file) => (
            <li key={file}>
              {onToggleFile ? (
                <label className="checkbox" title={t('context.excludeFile')}>
                  <input
                    type="checkbox"
                    checked={!excludedFiles.includes(file)}
                    onChange={(event) => onToggleFile(file, event.target.checked)}
                  />
                  <code>{file}</code>
                </label>
              ) : (
                <code>{file}</code>
              )}
            </li>
          ))}
        </ul>
      )}
      {excludedFiles.length > 0 && (
        <div className="hint">{t('context.excludedCount', { count: excludedFiles.length })}</div>
      )}

      {context.paths.length > 0 && (
        <details open>
          <summary>{t('context.paths')}</summary>
          <ul className="context-paths">
            {context.paths.map((path) => (
              <li key={path.endpointId}>
                <b>{path.httpMethod} {path.route}</b>
                <ol>
                  {path.steps.map((step) => (
                    <li key={step.nodeId}>
                      {step.displayName} <code>{step.file}:{step.startLine}</code>
                      <span className="chip small">{step.resolution}/{step.confidence}</span>
                    </li>
                  ))}
                </ol>
              </li>
            ))}
          </ul>
        </details>
      )}

      {context.evidence.length > 0 && (
        <details>
          <summary>{t('context.evidence')} ({context.evidence.length})</summary>
          <ul className="context-evidence">
            {context.evidence.map((item) => (
              <li key={item.id}>
                <code>{item.id}</code> {item.kind} · <code>{item.file}:{item.startLine}</code>
                <span className="chip small">{item.resolution}/{item.confidence}</span>
                <div className="hint">{item.detail}</div>
              </li>
            ))}
          </ul>
        </details>
      )}

      {(context.omitted.nodes > 0 || context.omitted.sourceRanges.length > 0 || context.omitted.sections.length > 0) && (
        <details>
          <summary>{t('context.omitted')}</summary>
          <ul>
            {context.omitted.nodes > 0 && <li>nodes: {context.omitted.nodes}</li>}
            {context.omitted.sourceRanges.map((range) => <li key={range}><code>{range}</code></li>)}
            {context.omitted.sections.map((section) => <li key={section}>{section}</li>)}
          </ul>
        </details>
      )}

      <details>
        <summary>{t('context.limitations')} ({context.limitations.length})</summary>
        <ul className="context-limitations">
          {context.limitations.map((limitation) => <li key={limitation}>{limitation}</li>)}
        </ul>
      </details>

      <button type="button" className="link-button" onClick={() => setShowMarkdown((value) => !value)}>
        {showMarkdown ? t('context.hideMarkdown') : t('context.showMarkdown')}
      </button>
      {showMarkdown && <pre className="context-markdown">{context.markdown}</pre>}
    </div>
  )
}
