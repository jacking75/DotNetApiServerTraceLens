import { useI18n } from '../i18n/I18nProvider'
import type { TranslationKey } from '../i18n/ko'
import type { DiagnosticItem, DiagnosticsReport } from '../types/context'

interface DiagnosticsCardProps {
  report: DiagnosticsReport | null
  /** 첫 화면과 모달이 같은 카드를 쓰되 바깥 상자만 다르다. */
  className?: string
}

/**
 * 환경 점검 결과 카드다. 첫 화면과 헤더의 모달이 같은 컴포넌트를 쓴다.
 * 점검 문장은 code로 번역하고, 경로·포트 같은 값은 서버가 준 그대로 보여 준다.
 */
export function DiagnosticsCard({ report, className }: DiagnosticsCardProps) {
  const { t } = useI18n()
  if (!report) return null

  const translateByCode = (prefix: string, code: string, fallback: string) => {
    const key = `${prefix}.${code}` as TranslationKey
    const translated = t(key)
    return translated === key ? fallback : translated
  }

  const describe = (item: DiagnosticItem) => translateByCode('diag', item.code, item.message)

  return (
    <section className={className ?? 'welcome-card wide'} aria-live="polite">
      <h3>{t('diagnostics.title')}</h3>
      <p className={report.canAnalyze ? 'ok' : 'bad'}>
        {report.canAnalyze ? t('diagnostics.canAnalyze') : t('diagnostics.cannotAnalyze')}
      </p>
      <ul className="diagnostics-list">
        {report.items.map((item) => (
          <li key={item.code} className={`sev-${item.severity.toLowerCase()}`}>
            <b>{item.code}</b> {describe(item)}
            {item.detail && <div className="hint"><code>{item.detail}</code></div>}
            {item.suggestedAction && (
              <div className="hint">→ {translateByCode('diagAction', item.code, item.suggestedAction)}</div>
            )}
          </li>
        ))}
      </ul>
      <small>{t('diagnostics.supported', { list: report.supportedTargets.join(', ') })}</small>
    </section>
  )
}
