/** @vitest-environment jsdom */
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { I18nProvider } from '../i18n/I18nProvider'
import { entryPoint } from '../types/testFactories'
import { EntryPointList } from './EntryPointList'

const orders = entryPoint('Orders.List()', 'GET', '/orders')
const hidden = { ...entryPoint('Admin.List()', 'GET', '/admin'), hiddenFromApiExplorer: true }
beforeEach(() => localStorage.setItem('tracelens.lang', 'ko'))
afterEach(cleanup)
function show(selectedEndpointId: string | null = null) {
  render(<I18nProvider><EntryPointList entryPoints={[orders, hidden]} selectedEndpointId={selectedEndpointId}
    onSelect={vi.fn()} emptyMessage="empty" sort="route" onChangeSort={vi.fn()} /></I18nProvider>)
}

it('검색창의 편집 키를 가로채지 않고 목록 항목의 방향키만 처리한다', () => {
  show()
  const search = screen.getByRole('textbox')
  search.focus()
  for (const key of ['Home', 'End', 'ArrowUp', 'ArrowDown']) {
    expect(fireEvent.keyDown(search, { key })).toBe(true)
    expect(document.activeElement).toBe(search)
  }
  const row = screen.getByRole('button', { name: /GET.*orders/ })
  row.focus()
  expect(fireEvent.keyDown(row, { key: 'Home' })).toBe(false)
})

it('검색 결과 없음과 실제 건수를 보여주고 초기화하면 목록을 복구한다', async () => {
  show()
  await userEvent.type(screen.getByRole('textbox'), 'missing')
  expect(screen.getByRole('status').textContent).toBe('전체 2개 중 0개 표시')
  expect(screen.getByText(/현재 검색·숨김 조건에 맞는 진입점이 없다/)).toBeDefined()
  await userEvent.click(screen.getByRole('button', { name: '검색 초기화' }))
  expect(screen.getByRole('status').textContent).toBe('전체 2개 중 1개 표시')
  expect(document.activeElement).toBe(screen.getByRole('textbox'))
})

it('가려진 현재 선택을 다시 표시하면 숨겨진 API도 선택 표시와 함께 나타난다', async () => {
  show(hidden.endpointId)
  await userEvent.click(screen.getByRole('button', { name: '현재 선택 표시' }))
  expect(screen.getByRole('button', { name: /GET.*admin/ }).getAttribute('aria-current')).toBe('true')
  expect(screen.queryByText('현재 선택한 진입점이 검색·숨김 조건에 가려져 있다.')).toBeNull()
})
