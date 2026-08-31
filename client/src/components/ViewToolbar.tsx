import { relationLabels, type RelationType } from '../graph/relations'
import type { ClassMapState, DataViewState, MiddlewareViewState } from '../state/viewState'
import type { FindingSeverity, ViewMode } from '../types/graph'

interface ViewToolbarProps {
  viewMode: ViewMode
  onChangeMode: (mode: ViewMode) => void
  classMap: ClassMapState
  onChangeClassMap: (patch: Partial<ClassMapState>) => void
  onFitZoom: () => void
  callFlowLevel: number
  flowEntryId: string | null
  depth: number
  onChangeDepth: (depth: number) => void
  dataView: DataViewState
  onChangeDataView: (patch: Partial<DataViewState>) => void
  middlewareView: MiddlewareViewState
  onChangeMiddlewareView: (patch: Partial<MiddlewareViewState>) => void
  /** 설정에서 읽은 구성. 샤딩을 쓰면 SHARD 규칙군이 켜진다. */
  shardingEnabled: boolean
}

const modes: Array<{ mode: ViewMode; label: string }> = [
  { mode: 'callflow', label: '호출 흐름' },
  { mode: 'classmap', label: '클래스 다이어그램' },
  { mode: 'flowmap', label: '전체 흐름도' },
  { mode: 'startup', label: '시작 흐름' },
  { mode: 'data', label: 'DB · 캐시' },
  { mode: 'middleware', label: '미들웨어' },
]

const hints: Record<ViewMode, string> = {
  callflow: '진입점에서 분기 지점까지만 그리고 ⊕ 노드를 눌러 한 단계씩 파고든다.',
  classmap: '솔루션 전체 클래스를 관계 방향에 따라 층을 나눠 그린다.',
  flowmap: '모든 진입점과 그 아래 클래스 흐름이다. 라우트 줄을 누르면 그 진입점의 전 구간으로 들어간다.',
  startup: 'Program.cs를 훑어 API 서버가 시작될 때까지 어떤 객체를 등록하고 어떻게 연결하는지 순서대로 보여준다.',
  data: 'DB와 캐시에 닿는 경로를 순서대로 세우고, 트랜잭션 없이 쓰는 자리에서 무엇이 되돌려지지 않는지 본다.',
  middleware: '요청이 무엇을 통과하고 어디서 끊기는지, 응답이 돌아올 때 무엇이 더 실행되는지 순서대로 보여준다.',
}

export function ViewToolbar({
  viewMode,
  onChangeMode,
  classMap,
  onChangeClassMap,
  onFitZoom,
  callFlowLevel,
  flowEntryId,
  depth,
  onChangeDepth,
  dataView,
  onChangeDataView,
  middlewareView,
  onChangeMiddlewareView,
  shardingEnabled,
}: ViewToolbarProps) {
  const hint = viewMode === 'classmap'
    ? classMap.focusOnly
      ? '선택한 클래스와 직접 이어진 클래스만 그린다.'
      : classMap.layout === 'tree'
        ? hints.classmap
        : '솔루션 전체 클래스를 네임스페이스 패키지로 묶어 그린다.'
    : viewMode === 'flowmap' && flowEntryId
      ? '이 진입점이 타는 전 구간이다. 클래스로 묶고 메서드는 줄로 보여준다.'
      : hints[viewMode]

  return (
    <div className="mode-bar">
      <div className="mode-switch" role="tablist" aria-label="보기 전환">
        {modes.map((item) => (
          <button
            type="button"
            role="tab"
            key={item.mode}
            aria-selected={viewMode === item.mode}
            className={viewMode === item.mode ? 'active' : ''}
            onClick={() => onChangeMode(item.mode)}
          >
            {item.label}
          </button>
        ))}
      </div>
      <span className="mode-hint">{hint}</span>
      <div className="toolbar-extra">
        {viewMode === 'callflow' && <span>레벨 {callFlowLevel}</span>}
        {viewMode === 'data' && (
          <DataControls
            dataView={dataView}
            onChange={onChangeDataView}
            shardingEnabled={shardingEnabled}
          />
        )}
        {viewMode === 'middleware' && (
          <MiddlewareControls middlewareView={middlewareView} onChange={onChangeMiddlewareView} />
        )}
        {viewMode === 'classmap' && (
          <ClassMapControls classMap={classMap} onChange={onChangeClassMap} onFitZoom={onFitZoom} />
        )}
        {viewMode === 'flowmap' && flowEntryId && (
          <>
            <span>깊이</span>
            {[2, 3, 4, 9].map((value) => (
              <Chip
                key={value}
                label={value === 9 ? '전체' : String(value)}
                active={depth === value}
                onClick={() => onChangeDepth(value)}
              />
            ))}
          </>
        )}
      </div>
    </div>
  )
}

interface ClassMapControlsProps {
  classMap: ClassMapState
  onChange: (patch: Partial<ClassMapState>) => void
  onFitZoom: () => void
}

function ClassMapControls({ classMap, onChange, onFitZoom }: ClassMapControlsProps) {
  const relationTypes = Object.keys(relationLabels) as RelationType[]
  return (
    <>
      <span>배치</span>
      <Chip
        label="트리"
        title="관계 방향으로 층을 나눠 트리처럼 배치한다"
        active={classMap.layout === 'tree'}
        onClick={() => onChange({ layout: 'tree' })}
      />
      <Chip
        label="패키지"
        title="네임스페이스별 격자로 묶어 배치한다"
        active={classMap.layout === 'package'}
        onClick={() => onChange({ layout: 'package' })}
      />
      {classMap.layout === 'tree' && (
        <>
          <Chip
            label="→"
            title="왼쪽에서 오른쪽으로 뻗는 트리"
            active={classMap.treeDirection === 'LR'}
            onClick={() => onChange({ treeDirection: 'LR' })}
          />
          <Chip
            label="↓"
            title="위에서 아래로 뻗는 트리"
            active={classMap.treeDirection === 'TB'}
            onClick={() => onChange({ treeDirection: 'TB' })}
          />
        </>
      )}
      <span className="sepline" />
      <span>멤버</span>
      <Chip label="필드" active={classMap.showFields} onClick={() => onChange({ showFields: !classMap.showFields })} />
      <Chip label="메서드" active={classMap.showMethods} onClick={() => onChange({ showMethods: !classMap.showMethods })} />
      <span className="sepline" />
      <span>관계</span>
      {relationTypes.map((type) => (
        <Chip
          key={type}
          label={relationLabels[type]}
          active={classMap.relations[type]}
          onClick={() => onChange({ relations: { ...classMap.relations, [type]: !classMap.relations[type] } })}
        />
      ))}
      <span className="sepline" />
      <Chip
        label="선택 1홉만"
        active={classMap.focusOnly}
        onClick={() => onChange({ focusOnly: !classMap.focusOnly })}
      />
      <span className="sepline" />
      <span>줌</span>
      {[0.5, 0.75, 1].map((value) => (
        <Chip
          key={value}
          label={`${Math.round(value * 100)}%`}
          active={classMap.zoom === value}
          onClick={() => onChange({ zoom: value })}
        />
      ))}
      <Chip label="맞춤" active={false} title="전체가 화면에 들어오는 배율로 맞춘다" onClick={onFitZoom} />
    </>
  )
}

interface ChipProps {
  label: string
  active: boolean
  onClick: () => void
  title?: string
}

function Chip({ label, active, onClick, title }: ChipProps) {
  return (
    <button type="button" className={`chipbtn${active ? ' active' : ''}`} onClick={onClick} title={title}>
      {label}
    </button>
  )
}

const severityFilters: { key: 'all' | FindingSeverity; label: string }[] = [
  { key: 'all', label: '전체' },
  { key: 'Critical', label: '치명' },
  { key: 'Warning', label: '경고' },
  { key: 'Info', label: '정보' },
]

function DataControls({
  dataView,
  onChange,
  shardingEnabled,
}: {
  dataView: DataViewState
  onChange: (patch: Partial<DataViewState>) => void
  shardingEnabled: boolean
}) {
  const subs: { key: DataViewState['sub']; label: string }[] = [
    { key: 'route', label: '실행 경로' },
    { key: 'map', label: '접근 지도' },
    { key: 'tx', label: '트랜잭션' },
    { key: 'comp', label: '보상 처리' },
    { key: 'check', label: '점검 결과' },
  ]

  return (
    <>
      {subs.map((sub) => (
        <Chip
          key={sub.key}
          label={sub.label}
          active={dataView.sub === sub.key}
          onClick={() => onChange({ sub: sub.key })}
        />
      ))}
      <span className="sepline" />
      <span className="chipbtn static" title="tracelens.config.json의 dataSources에서 읽은 구성이다.">
        구성: {shardingEnabled ? '샤딩' : '단일 DB'}
      </span>
      <Chip
        label="확실한 것만"
        active={dataView.onlyCertain}
        onClick={() => onChange({ onlyCertain: !dataView.onlyCertain })}
      />
      {dataView.sub === 'check' && (
        <>
          <span className="sepline" />
          {severityFilters.map((filter) => (
            <Chip
              key={filter.key}
              label={filter.label}
              active={dataView.severity === filter.key}
              onClick={() => onChange({ severity: filter.key })}
            />
          ))}
        </>
      )}
    </>
  )
}

function MiddlewareControls({
  middlewareView,
  onChange,
}: {
  middlewareView: MiddlewareViewState
  onChange: (patch: Partial<MiddlewareViewState>) => void
}) {
  const subs: { key: MiddlewareViewState['sub']; label: string }[] = [
    { key: 'pipe', label: '파이프라인' },
    { key: 'sc', label: '단락 지점' },
    { key: 'check', label: '점검 결과' },
  ]

  return (
    <>
      {subs.map((sub) => (
        <Chip
          key={sub.key}
          label={sub.label}
          active={middlewareView.sub === sub.key}
          onClick={() => onChange({ sub: sub.key })}
        />
      ))}
      <span className="sepline" />
      <Chip
        label="응답 경로 표시"
        active={middlewareView.showPost}
        onClick={() => onChange({ showPost: !middlewareView.showPost })}
      />
      {middlewareView.sub === 'check' && (
        <>
          <span className="sepline" />
          {severityFilters.map((filter) => (
            <Chip
              key={filter.key}
              label={filter.label}
              active={middlewareView.severity === filter.key}
              onClick={() => onChange({ severity: filter.key })}
            />
          ))}
        </>
      )}
    </>
  )
}
