import { lazy, Suspense, useMemo } from 'react'
import { memberKey } from '../graph/classDiagram'
import { findStartupStep } from '../graph/startupFlow'
import { useI18n } from '../i18n/I18nProvider'
import type { GraphDocument, Selection, StartupFlowDocument } from '../types/graph'
import type { SourceTarget } from './CodePanel'
import type { SourceLocation } from './SourceLink'

const SourceView = lazy(() => import('./CodePanel').then((module) => ({ default: module.SourceView })))

/** `파일:줄`로 온 위치 주변에 함께 보여 줄 줄 수. 호출 한 줄만 보면 문맥을 알 수 없다. */
const callContextLines = 15

interface CodeAreaProps {
  graph: GraphDocument | null
  startupFlow?: StartupFlowDocument | null
  selection: Selection | null
  /** `파일:줄`을 눌러 연 임시 위치. 있으면 선택 항목의 선언 대신 이 구간을 보여 준다. */
  sourceTarget: SourceLocation | null
  /** 임시 보기를 접고 선택 항목의 선언으로 돌아간다. */
  onClearSourceTarget: () => void
  theme: 'dark' | 'light'
  height: number | string
  /** 여백의 호출 지점을 누르면 그 대상 노드를 선택한다. */
  onSelectNode: (nodeId: string) => void
}

/**
 * 우측 열 맨 위의 공용 코드 보기. 어떤 보기에서 무엇을 골랐든 여기 한 곳이 소스를 연다.
 * 화면의 모든 `파일:줄`이 이 자리를 움직이므로 상세 패널마다 코드 보기를 따로 두지 않는다.
 */
export function CodeArea({
  graph,
  startupFlow,
  selection,
  sourceTarget,
  onClearSourceTarget,
  theme,
  height,
  onSelectNode,
}: CodeAreaProps) {
  const { t } = useI18n()
  const nodeNameById = useMemo(
    () => new Map((graph?.nodes ?? []).map((item) => [item.id, item.displayName])),
    [graph])

  /** 선택한 항목의 선언 구간. `파일:줄`로 옮겨 간 뒤에도 "돌아가기"의 목적지가 된다. */
  const declaration = useMemo(() => declarationOf(graph, selection, startupFlow), [graph, selection, startupFlow])
  const startupLine = useMemo(() => {
    if (!graph || !startupFlow || selection?.type !== 'startupStep') return null
    let key = selection.key
    while (key) {
      const step = findStartupStep(graph, key, startupFlow)
      if (step?.file === startupFlow.programFile) return step.line
      const separator = key.lastIndexOf('.')
      if (separator < 0) break
      key = key.slice(0, separator)
    }
    return null
  }, [graph, selection, startupFlow])

  const shown: SourceTarget | null = startupFlow
    ? { file: startupFlow.programFile, startLine: 1, endLine: Number.MAX_SAFE_INTEGER }
    : sourceTarget
    ? {
      file: sourceTarget.file,
      startLine: Math.max(1, sourceTarget.line - callContextLines),
      endLine: (sourceTarget.endLine ?? sourceTarget.line) + callContextLines,
    }
    : declaration

  const highlightLines = startupFlow
    ? startupLine === null ? [] : [startupLine]
    : sourceTarget ? [sourceTarget.line] : declaration ? [declaration.startLine] : []

  /** 지금 보는 파일 안의 호출 지점. 여백 표시를 누르면 그 호출의 대상 노드로 간다. */
  const shownFile = shown?.file
  const markers = useMemo(() => {
    if (!graph || !shownFile || selection?.type !== 'method') return []
    return graph.callSites
      .filter((site) => site.from === selection.key && site.to.length > 0 && site.file === shownFile)
      .map((site) => ({
        line: site.line,
        targetNodeId: site.to,
        label: nodeNameById.get(site.to) ?? site.to,
      }))
  }, [graph, shownFile, selection, nodeNameById])

  return (
    <section className="panel code-area" id="tracelens-code-panel">
      <header>
        <span>{t('code.title')}</span>
        <small>{shown ? startupFlow ? shown.file : `${shown.file}:${shown.startLine}` : ''}</small>
        {!startupFlow && sourceTarget && declaration && (
          <button type="button" className="link-button" onClick={onClearSourceTarget}>
            {t('code.backToDeclaration')}
          </button>
        )}
      </header>
      {shown
        ? (
          <Suspense fallback={<div className="panel-message">{t('detail.loadingSource')}</div>}>
            <SourceView
              target={shown}
              theme={theme}
              height={height}
              emptyMessage={t('detail.loadingSource')}
              highlightLines={highlightLines}
              markers={markers}
              onSelectMarker={(marker) => onSelectNode(marker.targetNodeId)}
              showVersion
              fullFile={!!startupFlow}
            />
          </Suspense>
        )
        : <div className="panel-message">{t('code.pickSomething')}</div>}
    </section>
  )
}

/** 선택한 항목의 선언 구간. 종류마다 선언이 있는 자리가 다르다. */
export function declarationOf(graph: GraphDocument | null, selection: Selection | null, startupFlow?: StartupFlowDocument | null): SourceTarget | null {
  if (!graph || !selection) return null

  if (selection.type === 'method') {
    const node = graph.nodes.find((item) => item.id === selection.key)
    if (node) return { file: node.file, startLine: node.startLine, endLine: node.endLine }
    for (const item of graph.classes) {
      const member = item.members.find((candidate) => memberKey(item.id, candidate) === selection.key)
      if (member) return { file: item.file, startLine: member.startLine, endLine: member.endLine }
    }

    return null
  }

  if (selection.type === 'class') {
    const item = graph.classes.find((candidate) => candidate.id === selection.key)
    return item ? { file: item.file, startLine: item.startLine, endLine: item.endLine } : null
  }

  if (selection.type === 'finding') {
    const finding = [...(graph.dataAccess?.findings ?? []), ...(graph.middleware?.findings ?? [])]
      .find((item) => item.id === selection.key)
    return finding ? { file: finding.file, startLine: finding.line, endLine: finding.line } : null
  }

  if (selection.type === 'writePath') {
    const path = graph.dataAccess?.writePaths.find((item) => item.id === selection.key)
    return path ? { file: path.file, startLine: path.startLine, endLine: path.startLine } : null
  }

  if (selection.type === 'middlewareStep') {
    const step = graph.middleware?.steps.find((item) => item.id === selection.key)
    if (!step) return null
    return step.implementationFile
      ? { file: step.implementationFile, startLine: step.implementationLine, endLine: step.implementationLine }
      : { file: step.registrationFile, startLine: step.registrationLine, endLine: step.registrationLine }
  }

  if (selection.type === 'startupStep') {
    const step = findStartupStep(graph, selection.key, startupFlow)
    return step ? { file: step.file, startLine: step.line, endLine: step.line } : null
  }

  return null
}
