export interface BreadcrumbItem {
  key: string
  label: string
}

interface BreadcrumbProps {
  items: BreadcrumbItem[]
  onNavigate: (index: number) => void
}

export function Breadcrumb({ items, onNavigate }: BreadcrumbProps) {
  return (
    <nav className="breadcrumb" aria-label="그래프 탐색 경로">
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
