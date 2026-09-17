import { describe, expect, it } from 'vitest'
import { normalizeGraph, parseServerSentEvents } from './client'
import type { GraphDocument } from '../types/graph'

describe('parseServerSentEvents', () => {
  it('완결된 블록만 꺼내고 나머지는 남긴다', () => {
    const { data, rest } = parseServerSentEvents('data: {"a":1}\n\ndata: {"b":2}\n\ndata: {"c"')

    expect(data).toEqual(['{"a":1}', '{"b":2}'])
    expect(rest).toBe('data: {"c"')
  })

  it('여러 줄 data를 한 블록으로 합친다', () => {
    const { data } = parseServerSentEvents('data: 첫 줄\ndata: 둘째 줄\n\n')

    expect(data).toEqual(['첫 줄\n둘째 줄'])
  })

  it('CRLF도 같은 규칙으로 읽는다', () => {
    const { data, rest } = parseServerSentEvents('data: {"a":1}\r\n\r\n')

    expect(data).toEqual(['{"a":1}'])
    expect(rest).toBe('')
  })

  it('data가 없는 블록은 건너뛴다', () => {
    const { data } = parseServerSentEvents(': keep-alive\n\ndata: {"a":1}\n\n')

    expect(data).toEqual(['{"a":1}'])
  })
})

describe('normalizeGraph', () => {
  const base = (over: Partial<GraphDocument> = {}): GraphDocument => ({
    schemaVersion: 7,
    solutionPath: 'C:/x/Fixture.sln',
    rootDirectory: 'C:/x',
    analyzedAt: '2026-09-17T00:00:00+09:00',
    entryPoints: [],
    classes: [],
    nodes: [],
    edges: [],
    fileHashes: {},
    callSites: [],
    snapshotId: 's',
    inputFingerprint: 'f',
    manifest: null,
    diagnostics: null,
    analyzerVersion: '0.8.0',
    rulesVersion: '1',
    startupFlow: null,
    dataAccess: null,
    middleware: null,
    ...over,
  } as GraphDocument)

  it('v7 이전 문서의 새 필드를 기본값으로 채운다', () => {
    const graph = normalizeGraph(base({
      // 서버가 옛 형식을 돌려준 상황을 흉내 낸다.
      edges: [{ from: 'a', to: 'b' } as never],
      callSites: [{ from: 'a', to: 'b', declaredTarget: 'B', file: 'a.cs', line: 1, resolution: 'Direct', confidence: 'Certain', note: '' } as never],
    }))

    expect(graph.edges[0].confidence).toBe('Certain')
    expect(graph.callSites[0].viaLambda).toBe(false)
  })

  it('미들웨어 단계의 kind가 없으면 Middleware로 채운다', () => {
    const graph = normalizeGraph(base({
      middleware: {
        programFile: 'Program.cs',
        steps: [{ id: 'mw1', order: 1, name: 'UseRouting' } as never],
        findings: [],
        rules: [],
      },
    }))

    expect(graph.middleware?.steps[0].kind).toBe('Middleware')
  })

  it('DB 접근의 확실도가 없으면 Certain으로 채운다', () => {
    const graph = normalizeGraph(base({
      dataAccess: {
        shardingEnabled: false,
        dataSources: [],
        accesses: [{ id: 'a', target: 'user', operation: 'Read' } as never],
        transactions: [{ id: 'tx' } as never],
        writePaths: [],
        findings: [],
        rules: [],
      },
    }))

    expect(graph.dataAccess?.accesses[0].confidence).toBe('Certain')
    expect(graph.dataAccess?.transactions[0].kind).toBe('Explicit')
  })
})
