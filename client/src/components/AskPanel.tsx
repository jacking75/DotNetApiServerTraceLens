import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError, askWithMcp, buildContext, cancelAskRun, getDiagnostics, getMcpSetup, openInEditor } from '../api/client'
import { translateError, useI18n } from '../i18n/I18nProvider'
import type { TranslationKey } from '../i18n/ko'
import type { ContextBudget, ContextDocument, ContextIntent, McpSetupPreview, SelectionKind } from '../types/context'
import type { AskMode } from '../types/graph'
import { ContextPreview } from './ContextPreview'
import { HelpLink, type GlossaryTerm } from './HelpDialog'
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
  /** 레시피 배너·프리셋 칩이 밀어 넣는 질문. `token`이 바뀔 때마다 입력칸을 채운다(편집은 그대로 된다). */
  presetQuestion?: { text: string; token: number } | null
  /** 문맥 복사를 마치면 첫 사용 안내의 마지막 단계가 끝난다. */
  onContextCopied?: () => void
  /** 용어집으로 보내는 `?` 링크. */
  onExplain: (term: GlossaryTerm) => void
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
export function AskPanel({ context, snapshotId, presetQuestion, onContextCopied, onExplain }: AskPanelProps) {
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
  /** 오류 원문. 번역한 한 줄 아래에 접어서 남긴다. */
  const [rawError, setRawError] = useState<string | null>(null)
  /**
   * AI CLI가 설치돼 있는지. 보내기 전에 알 수 있어야 한다.
   * 진단 결과는 세션 동안 한 번만 읽는다(매 탭 전환마다 프로세스를 찾지 않는다).
   */
  const [agentMissing, setAgentMissing] = useState<boolean | null>(null)
  /** 에디터 열기가 실패했을 때의 폴백. 자동 이동 대신 사용자가 고른다. */
  const [editorFallback, setEditorFallback] = useState<{ uri: string; location: string } | null>(null)
  /** 서버가 알려 준 실행 ID. 중단하면 SSE만 끊는 것이 아니라 프로세스도 끝낸다. */
  const runIdRef = useRef<string | null>(null)
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
    setRawError(null)
    setEditorFallback(null)
  }, [contextKey])

  // 탭을 처음 열 때 한 번만 확인한다. 실패하면 "모른다"로 두고 배지를 띄우지 않는다.
  useEffect(() => {
    if (mode !== 'mcp' || agentMissing !== null) return
    void (async () => {
      try {
        const report = await getDiagnostics()
        const claude = report.items.find((item) => item.code.startsWith('CLI_CLAUDE_'))
        setAgentMissing(claude?.code === 'CLI_CLAUDE_MISSING')
      } catch {
        setAgentMissing(null)
      }
    })()
  }, [mode, agentMissing])

  useEffect(() => () => abortController.current?.abort(), [])

  // 밖에서 밀어 넣은 질문을 입력칸에 채운다. 같은 문장을 다시 눌러도 반영되도록 token으로 구분한다.
  const presetToken = presetQuestion?.token ?? 0
  const presetText = presetQuestion?.text ?? ''
  useEffect(() => {
    if (presetToken > 0) setQuestion(presetText)
  }, [presetToken]) // eslint-disable-line react-hooks/exhaustive-deps

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
          if (event.type === 'run') runIdRef.current = event.text
          if (event.type === 'delta' && event.text) setAnswer((current) => current + event.text)
          if (event.type === 'log' && event.text) {
            const logText = event.text
            setLogs((current) => [...current, logText])
          }
          if (event.type === 'error') {
            // 서버가 code를 함께 주므로 텍스트 접두사를 파싱하지 않는다. 원문은 접어서 남긴다.
            setMessage(event.code ? translateError(t, event.code, event.text ?? '') : t('ask.agentFailed'))
            setRawError(event.text ?? null)
          }
        },
        controller.signal,
      )
    } catch (reason) {
      if (!controller.signal.aborted) setMessage(describe(reason, 'ask.agentFailed'))
    } finally {
      if (!controller.signal.aborted) setBusy(false)
      runIdRef.current = null
    }
  }

  /** 실행을 중단한다. 연결만 닫으면 CLI 프로세스가 남을 수 있어 서버에도 알린다. */
  const stop = async () => {
    const runId = runIdRef.current
    abortController.current?.abort()
    setBusy(false)
    if (runId) {
      try {
        await cancelAskRun(runId)
      } catch {
        // 이미 끝난 실행이면 404다. 사용자에게 알릴 것이 없다.
      }
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
        setEditorFallback(null)
      } else {
        // 자동 이동은 하지 않는다. vscode:// 핸들러가 없으면 브라우저가 조용히 무시한다.
        setMessage(t('ask.editorFailed'))
        setRawError(result.error)
        const location = context.lines.find((line) => line.label === t('detail.file'))?.value ?? context.id
        setEditorFallback({ uri: result.fallbackUri, location })
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

        {mode === 'mcp' && agentMissing === true && (
          <div className="panel-message tight warn">
            <b>{t('ask.agentMissing')}</b>
            <div className="hint">{t('diagAction.CLI_CLAUDE_MISSING')}</div>
            <a href="https://docs.claude.com/en/docs/claude-code" target="_blank" rel="noreferrer">
              {t('ask.agentInstallGuide')}
            </a>
          </div>
        )}

        {mode === 'editor' && editorFallback && (
          <div className="panel-message tight warn">
            <div>{t('ask.editorFallback', { location: editorFallback.location })}</div>
            <div className="row">
              <button
                type="button"
                className="link-button"
                onClick={() => void navigator.clipboard.writeText(editorFallback.location)
                  .then(() => setMessage(t('ask.copied')), () => setMessage(t('ask.copyFailed')))}
              >
                {t('ask.copyLocation')}
              </button>
              <button
                type="button"
                className="link-button"
                onClick={() => { window.location.href = editorFallback.uri }}
              >
                {t('ask.tryVscodeUri')}
              </button>
            </div>
          </div>
        )}

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
          {mode === 'mcp' && busy && (
            <button type="button" onClick={() => void stop()}>{t('ask.cancel')}</button>
          )}
          <button type="button" className="link-button" onClick={() => void showSetup()}>
            {t('ask.mcpSetup')}
          </button>
          <HelpLink term="mcp" onOpen={onExplain} />
          <button
            type="button"
            className="link-button"
            onClick={() => setShareOpen(true)}
            disabled={!context}
          >
            {t('share.open')}
          </button>
          {message && <div className="copy-message">{message}</div>}
          {rawError && (
            <details className="raw-error">
              <summary>{t('error.showRaw')}</summary>
              <pre>{rawError}</pre>
            </details>
          )}
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
