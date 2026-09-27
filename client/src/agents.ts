/** 인앱 질문 실행에 사용할 CLI. 헤더와 질문 패널이 같은 선택을 쓴다. */
export type AgentId = 'claude-code' | 'codex'

export const agents = [
  { id: 'claude-code', label: 'Claude Code', command: 'claude', missingCode: 'CLI_CLAUDE_MISSING' },
  { id: 'codex', label: 'Codex CLI', command: 'codex', missingCode: 'CLI_CODEX_MISSING' },
] as const

export function agentInfo(id: AgentId) {
  return agents.find((item) => item.id === id)!
}
