import { describe, expect, it } from 'vitest'
import { buildAccessMatrix, buildEntryDataFlow, buildFailGrid, countBySeverity, filterFindings } from './dataView'
import type { DataAccess, DataAccessDocument, Finding, GraphDocument, WritePath } from '../types/graph'

const step = (order: number, compensation: WritePath['steps'][number]['compensation'], label = `step${order}`) => ({
  order,
  label,
  detail: '',
  storage: 'Database' as const,
  dataSource: 'GameDb',
  target: `t${order}`,
  operation: 'Write' as const,
  compensation,
  compensationNote: '',
  file: 'a.cs',
  line: order,
})

const path = (steps: WritePath['steps']): WritePath => ({
  id: 'p1',
  methodNodeId: 'm1',
  methodDisplayName: 'S.M',
  classId: 'S',
  file: 'a.cs',
  startLine: 1,
  inTransaction: false,
  note: '',
  steps,
  findingIds: [],
})

describe('buildFailGrid', () => {
  it('앞 단계는 보상 판정대로, 뒤 단계는 미실행으로 채운다', () => {
    const grid = buildFailGrid(path([step(1, 'Yes'), step(2, 'No'), step(3, 'NotNeeded')]))

    expect(grid).toHaveLength(3)
    expect(grid[0].map((cell) => cell.kind)).toEqual(['here', 'notrun', 'notrun'])
    expect(grid[1].map((cell) => cell.kind)).toEqual(['undone', 'here', 'notrun'])
    expect(grid[2].map((cell) => cell.kind)).toEqual(['undone', 'stays', 'here'])
  })

  it('되돌릴 수 없는 단계는 남는 것으로 본다', () => {
    const grid = buildFailGrid(path([step(1, 'Irreversible'), step(2, 'No')]))

    expect(grid[1][0]).toEqual({ kind: 'stays', labelKey: 'data.cell.staysIrreversible' })
  })

  it('트랜잭션 안의 단계는 롤백으로 표시한다', () => {
    const grid = buildFailGrid(path([step(1, 'Transaction'), step(2, 'Transaction')]))

    expect(grid[1][0].kind).toBe('undone')
  })
})

const access = (over: Partial<DataAccess>): DataAccess => ({
  id: 'a',
  storage: 'Database',
  dataSource: 'GameDb',
  target: 'user_item',
  operation: 'Write',
  statement: 'UPDATE user_item',
  methodNodeId: 'm',
  methodDisplayName: 'R.M',
  classId: 'Ns.ItemRepository',
  file: 'r.cs',
  line: 1,
  inTransaction: false,
  inLoop: false,
  confidence: 'Certain',
  note: '',
  transactionWrapper: '',
  ...over,
})

const document = (over: Partial<DataAccessDocument>): DataAccessDocument => ({
  shardingEnabled: false,
  dataSources: [],
  accesses: [],
  transactions: [],
  writePaths: [],
  findings: [],
  rules: [],
  ...over,
})

describe('buildAccessMatrix', () => {
  it('클래스별로 대상과 R/W를 합친다', () => {
    const matrix = buildAccessMatrix(document({
      accesses: [
        access({}),
        access({ operation: 'Read', target: 'user_item' }),
        access({ target: 'user_mail', classId: 'Ns.MailRepository' }),
      ],
    }), 'all')

    expect(matrix.targets.map((target) => target.name)).toEqual(['user_item', 'user_mail'])
    const item = matrix.rows.find((row) => row.className === 'ItemRepository')!
    expect(item.cells['user_item'].operations).toBe('RW')
  })

  it('쓰기만 보기는 읽기 접근을 뺀다', () => {
    const matrix = buildAccessMatrix(document({
      accesses: [access({ operation: 'Read', target: 'user_profile' }), access({})],
    }), 'write')

    expect(matrix.targets.map((target) => target.name)).toEqual(['user_item'])
  })

  it('규칙에 걸린 칸에는 점검 항목 id를 붙인다', () => {
    const finding = { id: 'f1', target: 'user_item' } as Finding
    const matrix = buildAccessMatrix(document({ accesses: [access({})], findings: [finding] }), 'all')

    expect(matrix.rows[0].cells['user_item'].findingId).toBe('f1')
  })
})

describe('buildEntryDataFlow', () => {
  it('호출을 따라가며 저장소에 닿는 메서드만 순서대로 모은다', () => {
    const graph = {
      edges: [{ from: 'entry', to: 'service' }, { from: 'service', to: 'repo' }],
      dataAccess: document({
        accesses: [
          access({ methodNodeId: 'repo', methodDisplayName: 'Repo.Save', line: 5 }),
          access({ methodNodeId: 'repo', methodDisplayName: 'Repo.Save', line: 2 }),
        ],
      }),
    } as unknown as GraphDocument

    const steps = buildEntryDataFlow(graph, 'entry')

    expect(steps).toHaveLength(1)
    expect(steps[0].nodeId).toBe('repo')
    expect(steps[0].depth).toBe(2)
    expect(steps[0].accesses.map((item) => item.line)).toEqual([2, 5])
  })

  it('순환 호출에서도 멈춘다', () => {
    const graph = {
      edges: [{ from: 'a', to: 'b' }, { from: 'b', to: 'a' }],
      dataAccess: document({ accesses: [access({ methodNodeId: 'b' })] }),
    } as unknown as GraphDocument

    expect(buildEntryDataFlow(graph, 'a')).toHaveLength(1)
  })
})

describe('filterFindings', () => {
  const findings = [
    { id: '1', severity: 'Critical', confidence: 'Certain' },
    { id: '2', severity: 'Warning', confidence: 'Estimated' },
    { id: '3', severity: 'Info', confidence: 'Certain' },
  ] as Finding[]

  it('심각도와 확실도로 거른다', () => {
    expect(filterFindings(findings, { severity: 'all', onlyCertain: true }).map((item) => item.id)).toEqual(['1', '3'])
    expect(filterFindings(findings, { severity: 'Critical', onlyCertain: false }).map((item) => item.id)).toEqual(['1'])
  })

  it('심각도별 개수를 센다', () => {
    expect(countBySeverity(findings)).toEqual({ Critical: 1, Warning: 1, Info: 1 })
  })
})
