/**
 * @vitest-environment jsdom
 */
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { I18nProvider } from '../i18n/I18nProvider'
import { PathPicker } from './PathPicker'

/** `/api/browse` 대신 고정 목록을 돌려준다. 테스트가 서버를 띄우지 않게 한다. */
function useKoreanUi() {
  // jsdom의 navigator.language는 en-US다. 테스트는 한국어 라벨을 쓰므로 언어를 고정한다.
  window.localStorage.setItem('tracelens.lang', 'ko')
}

function stubBrowse() {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(
    JSON.stringify({
      path: 'C:\\Work',
      parentPath: 'C:\\',
      entries: [
        { name: 'Sample.sln', path: 'C:\\Work\\Sample.sln', kind: 'Solution' },
        { name: 'src', path: 'C:\\Work\\src', kind: 'Directory' },
      ],
    }),
    { status: 200, headers: { 'Content-Type': 'application/json' } },
  )))
}

describe('PathPicker 접근성', () => {
  beforeEach(() => {
    useKoreanUi()
    stubBrowse()
  })
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('열리면 닫기 버튼에 포커스가 간다', async () => {
    render(<I18nProvider><PathPicker initialPath="" onSelect={() => undefined} onClose={() => undefined} /></I18nProvider>)

    await waitFor(() => expect(screen.getByRole('dialog')).toBeDefined())
    expect(document.activeElement).toBe(screen.getByLabelText('닫기'))
  })

  it('Escape로 닫힌다', async () => {
    const onClose = vi.fn()
    render(<I18nProvider><PathPicker initialPath="" onSelect={() => undefined} onClose={onClose} /></I18nProvider>)
    await waitFor(() => expect(screen.getByRole('dialog')).toBeDefined())

    await userEvent.keyboard('{Escape}')

    expect(onClose).toHaveBeenCalled()
  })

  it('닫힐 때 이전 포커스를 되돌린다', async () => {
    const opener = document.createElement('button')
    document.body.appendChild(opener)
    opener.focus()

    const view = render(
      <I18nProvider><PathPicker initialPath="" onSelect={() => undefined} onClose={() => undefined} /></I18nProvider>,
    )
    await waitFor(() => expect(screen.getByRole('dialog')).toBeDefined())
    view.unmount()

    expect(document.activeElement).toBe(opener)
    opener.remove()
  })

  it('폴더 선택 모드에서는 지금 폴더를 그대로 고를 수 있다', async () => {
    const onSelect = vi.fn()
    render(
      <I18nProvider>
        <PathPicker initialPath="" mode="directory" onSelect={onSelect} onClose={() => undefined} />
      </I18nProvider>,
    )
    await waitFor(() => expect(screen.getByRole('dialog')).toBeDefined())

    await userEvent.click(screen.getByRole('button', { name: '선택' }))

    expect(onSelect).toHaveBeenCalledWith('C:\\Work')
  })
})
