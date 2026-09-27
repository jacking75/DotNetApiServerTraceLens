/**
 * @vitest-environment jsdom
 */
import { cleanup, render, screen, waitFor } from '@testing-library/react'
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
function Harness({ agent = 'claude-code' }: { agent?: 'claude-code' | 'codex' }) {
  const [question, setQuestion] = useState('')
  return (
    <AskPanel
      context={context}
      agent={agent}
      diagnostics={null}
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

  it('상단에서 고른 Codex CLI를 질문 탭과 실행 요청에 적용한다', async () => {
    const requests: Array<{ agent?: string }> = []
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input).endsWith('/api/ask')) {
        requests.push(JSON.parse(String(init?.body)))
        return new Response('data: {"type":"done"}\n\n', {
          headers: { 'Content-Type': 'text/event-stream' },
        })
      }
      return new Response('{}', { status: 500, headers: { 'Content-Type': 'application/json' } })
    }))
    const user = userEvent.setup()
    render(<I18nProvider><Harness agent="codex" /></I18nProvider>)

    await user.click(screen.getByRole('tab', { name: 'Codex CLI로 실행' }))
    await user.type(screen.getByRole('textbox', { name: 'AI에게 질문' }), '이 코드를 설명해 줘')
    await user.click(screen.getByRole('button', { name: '질문 보내기' }))

    await waitFor(() => expect(requests).toHaveLength(1))
    expect(requests[0].agent).toBe('codex')
  })

  it('MCP 연동 설정도 선택한 Codex CLI 탭에서 연다', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      if (String(input).endsWith('/api/mcp-setup')) {
        return Response.json({
          graphPath: '', command: '', args: [], warning: null,
          hosts: [
            { host: 'Claude Code', file: '.mcp.json', format: 'json', content: '{}', note: '' },
            { host: 'Codex CLI', file: 'config.toml', format: 'toml', content: '[mcp_servers]', note: '' },
          ],
          oneLiners: [], verifyPrompt: '', agentGuide: '',
        })
      }
      return new Response('{}', { status: 500, headers: { 'Content-Type': 'application/json' } })
    }))
    const user = userEvent.setup()
    render(<I18nProvider><Harness agent="codex" /></I18nProvider>)

    await user.click(screen.getByRole('button', { name: 'MCP 연동 설정 보기' }))

    expect((await screen.findByRole('tab', { name: 'Codex CLI' })).getAttribute('aria-selected')).toBe('true')
  })
})
