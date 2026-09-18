/**
 * @vitest-environment jsdom
 */
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { I18nProvider } from '../i18n/I18nProvider'
import { SourceLink } from './SourceLink'

/**
 * 안내 문구는 "파일·줄을 눌러 실제 코드를 확인할 수 있다"고 말한다.
 * 눌리지 않으면 그 문구가 거짓이 된다 — 이 테스트가 문구와 동작을 묶는다.
 */
describe('SourceLink', () => {
  beforeEach(() => window.localStorage.setItem('tracelens.lang', 'ko'))
  afterEach(cleanup)

  it('누르면 그 위치로 코드를 연다', async () => {
    const onOpen = vi.fn()
    const user = userEvent.setup()
    render(
      <I18nProvider>
        <SourceLink file="src/OrderService.cs" line={42} onOpen={onOpen} />
      </I18nProvider>,
    )

    await user.click(screen.getByRole('button'))

    expect(onOpen).toHaveBeenCalledWith({ file: 'src/OrderService.cs', line: 42, endLine: undefined })
  })

  /** Shift+클릭은 에디터로 보낸다. 코드 보기를 여는 기본 동작을 가리지 않아야 한다. */
  it('Shift+클릭은 에디터에서 연다', async () => {
    const onOpen = vi.fn()
    const onOpenInEditor = vi.fn()
    const user = userEvent.setup()
    render(
      <I18nProvider>
        <SourceLink file="src/OrderService.cs" line={42} onOpen={onOpen} onOpenInEditor={onOpenInEditor} />
      </I18nProvider>,
    )

    await user.keyboard('{Shift>}')
    await user.click(screen.getByRole('button'))
    await user.keyboard('{/Shift}')

    expect(onOpenInEditor).toHaveBeenCalledTimes(1)
    expect(onOpen).not.toHaveBeenCalled()
  })
})
