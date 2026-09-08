import { useState } from 'react'
import { recipes, type Recipe } from '../content/recipes'
import { useI18n } from '../i18n/I18nProvider'
import type { DiagnosticItem, DiagnosticsReport } from '../types/context'
import type { TranslationKey } from '../i18n/ko'

interface WelcomePanelProps {
  /** 예제 프로젝트 경로를 알고 있으면 바로 분석할 수 있게 한다. */
  samplePath: string | null
  busy: boolean
  diagnostics: DiagnosticsReport | null
  onAnalyzeSample: () => void
  onPickTarget: () => void
  onRunDiagnostics: () => void
  onStartRecipe: (recipe: Recipe) => void
}

/**
 * 분석 전 빈 화면 대신 보여 주는 첫 화면이다.
 * README와 같은 이야기 순서(무엇을 하는 도구인지 → 예제 → 내 프로젝트 → 지원 범위)를 따른다.
 */
export function WelcomePanel({
  samplePath,
  busy,
  diagnostics,
  onAnalyzeSample,
  onPickTarget,
  onRunDiagnostics,
  onStartRecipe,
}: WelcomePanelProps) {
  const { t, lang } = useI18n()
  const [showDiagnostics, setShowDiagnostics] = useState(false)

  /** 점검 문장은 코드로 번역하고, 경로·포트 같은 값은 서버가 준 그대로 보여 준다. */
  const translateByCode = (prefix: string, code: string, fallback: string) => {
    const key = `${prefix}.${code}` as TranslationKey
    const translated = t(key)
    return translated === key ? fallback : translated
  }

  const describe = (item: DiagnosticItem) => translateByCode('diag', item.code, item.message)

  return (
    <div className="welcome">
      <h2>{t('welcome.title')}</h2>
      <p className="welcome-lead">{t('welcome.lead')}</p>

      <div className="welcome-cards">
        <section className="welcome-card">
          <h3>{t('welcome.exampleTitle')}</h3>
          <p>{t('welcome.exampleBody')}</p>
          <button type="button" className="primary" onClick={onAnalyzeSample} disabled={busy || !samplePath}>
            {t('welcome.exampleCta')}
          </button>
          <small>{t('welcome.exampleHint')}</small>
        </section>

        <section className="welcome-card">
          <h3>{t('welcome.ownTitle')}</h3>
          <p>{t('welcome.ownBody')}</p>
          <button type="button" onClick={onPickTarget} disabled={busy}>
            {t('welcome.ownCta')}
          </button>
          <button
            type="button"
            className="link-button"
            onClick={() => {
              setShowDiagnostics(true)
              onRunDiagnostics()
            }}
          >
            {t('diagnostics.open')}
          </button>
        </section>
      </div>

      {showDiagnostics && diagnostics && (
        <section className="welcome-card wide" aria-live="polite">
          <h3>{t('diagnostics.title')}</h3>
          <p className={diagnostics.canAnalyze ? 'ok' : 'bad'}>
            {diagnostics.canAnalyze ? t('diagnostics.canAnalyze') : t('diagnostics.cannotAnalyze')}
          </p>
          <ul className="diagnostics-list">
            {diagnostics.items.map((item) => (
              <li key={item.code} className={`sev-${item.severity.toLowerCase()}`}>
                <b>{item.code}</b> {describe(item)}
                {item.detail && <div className="hint"><code>{item.detail}</code></div>}
                {item.suggestedAction && (
                  <div className="hint">→ {translateByCode('diagAction', item.code, item.suggestedAction)}</div>
                )}
              </li>
            ))}
          </ul>
          <small>{t('diagnostics.supported', { list: diagnostics.supportedTargets.join(', ') })}</small>
        </section>
      )}

      <section className="welcome-card wide">
        <h3>{t('welcome.scopeTitle')}</h3>
        <ul className="scope-list">
          <li>{t('welcome.scope1')}</li>
          <li>{t('welcome.scope2')}</li>
          <li>{t('welcome.scope3')}</li>
          <li>{t('welcome.scope4')}</li>
        </ul>
      </section>

      <section className="welcome-card wide">
        <h3>{t('welcome.recipes')}</h3>
        <ul className="recipe-list">
          {recipes.map((recipe) => (
            <li key={recipe.id}>
              <button type="button" className="recipe-button" onClick={() => onStartRecipe(recipe)}>
                {lang === 'ko' ? recipe.titleKo : recipe.titleEn}
              </button>
              <div className="hint">{(lang === 'ko' ? recipe.stepsKo : recipe.stepsEn)[0]}</div>
            </li>
          ))}
        </ul>
      </section>
    </div>
  )
}
