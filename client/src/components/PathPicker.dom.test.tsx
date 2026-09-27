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

  it('드라이브 버튼으로 어느 폴더에서든 드라이브 목록으로 이동한다', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: string | URL | Request) => {
      const isDriveList = !String(input).includes('?')
      return new Response(JSON.stringify(isDriveList
        ? {
            path: null,
            parentPath: null,
            entries: [{ name: 'D:\\', path: 'D:\\', kind: 'Directory' }],
          }
        : {
            path: 'C:\\Work',
            parentPath: 'C:\\',
            entries: [{ name: 'Sample.sln', path: 'C:\\Work\\Sample.sln', kind: 'Solution' }],
          }), { status: 200, headers: { 'Content-Type': 'application/json' } })
    }))
    render(
      <I18nProvider>
        <PathPicker initialPath="C:\\Work" onSelect={() => undefined} onClose={() => undefined} />
      </I18nProvider>,
    )
    await screen.findByText('Sample.sln')

    await userEvent.click(screen.getByRole('button', { name: '드라이브' }))

    expect(await screen.findByText('D:\\')).toBeDefined()
  })

  it('폴더 모드에서 파일을 더블클릭해도 파일 경로를 확정하지 않는다', async () => {
    const onSelect = vi.fn()
    render(<I18nProvider><PathPicker initialPath="" mode="directory" onSelect={onSelect} onClose={vi.fn()} /></I18nProvider>)
    await userEvent.dblClick(await screen.findByText('Sample.sln'))
    expect(onSelect).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: '선택' }))
    expect(onSelect).toHaveBeenCalledWith('C:\\Work')
  })

  it('폴더 이동 중과 실패 뒤에 이전 폴더를 확정할 수 없다', async () => {
    const onSelect = vi.fn()
    render(<I18nProvider><PathPicker initialPath="" mode="directory" onSelect={onSelect} onClose={vi.fn()} /></I18nProvider>)
    await screen.findByText('Sample.sln')
    let reject!: (reason: Error) => void
    vi.stubGlobal('fetch', vi.fn(() => new Promise((_resolve, fail) => { reject = fail })))
    await userEvent.click(screen.getByRole('button', { name: /src/ }))
    const confirm = screen.getByRole('button', { name: '선택' }) as HTMLButtonElement
    expect(confirm.disabled).toBe(true)
    reject(new Error('Access denied'))
    await screen.findByRole('alert')
    expect(confirm.disabled).toBe(true)
    await userEvent.click(confirm)
    expect(onSelect).not.toHaveBeenCalled()
  })

  it('주소 표시줄에 절대 경로를 입력해 바로 이동한다', async () => {
    const fetchMock = vi.fn(async (input: string | URL | Request) => {
      const url = String(input)
      const moved = url.includes('D%3A%5CRepos')
      return new Response(JSON.stringify(moved
        ? {
            path: 'D:\\Repos',
            parentPath: 'D:\\',
            entries: [{ name: 'Other.slnx', path: 'D:\\Repos\\Other.slnx', kind: 'Solution' }],
          }
        : {
            path: 'C:\\Work',
            parentPath: 'C:\\',
            entries: [{ name: 'Sample.sln', path: 'C:\\Work\\Sample.sln', kind: 'Solution' }],
          }), { status: 200, headers: { 'Content-Type': 'application/json' } })
    })
    vi.stubGlobal('fetch', fetchMock)
    render(
      <I18nProvider>
        <PathPicker initialPath="C:\\Work" onSelect={() => undefined} onClose={() => undefined} />
      </I18nProvider>,
    )
    await screen.findByText('Sample.sln')

    const address = screen.getByRole('textbox', { name: '경로 주소' })
    await userEvent.clear(address)
    await userEvent.type(address, 'D:\\Repos{Enter}')

    expect(await screen.findByText('Other.slnx')).toBeDefined()

    await userEvent.click(screen.getByRole('button', { name: '뒤로' }))
    expect(await screen.findByText('Sample.sln')).toBeDefined()

    await userEvent.click(screen.getByRole('button', { name: '앞으로' }))
    expect(await screen.findByText('Other.slnx')).toBeDefined()
  })

  it('Ctrl+L로 주소 표시줄에 바로 포커스한다', async () => {
    render(<I18nProvider><PathPicker initialPath="C:\\Work" onSelect={() => undefined} onClose={() => undefined} /></I18nProvider>)
    await screen.findByText('Sample.sln')

    await userEvent.keyboard('{Control>}l{/Control}')

    expect(document.activeElement).toBe(screen.getByRole('textbox', { name: '경로 주소' }))
  })
})
