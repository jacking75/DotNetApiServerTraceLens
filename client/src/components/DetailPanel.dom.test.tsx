/**
 * @vitest-environment jsdom
 */
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { I18nProvider } from '../i18n/I18nProvider'
import { DetailPanel, type DetailActions } from './DetailPanel'
import type { GraphDocument, GraphNode } from '../types/graph'

const callerCount = 3000

function node(id: string): GraphNode {
  return {
    id,
    kind: 'Service',
    displayName: id,
    namespace: 'N',
    className: 'C',
    file: 'C.cs',
    startLine: 1,
    endLine: 2,
  } as unknown as GraphNode
}

const target = node('N.C.Target()')
const callers = Array.from({ length: callerCount }, (_, index) => node(`N.C.Caller${index}()`))

const graph = {
  schemaVersion: 7,
  solutionPath: 'C:/x/X.sln',
  rootDirectory: 'C:/x',
  analyzedAt: '2026-09-19T00:00:00+09:00',
  entryPoints: [],
  classes: [],
  nodes: [target, ...callers],
  edges: callers.map((caller) => ({ from: caller.id, to: target.id })),
  callSites: [],
  fileHashes: {},
} as unknown as GraphDocument

const actions = {
  onSelect: vi.fn(),
  onShowFlow: vi.fn(),
  onShowInDiagram: vi.fn(),
  onFocusOnly: vi.fn(),
  onGoToNamespace: vi.fn(),
  onExplain: vi.fn(),
  onOpenSource: vi.fn(),
  onOpenInEditor: vi.fn(),
  onShowCallerWindow: vi.fn(),
} satisfies DetailActions

afterEach(cleanup)

describe('DetailPanel', () => {
  /**
   * 호출자가 3000개인 메서드가 실제로 있다. 전부 그리면 노드를 고르는 것만으로 화면이 멎는다.
   * 상한이 사라지면 이 테스트가 먼저 깨진다.
   */
  it('호출자 목록을 한 번에 50개까지만 그리고 더 보기로 늘린다', async () => {
    render(
      <I18nProvider>
        <DetailPanel
          graph={graph}
          selection={{ type: 'method', key: target.id }}
          relations={[]}
          namespaces={[]}
          entryPoints={[]}
          namespacePrefix=""
          incomingByNode={new Map([[target.id, callers.map((caller) => caller.id)]])}
          outgoingByNode={new Map()}
          actions={actions}
        />
      </I18nProvider>,
    )

    const pills = () => screen.getAllByRole('button').filter((item) => item.className.includes('pill'))
    expect(pills()).toHaveLength(50)

    // 언어에 매이지 않게 남은 개수로 찾는다(테스트의 기본 언어는 영어다).
    const more = screen.getAllByRole('button')
      .find((item) => item.className.includes('link-button') && item.textContent?.includes(`${callerCount - 50}`))
    expect(more).toBeDefined()
    await userEvent.click(more!)
    expect(pills()).toHaveLength(100)
  })
})
