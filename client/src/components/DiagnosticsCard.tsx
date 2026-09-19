import { useState } from 'react'
import { useI18n } from '../i18n/I18nProvider'
import type { TranslationKey } from '../i18n/ko'
import type { DiagnosticItem, DiagnosticsReport } from '../types/context'

interface DiagnosticsCardProps {
  report: DiagnosticsReport | null
  /** 첫 화면과 모달이 같은 카드를 쓰되 바깥 상자만 다르다. */
  className?: string
  /** 오류 배너에서 온 오류 ID. 신고 정보에 함께 담는다. */
  errorId?: string | null
}

/**
 * CONTRIBUTING의 신고 템플릿 형식이다. 사용자가 손으로 모으지 않아도 되도록 한 번에 만든다.
 * 담는 것은 버전·OS·SDK·지원 형식·로그 경로·최근 오류·오류 ID뿐이다 — 소스나 환경 변수는 담지 않는다.
 *
 * 제목은 영어로 고정한다. 이 글은 사람이 읽는 화면이 아니라 이슈에 붙이는 본문이고,
 * 받는 쪽이 어느 언어로 썼는지에 따라 형식이 갈라지면 대조하기 어렵다.
 */
function buildReportInfo(report: DiagnosticsReport, errorId: string | null): string {
  const lines = [
    '## Environment',
    `- TraceLens: ${report.toolVersion}`,
    `- .NET: ${report.runtimeVersion}`,
    `- OS: ${report.operatingSystem}`,
    `- SDK: ${report.sdkPath ?? '(not found)'}`,
    `- Supported targets: ${report.supportedTargets.join(', ')}`,
    `- Log: ${report.log?.logFilePath ?? '(none)'}`,
  ]
  if (errorId) lines.push(`- Error id: ${errorId}`)
  const failed = report.items.filter((item) => item.severity !== 'Ok')
  if (failed.length > 0) {
    lines.push('', '## Checks that did not pass')
    for (const item of failed) {
      lines.push(`- ${item.code}: ${item.message}${item.detail ? ` (${item.detail})` : ''}`)
    }
  }

  const recent = report.log?.recentErrors ?? []
  if (recent.length > 0) {
    lines.push('', '## Recent errors')
    for (const line of recent.slice(0, 3)) lines.push(`- ${line}`)
  }

  return lines.join('\n')
}

/**
 * `.NET 8.0.30`처럼 접두사가 이미 든 값에 `.NET`을 또 붙이지 않는다.
 * 서버가 주는 문자열 형식이 런타임마다 달라 값을 보고 판단한다.
 */
function runtimeLabel(runtimeVersion: string): string {
  return runtimeVersion.trim().startsWith('.NET') ? runtimeVersion : `.NET ${runtimeVersion}`
}

/**
 * 환경 점검 결과 카드다. 첫 화면과 헤더의 모달이 같은 컴포넌트를 쓴다.
 * 점검 문장은 code로 번역하고, 경로·포트 같은 값은 서버가 준 그대로 보여 준다.
 */
export function DiagnosticsCard({ report, className, errorId = null }: DiagnosticsCardProps) {
  const { t } = useI18n()
  const [copied, setCopied] = useState<string | null>(null)
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
      {/*
        예전에는 `<small>` 세 개를 줄바꿈 없이 이어 붙여
        `.sln, .csprojTraceLens 0.9.0 · .NET .NET 8.0.30 …로그 파일:`처럼 한 줄로 뭉쳤다(E10).
        항목과 값을 `<dl>`로 나누고 `.NET` 접두사는 한 번만 쓴다.
      */}
      <dl className="diagnostics-facts">
        <dt>{t('diagnostics.supportedLabel')}</dt>
        <dd>{report.supportedTargets.join(', ')}</dd>
        <dt>{t('diagnostics.versionLabel')}</dt>
        <dd>TraceLens {report.toolVersion} · {runtimeLabel(report.runtimeVersion)} · {report.operatingSystem}</dd>
        {report.log?.logFilePath && (
          <>
            <dt>{t('diagnostics.logFile')}</dt>
            <dd><code>{report.log.logFilePath}</code></dd>
          </>
        )}
      </dl>
      {(report.log?.recentErrors ?? []).length > 0 && (
        <details>
          <summary>{t('diagnostics.recentErrors', { count: report.log!.recentErrors.length })}</summary>
          <ul className="welcome-list">
            {report.log!.recentErrors.slice(0, 3).map((line) => <li key={line}><code>{line}</code></li>)}
          </ul>
        </details>
      )}
      <button
        type="button"
        className="link-button"
        onClick={() => void navigator.clipboard.writeText(buildReportInfo(report, errorId))
          .then(() => setCopied(t('diagnostics.reportCopied')), () => setCopied(t('ask.copyFailed')))}
      >
        {t('diagnostics.copyReport')}
      </button>
      {copied && <small aria-live="polite">{copied}</small>}
    </section>
  )
}
