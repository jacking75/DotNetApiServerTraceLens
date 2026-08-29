import Editor, { type OnMount } from '@monaco-editor/react'
import { useEffect, useState } from 'react'
import { getSource } from '../api/client'
import type { SourceResponse } from '../types/graph'

export interface SourceTarget {
  file: string
  startLine: number
  endLine: number
}

interface SourceViewProps {
  target: SourceTarget | null
  height: number | string
  emptyMessage: string
}

/** 지정한 파일 구간을 읽어 Monaco로 보여준다. 상세 패널의 "소스" 절에 끼워 넣어 쓴다. */
export function SourceView({ target, height, emptyMessage }: SourceViewProps) {
  const [source, setSource] = useState<SourceResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setSource(null)
    setError(null)
    if (!target) return
    void getSource(target.file, target.startLine, target.endLine)
      .then(setSource)
      .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : '소스를 불러오지 못했다.'))
  }, [target?.file, target?.startLine, target?.endLine]) // eslint-disable-line react-hooks/exhaustive-deps

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

  if (error) return <div className="panel-message error">{error}</div>
  if (!source) return <div className="panel-message">{emptyMessage}</div>

  return (
    <div className="source-view" style={{ height }}>
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
          fontSize: 12,
          padding: { top: 10 },
          automaticLayout: true,
        }}
      />
    </div>
  )
}
