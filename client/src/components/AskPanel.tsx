import { useState } from 'react'
import type { GraphNode } from '../types/graph'

interface AskPanelProps {
  node: GraphNode | null
}

export function AskPanel({ node }: AskPanelProps) {
  const [question, setQuestion] = useState('')
  const [message, setMessage] = useState<string | null>(null)

  const copy = async () => {
    if (!node || !question.trim()) return
    const text = `파일: ${node.file}\n라인: ${node.startLine}-${node.endLine}\n심볼: ${node.displayName}\n\n질문: ${question.trim()}`
    try {
      await navigator.clipboard.writeText(text)
      setMessage('클립보드에 복사했다. Claude Code 터미널에 붙여넣으면 된다.')
    } catch {
      setMessage('클립보드에 접근하지 못했다. 브라우저 권한을 확인해야 한다.')
    }
  }

  return (
    <section className="panel ask-panel">
      <header>
        <span>Ask Claude Code</span>
        <small>클립보드 복사</small>
      </header>
      <div className="captured-context">
        {node ? (
          <>
            파일: <b>{node.file}</b><br />
            라인: <b>{node.startLine}-{node.endLine}</b><br />
            심볼: <b>{node.displayName}</b>
          </>
        ) : '질문할 노드를 먼저 선택해야 한다.'}
      </div>
      <textarea
        value={question}
        onChange={(event) => setQuestion(event.target.value)}
        placeholder="이 코드의 역할이나 호출 흐름을 질문한다."
        disabled={!node}
      />
      <button type="button" className="primary" onClick={() => void copy()} disabled={!node || !question.trim()}>
        클립보드 복사
      </button>
      {message && <div className="copy-message">{message}</div>}
    </section>
  )
}
