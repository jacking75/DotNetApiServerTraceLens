import type { NodeKind } from '../types/graph'

export const kindColors: Record<NodeKind, string> = {
  Controller: '#7c6df2',
  Service: '#268f81',
  Repository: '#c57b35',
  Validator: '#b75b8d',
  Infra: '#547aa5',
  Db: '#9b6fca',
  Interface: '#4fb0c6',
  Method: '#566174',
}

export const kindOrder: NodeKind[] =
  ['Controller', 'Service', 'Repository', 'Validator', 'Infra', 'Db', 'Interface', 'Method']
