import { useEffect, useRef, useState } from 'react'
import { askWithMcp, openInEditor } from '../api/client'
import type { AskMode } from '../types/graph'

/** 질문 패널이 캡처하는 컨텍스트. 메서드뿐 아니라 클래스·네임스페이스 선택에도 대응한다. */
export interface AskContext {
  /** MCP 연동과 에디터 열기에 쓰는 노드 id. 클래스·네임스페이스 선택에는 없을 수 있다. */
  nodeId: string | null
  lines: Array<{ label: string; value: string }>
  clipboardText: string
}

interface AskPanelProps {
  context: AskContext | null
}

const modeLabels: Record<AskMode, string> = {
  clipboard: '클립보드',
  mcp: 'MCP 직접 연동',
  editor: '에디터',
}

export function AskPanel({ context }: AskPanelProps) {
  const [mode, setMode] = useState<AskMode>('clipboard')
  const [question, setQuestion] = useState('')
  const [answer, setAnswer] = useState('')
  const [logs, setLogs] = useState<string[]>([])
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const abortController = useRef<AbortController | null>(null)
  const contextKey = context?.clipboardText ?? null

  useEffect(() => {
    abortController.current?.abort()
    setAnswer('')
    setLogs([])
    setMessage(null)
    setBusy(false)
  }, [contextKey])

  useEffect(() => () => abortController.current?.abort(), [])

  const copy = async () => {
    if (!context || !question.trim()) return
    const text = `${context.clipboardText}\n\n질문: ${question.trim()}`
    try {
      await navigator.clipboard.writeText(text)
      setMessage('클립보드에 복사했다. Claude Code 터미널에 붙여넣으면 된다.')
    } catch {
      setMessage('클립보드에 접근하지 못했다. 브라우저 권한을 확인해야 한다.')
    }
  }

  const ask = async () => {
    if (!context?.nodeId || !question.trim()) return
    abortController.current?.abort()
    const controller = new AbortController()
    abortController.current = controller
    setBusy(true)
    setAnswer('')
    setLogs([])
    setMessage(null)
    try {
      await askWithMcp(
        { nodeId: context.nodeId, question: question.trim(), mode: 'mcp' },
        (event) => {
          if (event.type === 'delta' && event.text) setAnswer((current) => current + event.text)
          if (event.type === 'log' && event.text) {
            const logText = event.text
            setLogs((current) => [...current, logText])
          }
          if (event.type === 'error') setMessage(event.text ?? 'Claude Code 실행에 실패했다.')
        },
        controller.signal,
      )
    } catch (reason) {
      if (!controller.signal.aborted) {
        setMessage(reason instanceof Error ? reason.message : 'Claude Code 요청에 실패했다.')
      }
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
        setMessage('선택한 위치를 에디터에서 열었다.')
      } else {
        setMessage(result.error ?? 'code 명령을 실행하지 못해 URI 방식으로 다시 시도한다.')
        window.location.href = result.fallbackUri
      }
    } catch (reason) {
      setMessage(reason instanceof Error ? reason.message : '에디터를 열지 못했다.')
    } finally {
      setBusy(false)
    }
  }

  const run = () => {
    if (mode === 'clipboard') void copy()
    if (mode === 'mcp') void ask()
    if (mode === 'editor') void openEditor()
  }

  const needsQuestion = mode !== 'editor'
  // MCP 연동과 에디터 열기는 노드 id가 있어야 한다. 클래스·네임스페이스만 고른 상태면 클립보드만 쓸 수 있다.
  const needsNodeId = mode !== 'clipboard'
  const blocked = needsNodeId && !context?.nodeId

  return (
    <section className="panel ask-panel">
      <header>
        <span>Claude에게 질문</span>
        <small>{modeLabels[mode]}</small>
      </header>
      <div className="ask-mode-tabs" role="tablist" aria-label="Claude Code 연동 방식">
        {(Object.keys(modeLabels) as AskMode[]).map((item) => (
          <button
            type="button"
            role="tab"
            aria-selected={mode === item}
            className={mode === item ? 'active' : ''}
            onClick={() => { setMode(item); setMessage(null) }}
            key={item}
          >
            {modeLabels[item]}
          </button>
        ))}
      </div>
      <div className="ask-scroll">
        <div className="captured-context">
          {context
            ? context.lines.map((line) => (
              <div key={line.label}>{line.label}: <b>{line.value}</b></div>
            ))
            : '질문할 항목을 먼저 선택해야 한다.'}
        </div>
        {blocked && context && (
          <div className="panel-message tight">
            이 선택에는 실행 가능한 노드가 없어서 클립보드 복사만 쓸 수 있다.
          </div>
        )}
        {mode !== 'editor' && (
          <textarea
            value={question}
            onChange={(event) => setQuestion(event.target.value)}
            placeholder="이 클래스가 어떤 인터페이스로 주입되는지, 이 메서드의 트랜잭션 경계가 어디인지 물어본다."
            disabled={!context || busy}
          />
        )}
        {mode === 'mcp' && (question.trim() || answer) && (
          <div className="ask-response" aria-live="polite">
            {question.trim() && <div className="chat-bubble user">{question.trim()}</div>}
            {(answer || busy) && <div className="chat-bubble assistant">{answer || 'Claude Code가 확인 중이다…'}</div>}
            {logs.length > 0 && <details><summary>실행 로그 {logs.length}줄</summary><pre>{logs.join('\n')}</pre></details>}
          </div>
        )}
        <div className="ask-actions">
          <button
            type="button"
            className="primary"
            onClick={run}
            disabled={!context || busy || blocked || (needsQuestion && !question.trim())}
          >
            {busy ? '처리 중…' : mode === 'clipboard' ? '클립보드 복사' : mode === 'mcp' ? '질문 보내기' : '에디터에서 열기'}
          </button>
          {message && <div className="copy-message">{message}</div>}
        </div>
      </div>
    </section>
  )
}
