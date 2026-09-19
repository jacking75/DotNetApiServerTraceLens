/**
 * @vitest-environment jsdom
 */
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useState } from 'react'
import { I18nProvider } from '../i18n/I18nProvider'
import { AskPanel, type AskContext } from './AskPanel'

const context: AskContext = {
  kind: 'Node',
  id: 'OrderService.CreateAsync',
  nodeId: 'OrderService.CreateAsync',
  lines: [{ label: '파일', value: 'src/OrderService.cs' }],
}

/** 문맥 미리보기·진단은 서버를 부른다. 테스트는 항상 실패 응답을 주고 패널만 본다. */
function stubFetch() {
  vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', {
    status: 500,
    headers: { 'Content-Type': 'application/json' },
  })))
}

/**
 * 질문 초안은 App이 들고 있다(우측 열을 접었다 펴도 남아야 하기 때문이다).
 * 테스트도 같은 모양으로 감싸야 칩 → 입력칸 흐름을 실제와 같게 확인한다.
 */
function Harness() {
  const [question, setQuestion] = useState('')
  return (
    <AskPanel
      context={context}
      snapshotId="snap"
      question={question}
      onChangeQuestion={setQuestion}
      recentAnswers={[]}
      onPushRecentAnswer={() => {}}
      onExplain={() => {}}
      onOpenSource={() => {}}
      onOpenInEditor={() => {}}
    />
  )
}

describe('AskPanel 프리셋 질문', () => {
  beforeEach(() => {
    window.localStorage.setItem('tracelens.lang', 'ko')
    stubFetch()
  })
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  /**
   * 빈 textarea에 placeholder 한 줄만 있으면 "뭐라고 물어보지"의 답이 없다.
   * 칩을 눌러도 입력칸이 채워지지 않으면 프리셋은 장식일 뿐이다.
   */
  it('칩을 누르면 질문 입력칸이 채워지고 그대로 고칠 수 있다', async () => {
    const user = userEvent.setup()
    render(
      <I18nProvider>
        <Harness />
      </I18nProvider>,
    )

    const chip = screen.getByRole('button', { name: /근거 ID와 함께 설명해 줘/ })
    await user.click(chip)

    const textarea = screen.getByRole('textbox', { name: 'AI에게 질문' }) as HTMLTextAreaElement
    expect(textarea.value).toContain('근거 ID와 함께 설명해 줘')

    await user.type(textarea, ' 추가로 실패 경로도.')
    expect(textarea.value).toContain('추가로 실패 경로도.')
  })

  it('목적과 길이가 무엇을 바꾸는지 한 줄로 알려 준다', () => {
    render(
      <I18nProvider>
        <Harness />
      </I18nProvider>,
    )

    expect(screen.getByText(/코드 이해 — 진입점에서 이 지점까지의 경로/)).toBeTruthy()
  })
})
