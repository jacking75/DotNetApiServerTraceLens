import Editor, { type OnMount } from '@monaco-editor/react'
import { useEffect, useRef, useState } from 'react'
import { getSource } from '../api/client'
import type { SourceResponse } from '../types/graph'
import { useI18n } from '../i18n/I18nProvider'

export interface SourceTarget {
  file: string
  startLine: number
  endLine: number
}

/** 코드 여백에 찍는 호출 지점 표시. 누르면 그 호출의 대상 노드로 간다. */
export interface SourceMarker {
  line: number
  targetNodeId: string
  label: string
}

interface SourceViewProps {
  target: SourceTarget | null
  /** 화면 테마. Monaco는 CSS 변수를 읽지 않으므로 직접 넘긴다. */
  theme?: 'dark' | 'light'
  height: number | string
  emptyMessage: string
  /**
   * 강조할 줄. 비우면 아무 줄도 칠하지 않는다.
   * 예전에는 1행부터 끝까지 전체를 칠해서 강조의 의미가 없었다.
   */
  highlightLines?: readonly number[]
  /** 호출 지점 표시. 같은 줄에 여러 개면 목록으로 고르게 한다. */
  markers?: readonly SourceMarker[]
  onSelectMarker?: (marker: SourceMarker) => void
  /**
   * 지금 보는 내용이 분석 당시 스냅샷인지 그 뒤에 바뀐 현재 파일인지 머리에 적는다.
   * 코드를 고친 직후가 도구를 가장 많이 쓰는 순간이라 이 구분이 없으면 잘못된 줄을 읽게 된다.
   */
  showVersion?: boolean
  fullFile?: boolean
}

/** 지정한 파일 구간을 읽어 Monaco로 보여준다. 상세 패널의 "소스" 절에 끼워 넣어 쓴다. */
export function SourceView({
  target,
  theme = 'dark',
  height,
  emptyMessage,
  highlightLines = [],
  markers = [],
  onSelectMarker,
  showVersion = false,
  fullFile = false,
}: SourceViewProps) {
  const { t } = useI18n()
  const [source, setSource] = useState<SourceResponse | null>(null)
  const [error, setError] = useState<string | null>(null)
  /** 같은 줄에 호출이 여러 개일 때 고르게 하는 작은 목록. */
  const [markerChoice, setMarkerChoice] = useState<SourceMarker[] | null>(null)
  const [editor, setEditor] = useState<Parameters<OnMount>[0] | null>(null)
  const decorationsRef = useRef<ReturnType<Parameters<OnMount>[0]['createDecorationsCollection']> | null>(null)
  // Monaco 콜백은 마운트 시점의 값을 잡아 둔다. 최신 값을 ref로 읽는다.
  const markersRef = useRef(markers)
  markersRef.current = markers
  const onSelectMarkerRef = useRef(onSelectMarker)
  onSelectMarkerRef.current = onSelectMarker

  // target 객체는 매 렌더 새로 만들어질 수 있다. 값 세 개만 의존으로 두고 같은 위치면 다시 읽지 않는다.
  const file = target?.file
  const startLine = target?.startLine
  const endLine = target?.endLine
  useEffect(() => {
    setSource(null)
    setEditor(null)
    setError(null)
    setMarkerChoice(null)
    if (file === undefined || startLine === undefined || endLine === undefined) return
    let active = true
    const load = async (): Promise<SourceResponse> => {
      if (!fullFile) return getSource(file, startLine, endLine)
      const chunks: SourceResponse[] = []
      let next = 1
      while (true) {
        const chunk = await getSource(file, next, 2147483647)
        chunks.push(chunk)
        if (!chunk.truncated) break
        if (chunk.endLine < next) break
        next = chunk.endLine + 1
      }
      const first = chunks[0]
      const last = chunks.at(-1)!
      return { ...first, endLine: last.endLine, content: chunks.map((chunk) => chunk.content).join('\n'), truncated: last.truncated,
        sourceVersion: chunks.some((chunk) => chunk.sourceVersion === 'current') ? 'current' : 'snapshot' }
    }
    void load()
      .then((result) => { if (active) setSource(result) })
      .catch((reason: unknown) => { if (active) setError(reason instanceof Error ? reason.message : t('code.loadFailed')) })
    return () => { active = false }
  }, [file, startLine, endLine, fullFile, t])

  useEffect(() => {
    if (!editor || !source) return
    const lineCount = editor.getModel()?.getLineCount() ?? 0
    const toEditorLine = (absolute: number) => absolute - source.startLine + 1
    const inRange = (absolute: number) => toEditorLine(absolute) >= 1 && toEditorLine(absolute) <= lineCount
    decorationsRef.current?.clear()
    decorationsRef.current = editor.createDecorationsCollection([
      ...highlightLines.filter(inRange).map((absolute) => ({
        range: { startLineNumber: toEditorLine(absolute), startColumn: 1, endLineNumber: toEditorLine(absolute), endColumn: 1 },
        options: { isWholeLine: true, className: 'tracelens-highlight' },
      })),
      ...markers.filter((marker) => inRange(marker.line)).map((marker) => ({
        range: { startLineNumber: toEditorLine(marker.line), startColumn: 1, endLineNumber: toEditorLine(marker.line), endColumn: 1 },
        options: { isWholeLine: true, className: 'tracelens-callsite', glyphMarginClassName: 'tracelens-callsite-glyph', glyphMarginHoverMessage: { value: marker.label } },
      })),
    ])
    if (fullFile && highlightLines.length > 0 && inRange(highlightLines[0])) editor.revealLineInCenter(toEditorLine(highlightLines[0]))
  }, [editor, source, highlightLines, markers, fullFile])

  const handleMount: OnMount = (editor) => {
    if (!source) return
    setEditor(editor)

    editor.onMouseDown((event) => {
      const clicked = event.target.position?.lineNumber
      if (clicked === undefined) return
      const absolute = source.startLine + clicked - 1
      const hits = markersRef.current.filter((marker) => marker.line === absolute)
      if (hits.length === 0) return
      if (hits.length === 1) {
        onSelectMarkerRef.current?.(hits[0])
        return
      }

      setMarkerChoice(hits)
    })
  }

  if (error) return <div className="panel-message error">{error}</div>
  if (!source) return <div className="panel-message">{emptyMessage}</div>

  return (
    <div className="source-view" style={{ height }}>
      {showVersion && (
        <div className={`source-version${source.sourceVersion === 'current' ? ' warn' : ''}`}>
          {source.sourceVersion === 'current' ? t('code.versionCurrent') : t('code.versionSnapshot')}
          {source.truncated && ` · ${t('code.truncated')}`}
        </div>
      )}
      {markerChoice && (
        <div className="marker-choice">
          <span className="hint">{t('source.pickCall')}</span>
          {markerChoice.map((marker) => (
            <button
              key={marker.targetNodeId}
              type="button"
              className="link-button"
              onClick={() => { onSelectMarker?.(marker); setMarkerChoice(null) }}
            >
              {marker.label}
            </button>
          ))}
          <button type="button" className="link-button" onClick={() => setMarkerChoice(null)}>{t('ask.close')}</button>
        </div>
      )}
      <Editor
        height="100%"
        language="csharp"
        theme={theme === 'light' ? 'vs' : 'vs-dark'}
        value={source.content}
        onMount={handleMount}
        options={{
          readOnly: true,
          minimap: { enabled: false },
          glyphMargin: markers.length > 0,
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
