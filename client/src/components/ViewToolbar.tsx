import { relationLabels, type RelationType } from '../graph/relations'
import { useI18n } from '../i18n/I18nProvider'
import type { TranslationKey } from '../i18n/ko'
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

/** 보기 목록. 이름과 설명은 i18n 키로만 참조한다. */
const modes: readonly ViewMode[] = ['callflow', 'classmap', 'flowmap', 'startup', 'data', 'middleware']

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
  const { t } = useI18n()
  const hint = viewMode === 'classmap'
    ? classMap.focusOnly
      ? t('viewHint.classmapFocus')
      : classMap.layout === 'tree'
        ? t('viewHint.classmap')
        : t('viewHint.classmapPackage')
    : viewMode === 'flowmap' && flowEntryId
      ? t('viewHint.flowmapEntry')
      : t(`viewHint.${viewMode}` as const)

  return (
    <div className="mode-bar">
      <div className="mode-switch" role="tablist" aria-label={t('viewToolbar.label')}>
        {modes.map((item) => (
          <button
            type="button"
            role="tab"
            key={item}
            aria-selected={viewMode === item}
            className={viewMode === item ? 'active' : ''}
            onClick={() => onChangeMode(item)}
          >
            {t(`view.${item}` as const)}
          </button>
        ))}
      </div>
      <span className="mode-hint">{hint}</span>
      <div className="toolbar-extra">
        {viewMode === 'callflow' && <span>{t('toolbar.level', { n: callFlowLevel })}</span>}
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
            <span>{t('toolbar.depth')}</span>
            {[2, 3, 4, 9].map((value) => (
              <Chip
                key={value}
                label={value === 9 ? t('toolbar.all') : String(value)}
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
  const { t } = useI18n()
  const relationTypes = Object.keys(relationLabels) as RelationType[]
  return (
    <>
      <span>{t('toolbar.layout')}</span>
      <Chip
        label={t('toolbar.layout.tree')}
        title={t('toolbar.layout.treeTitle')}
        active={classMap.layout === 'tree'}
        onClick={() => onChange({ layout: 'tree' })}
      />
      <Chip
        label={t('toolbar.layout.package')}
        title={t('toolbar.layout.packageTitle')}
        active={classMap.layout === 'package'}
        onClick={() => onChange({ layout: 'package' })}
      />
      {classMap.layout === 'tree' && (
        <>
          <Chip
            label="→"
            title={t('toolbar.dirLrTitle')}
            active={classMap.treeDirection === 'LR'}
            onClick={() => onChange({ treeDirection: 'LR' })}
          />
          <Chip
            label="↓"
            title={t('toolbar.dirTbTitle')}
            active={classMap.treeDirection === 'TB'}
            onClick={() => onChange({ treeDirection: 'TB' })}
          />
        </>
      )}
      <span className="sepline" />
      <span>{t('toolbar.members')}</span>
      <Chip label={t('toolbar.fields')} active={classMap.showFields} onClick={() => onChange({ showFields: !classMap.showFields })} />
      <Chip label={t('toolbar.methods')} active={classMap.showMethods} onClick={() => onChange({ showMethods: !classMap.showMethods })} />
      <span className="sepline" />
      <span>{t('toolbar.relations')}</span>
      {relationTypes.map((type) => (
        <Chip
          key={type}
          label={t(`relation.${type}` as const)}
          active={classMap.relations[type]}
          onClick={() => onChange({ relations: { ...classMap.relations, [type]: !classMap.relations[type] } })}
        />
      ))}
      <span className="sepline" />
      <Chip
        label={t('toolbar.focusOnly')}
        active={classMap.focusOnly}
        onClick={() => onChange({ focusOnly: !classMap.focusOnly })}
      />
      <span className="sepline" />
      <span>{t('toolbar.zoom')}</span>
      {[0.5, 0.75, 1].map((value) => (
        <Chip
          key={value}
          label={`${Math.round(value * 100)}%`}
          active={classMap.zoom === value}
          onClick={() => onChange({ zoom: value })}
        />
      ))}
      <Chip label={t('toolbar.fit')} active={false} title={t('toolbar.fitTitle')} onClick={onFitZoom} />
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

const severityFilters: { key: 'all' | FindingSeverity; labelKey: TranslationKey }[] = [
  { key: 'all', labelKey: 'toolbar.severity.all' },
  { key: 'Critical', labelKey: 'toolbar.severity.critical' },
  { key: 'Warning', labelKey: 'toolbar.severity.warning' },
  { key: 'Info', labelKey: 'toolbar.severity.info' },
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
  const { t } = useI18n()
  const subs: DataViewState['sub'][] = ['route', 'map', 'tx', 'comp', 'check']

  return (
    <>
      {subs.map((sub) => (
        <Chip
          key={sub}
          label={t(`toolbar.data.${sub}` as const)}
          active={dataView.sub === sub}
          onClick={() => onChange({ sub })}
        />
      ))}
      <span className="sepline" />
      <span className="chipbtn static" title={t('toolbar.data.shapeTitle')}>
        {t('toolbar.data.shape', {
          shape: shardingEnabled ? t('toolbar.data.sharded') : t('toolbar.data.single'),
        })}
      </span>
      <Chip
        label={t('toolbar.data.onlyCertain')}
        active={dataView.onlyCertain}
        onClick={() => onChange({ onlyCertain: !dataView.onlyCertain })}
      />
      {dataView.sub === 'check' && (
        <>
          <span className="sepline" />
          {severityFilters.map((filter) => (
            <Chip
              key={filter.key}
              label={t(filter.labelKey)}
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
  const { t } = useI18n()
  const subs: MiddlewareViewState['sub'][] = ['pipe', 'sc', 'check']

  return (
    <>
      {subs.map((sub) => (
        <Chip
          key={sub}
          label={t(`toolbar.mw.${sub}` as const)}
          active={middlewareView.sub === sub}
          onClick={() => onChange({ sub })}
        />
      ))}
      <span className="sepline" />
      <Chip
        label={t('toolbar.mw.showPost')}
        active={middlewareView.showPost}
        onClick={() => onChange({ showPost: !middlewareView.showPost })}
      />
      {middlewareView.sub === 'check' && (
        <>
          <span className="sepline" />
          {severityFilters.map((filter) => (
            <Chip
              key={filter.key}
              label={t(filter.labelKey)}
              active={middlewareView.severity === filter.key}
              onClick={() => onChange({ severity: filter.key })}
            />
          ))}
        </>
      )}
    </>
  )
}
