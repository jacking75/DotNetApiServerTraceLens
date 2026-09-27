/** @vitest-environment jsdom */
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { HeaderMenu } from './HeaderMenu'

afterEach(cleanup)
function show() {
  const action = vi.fn()
  render(<><HeaderMenu label="Settings" id="settings">
    <button disabled>Unavailable</button>
    <button onClick={action}>Action</button>
    <label>Language<select defaultValue="ko"><option value="ko">KO</option><option value="en">EN</option></select></label>
  </HeaderMenu><button>Outside</button></>)
  return action
}
it('방향키로 열면 사용 가능한 항목에 포커스하고 Escape로 돌아온다', async () => {
  show()
  const trigger = screen.getByRole('button', { name: /Settings/ })
  trigger.focus()
  await userEvent.keyboard('{ArrowDown}')
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Action' }))
  await userEvent.keyboard('{Escape}')
  expect(screen.queryByRole('group')).toBeNull()
  expect(document.activeElement).toBe(trigger)
})
it('언어 선택을 클릭해도 메뉴가 유지되며 실행 버튼은 닫고 포커스를 복원한다', async () => {
  const action = show()
  const trigger = screen.getByRole('button', { name: /Settings/ })
  await userEvent.click(trigger)
  await userEvent.selectOptions(screen.getByRole('combobox'), 'en')
  expect(screen.getByRole('group')).toBeDefined()
  await userEvent.click(screen.getByRole('button', { name: 'Action' }))
  expect(action).toHaveBeenCalledTimes(1)
  expect(screen.queryByRole('group')).toBeNull()
  expect(document.activeElement).toBe(trigger)
})
