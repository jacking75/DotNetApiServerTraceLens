import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError, askWithMcp, buildContext, getMcpSetup, openInEditor } from '../api/client'
import { translateError, useI18n } from '../i18n/I18nProvider'
import type { TranslationKey } from '../i18n/ko'
import type { ContextBudget, ContextDocument, ContextIntent, McpSetupPreview, SelectionKind } from '../types/context'
import type { AskMode } from '../types/graph'
import { ContextPreview } from './ContextPreview'
import { ShareReportDialog } from './ShareReportDialog'

/** 질문 패널이 다루는 선택. 실행 가능한 노드가 없어도 문맥은 만들 수 있다. */
export interface AskContext {
  kind: SelectionKind
  id: string
  /** 에디터 열기에 쓰는 노드 id. 클래스·네임스페이스 선택에는 없을 수 있다. */
  nodeId: string | null
  lines: Array<{ label: string; value: string }>
}

interface AskPanelProps {
  context: AskContext | null
  snapshotId: string
  /** 문맥 복사를 마치면 첫 사용 안내의 마지막 단계가 끝난다. */
  onContextCopied?: () => void
}

const modeKeys: Record<AskMode, TranslationKey> = {
  context: 'ask.modeContext',
  mcp: 'ask.modeAgent',
  editor: 'ask.modeEditor',
}

const intents: ReadonlyArray<{ value: ContextIntent; key: TranslationKey }> = [
  { value: 'Explain', key: 'ask.intent.explain' },
  { value: 'PlanChange', key: 'ask.intent.planChange' },
  { value: 'ReviewChange', key: 'ask.intent.reviewChange' },
  { value: 'InvestigateData', key: 'ask.intent.investigateData' },
]

const budgets: ReadonlyArray<{ value: ContextBudget; key: TranslationKey }> = [
  { value: 'Short', key: 'ask.budget.short' },
  { value: 'Balanced', key: 'ask.budget.balanced' },
  { value: 'Detailed', key: 'ask.budget.detailed' },
]

/**
 * 선택한 위치를 "작업 문맥"으로 바꿔 AI에 전달한다.
 * 문맥은 서버의 `POST /api/context` 하나에서 나오므로 클립보드·인앱 실행·MCP가 같은 내용을 쓴다.
 */
export function AskPanel({ context, snapshotId, onContextCopied }: AskPanelProps) {
  const { t } = useI18n()
  const [mode, setMode] = useState<AskMode>('context')
  const [intent, setIntent] = useState<ContextIntent>('Explain')
  const [budget, setBudget] = useState<ContextBudget>('Balanced')
  const [includeSource, setIncludeSource] = useState(true)
  const [question, setQuestion] = useState('')
  const [preview, setPreview] = useState<ContextDocument | null>(null)
  const [answer, setAnswer] = useState('')
  const [logs, setLogs] = useState<string[]>([])
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [setup, setSetup] = useState<McpSetupPreview | null>(null)
  /** 미리보기에서 체크를 끈 파일. 문맥 생성과 에이전트 실행 양쪽에 그대로 넘긴다. */
  const [excludedFiles, setExcludedFiles] = useState<string[]>([])
  const [shareOpen, setShareOpen] = useState(false)
  const abortController = useRef<AbortController | null>(null)
  const contextKey = context ? `${context.kind}:${context.id}` : null

  const describe = useCallback((reason: unknown, fallbackKey: TranslationKey) =>
    reason instanceof ApiError ? translateError(t, reason.code, reason.message) : t(fallbackKey), [t])

  useEffect(() => {
    abortController.current?.abort()
    setAnswer('')
    setLogs([])
    setMessage(null)
    setPreview(null)
    setBusy(false)
    setExcludedFiles([])
    setShareOpen(false)
  }, [contextKey])

  useEffect(() => () => abortController.current?.abort(), [])

  // 선택·목적·예산이 바뀌면 미리보기를 다시 만든다. 이 단계에는 LLM 호출이 없다.
  useEffect(() => {
    if (!context) return
    let active = true
    const timer = window.setTimeout(() => {
      void (async () => {
        try {
          const document = await buildContext({
            kind: context.kind,
            id: context.id,
            intent,
            budget,
            question: question.trim(),
            includeSource,
            excludeFiles: excludedFiles,
            expectedSnapshotId: snapshotId || undefined,
          })
          if (active) {
            setPreview(document)
            setMessage(null)
          }
        } catch (reason) {
          if (active) {
            setPreview(null)
            setMessage(describe(reason, 'ask.contextFailed'))
          }
        }
      })()
    }, 200)
    return () => {
      active = false
      window.clearTimeout(timer)
    }
  }, [context, contextKey, intent, budget, question, includeSource, excludedFiles, snapshotId, describe])

  const copy = async () => {
    if (!preview) return
    try {
      await navigator.clipboard.writeText(preview.markdown)
      setMessage(t('ask.copied'))
      onContextCopied?.()
    } catch {
      setMessage(t('ask.copyFailed'))
    }
  }

  const ask = async () => {
    if (!context || !question.trim()) return
    abortController.current?.abort()
    const controller = new AbortController()
    abortController.current = controller
    setBusy(true)
    setAnswer('')
    setLogs([])
    setMessage(null)
    try {
      await askWithMcp(
        {
          kind: context.kind,
          id: context.id,
          nodeId: context.nodeId,
          question: question.trim(),
          mode: 'mcp',
          intent,
          budget,
          includeSource,
          excludeFiles: excludedFiles,
          expectedSnapshotId: snapshotId || undefined,
        },
        (event) => {
          if (event.type === 'delta' && event.text) setAnswer((current) => current + event.text)
          if (event.type === 'log' && event.text) {
            const logText = event.text
            setLogs((current) => [...current, logText])
          }
          if (event.type === 'error') setMessage(event.text ?? t('ask.agentFailed'))
        },
        controller.signal,
      )
    } catch (reason) {
      if (!controller.signal.aborted) setMessage(describe(reason, 'ask.agentFailed'))
    } finally {
      if (!controller.signal.aborted) setBusy(false)
    }
  }

  const openEditor = async () => {
    if (!context?.nodeId) return
    setBusy(true)
    setMessage(null)
    try {
      const result = await openInEditor(context.nodeId)
      if (result.opened) {
        setMessage(t('ask.editorOpened'))
      } else {
        setMessage(result.error ?? t('ask.editorFailed'))
        window.location.href = result.fallbackUri
      }
    } catch (reason) {
      setMessage(describe(reason, 'ask.editorFailed'))
    } finally {
      setBusy(false)
    }
  }

  const showSetup = async () => {
    try {
      setSetup(await getMcpSetup())
    } catch (reason) {
      setMessage(describe(reason, 'ask.contextFailed'))
    }
  }

  const run = () => {
    if (mode === 'context') void copy()
    if (mode === 'mcp') void ask()
    if (mode === 'editor') void openEditor()
  }

  const needsQuestion = mode === 'mcp'
  const blocked = mode === 'editor' && !context?.nodeId

  return (
    <section className="panel ask-panel" id="tracelens-ask-panel">
      <header>
        <span>{t('ask.title')}</span>
        <small>{t(modeKeys[mode])}</small>
      </header>
      <div className="ask-mode-tabs" role="tablist" aria-label={t('ask.title')}>
        {(Object.keys(modeKeys) as AskMode[]).map((item) => (
          <button
            type="button"
            role="tab"
            aria-selected={mode === item}
            className={mode === item ? 'active' : ''}
            onClick={() => { setMode(item); setMessage(null) }}
            key={item}
          >
            {t(modeKeys[item])}
          </button>
        ))}
      </div>
      <div className="ask-scroll">
        <div className="captured-context">
          {context
            ? context.lines.map((line) => (
              <div key={line.label}>{line.label}: <b>{line.value}</b></div>
            ))
            : t('ask.selectFirst')}
        </div>

        {blocked && context && <div className="panel-message tight">{t('ask.needNode')}</div>}

        {mode !== 'editor' && context && (
          <>
            <div className="ask-options">
              <label>
                {t('ask.intent')}
                <select value={intent} onChange={(event) => setIntent(event.target.value as ContextIntent)}>
                  {intents.map((item) => <option key={item.value} value={item.value}>{t(item.key)}</option>)}
                </select>
              </label>
              <label>
                {t('ask.budget')}
                <select value={budget} onChange={(event) => setBudget(event.target.value as ContextBudget)}>
                  {budgets.map((item) => <option key={item.value} value={item.value}>{t(item.key)}</option>)}
                </select>
              </label>
              <label className="checkbox">
                <input
                  type="checkbox"
                  checked={!includeSource}
                  onChange={(event) => setIncludeSource(!event.target.checked)}
                />
                {t('context.excludeSource')}
              </label>
            </div>
            <textarea
              value={question}
              onChange={(event) => setQuestion(event.target.value)}
              placeholder={t('ask.questionPlaceholder')}
              aria-label={t('ask.title')}
              disabled={busy}
            />
          </>
        )}

        {mode !== 'editor' && preview && (
          <ContextPreview
            context={preview}
            excludedFiles={excludedFiles}
            onToggleFile={(file, include) => setExcludedFiles((current) =>
              include ? current.filter((item) => item !== file) : [...current, file])}
          />
        )}

        {mode === 'mcp' && (question.trim() || answer) && (
          <div className="ask-response" aria-live="polite">
            {question.trim() && <div className="chat-bubble user">{question.trim()}</div>}
            {(answer || busy) && <div className="chat-bubble assistant">{answer || t('ask.working')}</div>}
            {logs.length > 0 && (
              <details>
                <summary>{t('ask.logs', { count: logs.length })}</summary>
                <pre>{logs.join('\n')}</pre>
              </details>
            )}
          </div>
        )}

        <div className="ask-actions">
          <button
            type="button"
            className="primary"
            onClick={run}
            disabled={!context || busy || blocked || (needsQuestion && !question.trim()) || (mode === 'context' && !preview)}
          >
            {busy
              ? t('app.analyzing')
              : mode === 'context' ? t('ask.copy') : mode === 'mcp' ? t('ask.send') : t('ask.openEditor')}
          </button>
          <button type="button" className="link-button" onClick={() => void showSetup()}>
            {t('ask.mcpSetup')}
          </button>
          <button
            type="button"
            className="link-button"
            onClick={() => setShareOpen(true)}
            disabled={!context}
          >
            {t('share.open')}
          </button>
          {message && <div className="copy-message">{message}</div>}
        </div>

        {setup && (
          <div className="mcp-setup">
            <header>
              <span>{t('ask.mcpSetupTitle')}</span>
              <button type="button" className="link-button" onClick={() => setSetup(null)}>{t('ask.close')}</button>
            </header>
            <div className="hint">{t('ask.mcpSetupNote')}</div>
            {setup.warning && <div className="hint warn">{setup.warning}</div>}
            {setup.hosts.map((host) => (
              <details key={host.host}>
                <summary>{host.host} · <code>{host.file}</code></summary>
                <div className="hint">{host.note}</div>
                <pre>{host.content}</pre>
              </details>
            ))}
          </div>
        )}
      </div>

      {shareOpen && context && (
        <ShareReportDialog kind={context.kind} id={context.id} onClose={() => setShareOpen(false)} />
      )}
    </section>
  )
}
