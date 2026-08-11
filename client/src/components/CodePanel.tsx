import Editor, { type OnMount } from '@monaco-editor/react'
import { useEffect, useState } from 'react'
import { getSource } from '../api/client'
import type { GraphNode, SourceResponse } from '../types/graph'

interface CodePanelProps {
  node: GraphNode | null
}

export function CodePanel({ node }: CodePanelProps) {
  const [source, setSource] = useState<SourceResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setSource(null)
    setError(null)
    if (!node) return
    void getSource(node.file, node.startLine, node.endLine)
      .then(setSource)
      .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : '소스를 불러오지 못했다.'))
  }, [node])

  const handleMount: OnMount = (editor) => {
    if (!source) return
    const lineCount = Math.max(1, source.content.split(/\r?\n/).length)
    editor.createDecorationsCollection([
      {
        range: { startLineNumber: 1, startColumn: 1, endLineNumber: lineCount, endColumn: 1 },
        options: { isWholeLine: true, className: 'tracelens-highlight' },
      },
    ])
  }

  return (
    <section className="panel code-panel">
      <header>
        <span>Source</span>
        <small>{node ? `${node.file}:${node.startLine}-${node.endLine}` : '노드를 선택해야 한다'}</small>
      </header>
      {error && <div className="panel-message error">{error}</div>}
      {!error && !source && <div className="panel-message">그래프의 노드를 선택하면 실제 코드가 표시된다.</div>}
      {source && (
        <Editor
          height="100%"
          language="csharp"
          theme="vs-dark"
          value={source.content}
          onMount={handleMount}
          options={{
            readOnly: true,
            minimap: { enabled: false },
            lineNumbers: (line) => String(source.startLine + line - 1),
            scrollBeyondLastLine: false,
            fontSize: 13,
            padding: { top: 16 },
            automaticLayout: true,
          }}
        />
      )}
    </section>
  )
}
