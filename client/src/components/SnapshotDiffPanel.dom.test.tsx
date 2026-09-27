/** @vitest-environment jsdom */
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import * as api from '../api/client'
import { I18nProvider } from '../i18n/I18nProvider'
import type { SnapshotListResponse, SnapshotDiff } from '../types/context'
import { SnapshotDiffPanel } from './SnapshotDiffPanel'

vi.mock('../api/client', async (original) => ({
  ...await original<typeof import('../api/client')>(), listSnapshots: vi.fn(), compareSnapshots: vi.fn(),
}))
beforeEach(() => {
  vi.resetAllMocks()
  localStorage.setItem('tracelens.lang', 'ko')
  vi.mocked(api.listSnapshots).mockResolvedValue({ entries: [{ snapshotId: 'baseline', label: 'Before', savedAt: new Date().toISOString() }] } as SnapshotListResponse)
  vi.mocked(api.compareSnapshots).mockResolvedValue({
    preconditions: { ok: true, differences: [], notes: [] }, endpoints: [], methods: [], edges: [],
    dataAccess: [], findings: [], renameCandidates: [], limitations: [],
  } as unknown as SnapshotDiff)
})
afterEach(cleanup)

it('재분석 실패·취소 시 비교 API를 부르지 않고 이전 비교도 지운다', async () => {
  const reanalyze = vi.fn().mockResolvedValueOnce(true).mockResolvedValueOnce(false)
  render(<I18nProvider><SnapshotDiffPanel snapshotId="current" analyzing={false} onReanalyze={reanalyze} onClose={vi.fn()} /></I18nProvider>)
  await screen.findByRole('option')
  await userEvent.click(screen.getByRole('button', { name: '재분석하고 비교' }))
  await waitFor(() => expect(document.querySelector('.diff-body')).not.toBeNull())
  await userEvent.click(screen.getByRole('button', { name: '재분석하고 비교' }))
  await screen.findByText(/재분석이 완료되지 않아 비교를 중단했다/)
  expect(api.compareSnapshots).toHaveBeenCalledTimes(1)
  expect(document.querySelector('.diff-body')).toBeNull()
})
