/** @vitest-environment jsdom */
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { I18nProvider } from '../i18n/I18nProvider'
import { entryPoint } from '../types/testFactories'
import type { GraphDocument } from '../types/graph'
import { QuickFind } from './QuickFind'

const first = entryPoint('Orders.First()', 'GET', '/orders')
const second = entryPoint('Orders.Second()', 'POST', '/orders')
const graph = { entryPoints: [first, second], nodes: [], classes: [] } as unknown as GraphDocument
beforeEach(() => localStorage.setItem('tracelens.lang', 'ko'))
afterEach(cleanup)
function show(recent: string[] = []) {
  const onPick = vi.fn()
  const onClose = vi.fn()
  render(<I18nProvider><QuickFind graph={graph} recent={recent} bookmarks={[]} onPickBookmark={vi.fn()}
    onPick={onPick} onClose={onClose} /></I18nProvider>)
  return { onPick, onClose }
}

it('실제로 포커스한 검색 결과를 Enter로 한 번만 연다', async () => {
  const { onPick } = show()
  await userEvent.type(screen.getByRole('textbox'), 'orders')
  const row = screen.getByRole('button', { name: /POST \/orders/ })
  row.focus()
  await userEvent.keyboard('{Enter}')
  expect(onPick).toHaveBeenCalledTimes(1)
  expect(onPick.mock.calls[0][0].id).toBe(second.endpointId)
})

it('검색창에서 방향키로 결과로 이동하고 Home·End로 결과 끝까지 이동한다', async () => {
  show()
  await userEvent.type(screen.getByRole('textbox'), 'orders')
  await userEvent.keyboard('{ArrowDown}{End}')
  expect(document.activeElement).toBe(screen.getByRole('button', { name: /POST \/orders/ }))
  await userEvent.keyboard('{Home}')
  expect(document.activeElement).toBe(screen.getByRole('button', { name: /GET \/orders/ }))
})

it('최근 항목도 방향키와 Enter로 열며 삭제된 항목은 보여주지 않는다', async () => {
  const { onPick } = show([`entryPoint:${second.endpointId}`, 'method:deleted'])
  expect(screen.queryByText('deleted')).toBeNull()
  await userEvent.keyboard('{ArrowDown}{Enter}')
  expect(onPick.mock.calls[0][0].label).toBe('POST /orders')
})

it('한글 조합 중 Enter는 결과를 열지 않는다', async () => {
  const { onPick } = show()
  await userEvent.type(screen.getByRole('textbox'), 'orders')
  fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Enter', isComposing: true })
  expect(onPick).not.toHaveBeenCalled()
})

it('닫기 버튼으로 닫으며 검색 초기화는 입력창에 포커스를 돌려준다', async () => {
  const { onClose, onPick } = show()
  await userEvent.type(screen.getByRole('textbox'), 'orders')
  await userEvent.click(screen.getByRole('button', { name: '검색 초기화' }))
  expect((screen.getByRole('textbox') as HTMLInputElement).value).toBe('')
  expect(document.activeElement).toBe(screen.getByRole('textbox'))
  screen.getByRole('button', { name: '닫기' }).focus()
  await userEvent.keyboard('{Enter}')
  expect(onClose).toHaveBeenCalledTimes(1)
  expect(onPick).not.toHaveBeenCalled()
})
