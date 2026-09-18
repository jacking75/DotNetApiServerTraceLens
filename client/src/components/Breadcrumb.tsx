import { useI18n } from '../i18n/I18nProvider'

export interface BreadcrumbItem {
  key: string
  label: string
  /**
   * 눌렀을 때 실제로 되돌아갈 자리가 있는지.
   * 동작이 없는 항목을 활성 버튼으로 그리면 "눌리는데 반응이 없다"가 된다.
   */
  navigable?: boolean
}

interface BreadcrumbProps {
  items: BreadcrumbItem[]
  onNavigate: (index: number) => void
}

export function Breadcrumb({ items, onNavigate }: BreadcrumbProps) {
  const { t } = useI18n()
  return (
    <nav className="breadcrumb" aria-label={t('graph.breadcrumbAria')}>
      {items.map((item, index) => (
        <span key={`${item.key}-${index}`}>
          {index > 0 && <span className="breadcrumb-separator">›</span>}
          <button
            type="button"
            className={index === items.length - 1 ? 'current' : ''}
            onClick={() => onNavigate(index)}
            disabled={index === items.length - 1 || item.navigable === false}
          >
            {item.label}
          </button>
        </span>
      ))}
    </nav>
  )
}
