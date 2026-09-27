import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError, askWithMcp, buildContext, cancelAskRun, getMcpSetup, openInEditor } from '../api/client'
import { agentInfo, type AgentId } from '../agents'
import { recipes } from '../content/recipes'
import { translateError, useI18n } from '../i18n/I18nProvider'
import type { TranslationKey } from '../i18n/ko'
import type { ContextBudget, ContextDocument, ContextIntent, DiagnosticsReport, McpSetupPreview, SelectionKind } from '../types/context'
import type { AskMode } from '../types/graph'
import { ContextPreview } from './ContextPreview'
import { HelpLink, type GlossaryTerm } from './HelpDialog'
import { McpSetupDialog } from './McpSetupDialog'
import type { SourceLocation } from './SourceLink'
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
  agent: AgentId
  diagnostics: DiagnosticsReport | null
  snapshotId: string
  /**
   * 질문 초안. 우측 열을 접었다 펴거나 탭을 바꿔도 남아야 하므로 App이 들고 있는다.
   * 레시피 배너·프리셋 칩도 이 값을 바꿔서 입력칸을 채운다.
   */
  question: string
  onChangeQuestion: (value: string) => void
  /** 최근 답변. 같은 이유로 App이 들고 있는다(새로고침 전까지 유지). */
  recentAnswers: RecentAnswer[]
  onPushRecentAnswer: (item: RecentAnswer) => void
  /** 문맥 복사를 마치면 첫 사용 안내의 마지막 단계가 끝난다. */
  onContextCopied?: () => void
  /** 용어집으로 보내는 `?` 링크. */
  onExplain: (term: GlossaryTerm) => void
  /** 문맥 미리보기의 `파일:줄`을 누르면 코드 보기를 그 위치로 옮긴다. */
  onOpenSource: (location: SourceLocation) => void
  /** Shift+클릭·컨텍스트 메뉴로 에디터에서 연다. */
  onOpenInEditor: (location: SourceLocation) => void
}

const modeKeys: Record<AskMode, TranslationKey> = {
  context: 'ask.modeContext',
  mcp: 'ask.modeAgent',
  editor: 'ask.modeEditor',
}

const intents: ReadonlyArray<{ value: ContextIntent; key: TranslationKey; hintKey: TranslationKey }> = [
  { value: 'Explain', key: 'ask.intent.explain', hintKey: 'ask.intentHint.explain' },
  { value: 'PlanChange', key: 'ask.intent.planChange', hintKey: 'ask.intentHint.planChange' },
  { value: 'ReviewChange', key: 'ask.intent.reviewChange', hintKey: 'ask.intentHint.reviewChange' },
  { value: 'InvestigateData', key: 'ask.intent.investigateData', hintKey: 'ask.intentHint.investigateData' },
]

const budgets: ReadonlyArray<{ value: ContextBudget; key: TranslationKey; hintKey: TranslationKey }> = [
  { value: 'Short', key: 'ask.budget.short', hintKey: 'ask.budgetHint.short' },
  { value: 'Balanced', key: 'ask.budget.balanced', hintKey: 'ask.budgetHint.balanced' },
  { value: 'Detailed', key: 'ask.budget.detailed', hintKey: 'ask.budgetHint.detailed' },
]

/** 보관하는 최근 답변 개수. 소스 본문이 섞일 수 있어 localStorage에는 쓰지 않는다(새로고침하면 사라진다). */
export const maxRecentAnswers = 5

export interface RecentAnswer {
  selectionLabel: string
  question: string
  answer: string
  at: string
}

/**
 * 선택한 위치를 "작업 문맥"으로 바꿔 AI에 전달한다.
 * 문맥은 서버의 `POST /api/context` 하나에서 나오므로 클립보드·인앱 실행·MCP가 같은 내용을 쓴다.
 */
export function AskPanel({
  context,
  agent,
  diagnostics,
  snapshotId,
  question,
  onChangeQuestion,
  recentAnswers,
  onPushRecentAnswer,
  onContextCopied,
  onExplain,
  onOpenSource,
  onOpenInEditor,
}: AskPanelProps) {
  const { t, lang } = useI18n()
  const [mode, setMode] = useState<AskMode>('context')
  const [intent, setIntent] = useState<ContextIntent>('Explain')
  const [budget, setBudget] = useState<ContextBudget>('Balanced')
  const [includeSource, setIncludeSource] = useState(true)
  const setQuestion = onChangeQuestion
  const [preview, setPreview] = useState<ContextDocument | null>(null)
  const [answer, setAnswer] = useState('')
  const [logs, setLogs] = useState<string[]>([])
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [runAgent, setRunAgent] = useState<AgentId | null>(null)
  const [setup, setSetup] = useState<McpSetupPreview | null>(null)
  /** 미리보기에서 체크를 끈 파일. 문맥 생성과 에이전트 실행 양쪽에 그대로 넘긴다. */
  const [excludedFiles, setExcludedFiles] = useState<string[]>([])
  /** 빈 모델은 선택한 CLI의 기본값이다. CLI를 바꾸면 이전 모델 이름은 지운다. */
  const [model, setModel] = useState('')
  const [shareOpen, setShareOpen] = useState(false)
  /** 오류 원문. 번역한 한 줄 아래에 접어서 남긴다. */
  const [rawError, setRawError] = useState<string | null>(null)
  /** 에디터 열기가 실패했을 때의 폴백. 자동 이동 대신 사용자가 고른다. */
  const [editorFallback, setEditorFallback] = useState<{ uri: string; location: string } | null>(null)
  /** 서버가 알려 준 실행 ID. 중단하면 SSE만 끊는 것이 아니라 프로세스도 끝낸다. */
  const runIdRef = useRef<string | null>(null)
  const abortController = useRef<AbortController | null>(null)
  const contextKey = context ? `${context.kind}:${context.id}` : null
  const selectedAgent = agentInfo(agent)
  const agentMissing = diagnostics?.items.some((check) => check.code === selectedAgent.missingCode) ?? false
  /** 지금 고른 목적에 맞는 레시피의 프리셋 질문. 없으면 칩을 그리지 않는다. */
  const presets = recipes.find((recipe) => recipe.intent === intent)
    ? (lang === 'ko'
      ? recipes.find((recipe) => recipe.intent === intent)!.presetQuestionsKo
      : recipes.find((recipe) => recipe.intent === intent)!.presetQuestionsEn)
    : []

  const describe = useCallback((reason: unknown, fallbackKey: TranslationKey) =>
    reason instanceof ApiError ? translateError(t, reason.code, reason.message) : t(fallbackKey), [t])

  // 선택이 바뀌기 직전의 답을 최근 목록으로 옮긴다. ref로 읽어야 정리 시점의 값을 본다.
  const answerRef = useRef({ answer: '', question: '', label: '' })
  answerRef.current = {
    answer,
    question,
    label: context ? `${context.kind} · ${context.id}` : '',
  }

  const pushRecentRef = useRef(onPushRecentAnswer)
  pushRecentRef.current = onPushRecentAnswer
  useEffect(() => () => {
    const current = answerRef.current
    if (!current.answer.trim()) return
    pushRecentRef.current({
      selectionLabel: current.label,
      question: current.question,
      answer: current.answer,
      at: new Date().toISOString(),
    })
  }, [contextKey])

  useEffect(() => {
    abortController.current?.abort()
    setAnswer('')
    setLogs([])
    setMessage(null)
    setPreview(null)
    setBusy(false)
    setRunAgent(null)
    setExcludedFiles([])
    setShareOpen(false)
    setRawError(null)
    setEditorFallback(null)
  }, [contextKey])

  useEffect(() => setModel(''), [agent])

  useEffect(() => () => abortController.current?.abort(), [])

  /**
   * 선택·목적·예산이 바뀌면 미리보기를 다시 만든다. 이 단계에는 LLM 호출이 없다.
   * 질문 본문은 여기 넣지 않는다 — 넣으면 한 글자 칠 때마다 서버가 소스를 다시 읽는다.
   * 질문은 실제로 보낼 때(`ask`) 합친다.
   */
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
  }, [context, contextKey, intent, budget, includeSource, excludedFiles, snapshotId, describe])

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
    setRunAgent(agent)
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
          agent,
          model: model.trim() || undefined,
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
            setMessage(event.code ? translateError(t, event.code, event.text ?? '') : t('ask.agentFailed', { agent: selectedAgent.label }))
            setRawError(event.text ?? null)
          }
        },
        controller.signal,
      )
    } catch (reason) {
      if (!controller.signal.aborted) setMessage(reason instanceof ApiError
        ? translateError(t, reason.code, reason.message)
        : t('ask.agentFailed', { agent: selectedAgent.label }))
    } finally {
      if (!controller.signal.aborted) {
        setBusy(false)
        setRunAgent(null)
      }
      runIdRef.current = null
    }
  }

  /** 실행을 중단한다. 연결만 닫으면 CLI 프로세스가 남을 수 있어 서버에도 알린다. */
  const stop = async () => {
    const runId = runIdRef.current
    abortController.current?.abort()
    setBusy(false)
    setRunAgent(null)
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
        <small>{t(modeKeys[mode], { agent: selectedAgent.label })}</small>
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
            {t(modeKeys[item], { agent: selectedAgent.label })}
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

        {mode === 'mcp' && agentMissing && (
          <div className="panel-message tight warn">
            <b>{t('ask.agentMissing', { command: selectedAgent.command })}</b>
            <div className="hint">{t(`diagAction.${selectedAgent.missingCode}`)}</div>
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
              {mode === 'mcp' && (
                <>
                  <label>
                    {t('ask.model')}
                    <input
                      value={model}
                      onChange={(event) => setModel(event.target.value)}
                      placeholder={t('ask.modelDefault')}
                      aria-label={t('ask.model')}
                    />
                  </label>
                </>
              )}
            </div>
            <div className="hint">
              {t(intents.find((item) => item.value === intent)?.hintKey ?? 'ask.intentHint.explain')}
              {' · '}
              {t(budgets.find((item) => item.value === budget)?.hintKey ?? 'ask.budgetHint.balanced')}
            </div>
            {presets.length > 0 && (
              <div className="ask-presets">
                <span className="hint">{t('ask.presets')}</span>
                {presets.map((preset) => (
                  <button
                    key={preset}
                    type="button"
                    className="chipbtn"
                    title={preset}
                    onClick={() => setQuestion(preset)}
                  >
                    {preset}
                  </button>
                ))}
              </div>
            )}
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
            onOpenSource={onOpenSource}
            onOpenInEditor={onOpenInEditor}
            excludedFiles={excludedFiles}
            onToggleFile={(file, include) => setExcludedFiles((current) =>
              include ? current.filter((item) => item !== file) : [...current, file])}
          />
        )}

        {mode === 'mcp' && (question.trim() || answer) && (
          <div className="ask-response" aria-live="polite">
            {question.trim() && <div className="chat-bubble user">{question.trim()}</div>}
            {(answer || busy) && <div className="chat-bubble assistant">{answer || t('ask.working', { agent: agentInfo(runAgent ?? agent).label })}</div>}
            {answer && (
              <button
                type="button"
                className="link-button"
                onClick={() => void navigator.clipboard.writeText(answer)
                  .then(() => setMessage(t('ask.answerCopied')), () => setMessage(t('ask.copyFailed')))}
              >
                {t('ask.copyAnswer')}
              </button>
            )}
            {logs.length > 0 && (
              <details>
                <summary>{t('ask.logs', { count: logs.length })}</summary>
                <pre>{logs.join('\n')}</pre>
              </details>
            )}
          </div>
        )}

        {recentAnswers.length > 0 && (
          <details className="recent-answers">
            <summary>{t('ask.recentAnswers', { count: recentAnswers.length })}</summary>
            <ul>
              {recentAnswers.map((item) => (
                <li key={`${item.at}${item.selectionLabel}`}>
                  <div className="hint"><code>{item.selectionLabel}</code></div>
                  {item.question && <div className="hint">{item.question}</div>}
                  <pre>{item.answer}</pre>
                </li>
              ))}
            </ul>
          </details>
        )}

        <div className="ask-actions">
          <button
            type="button"
            className="primary"
            onClick={run}
            disabled={!context || busy || blocked || (needsQuestion && !question.trim()) || (mode === 'context' && !preview) || (mode === 'mcp' && agentMissing)}
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

        {setup && <McpSetupDialog setup={setup} preferredHost={selectedAgent.label} onClose={() => setSetup(null)} />}
      </div>

      {shareOpen && context && (
        <ShareReportDialog kind={context.kind} id={context.id} onClose={() => setShareOpen(false)} />
      )}
    </section>
  )
}
