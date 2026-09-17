import { useI18n } from '../i18n/I18nProvider'

export interface BreadcrumbItem {
  key: string
  label: string
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
            disabled={index === items.length - 1}
          >
            {item.label}
          </button>
        </span>
      ))}
    </nav>
  )
}
