import { useState } from 'react'
import { recipes, type Recipe } from '../content/recipes'
import { useI18n } from '../i18n/I18nProvider'
import type { DiagnosticItem, DiagnosticsReport, SampleView } from '../types/context'
import type { TranslationKey } from '../i18n/ko'

interface WelcomePanelProps {
  /** 이 빌드에 들어 있는 예제 목록. 비어 있으면 예제 버튼 대신 사유를 보여 준다. */
  samples: SampleView[]
  /** 예제를 복사하는 중인지. 복사 → 분석이 이어지므로 버튼을 잠근다. */
  preparingSample: boolean
  /** 직전에 준비한 예제의 안내 문구(복사 위치). 없으면 null이다. */
  sampleNotice: string | null
  busy: boolean
  diagnostics: DiagnosticsReport | null
  /** 대상을 고른 직후인지. 경로만 입력칸에 들어가고 화면이 그대로라 다음 행동을 알려 준다. */
  targetPicked: boolean
  onAnalyzeSample: (sample: SampleView) => void
  onPickTarget: () => void
  onRunDiagnostics: () => void
  onStartRecipe: (recipe: Recipe) => void
}

/**
 * 분석 전 빈 화면 대신 보여 주는 첫 화면이다.
 * README와 같은 이야기 순서(무엇을 하는 도구인지 → 예제 → 내 프로젝트 → 지원 범위)를 따른다.
 */
export function WelcomePanel({
  samples,
  preparingSample,
  sampleNotice,
  busy,
  diagnostics,
  targetPicked,
  onAnalyzeSample,
  onPickTarget,
  onRunDiagnostics,
  onStartRecipe,
}: WelcomePanelProps) {
  const { t, lang } = useI18n()
  const [showDiagnostics, setShowDiagnostics] = useState(false)
  const available = samples.filter((sample) => sample.available)

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

      {targetPicked && (
        <p className="welcome-picked" role="status">{t('welcome.targetPicked')}</p>
      )}

      <div className="welcome-cards">
        <section className="welcome-card">
          <h3>{t('welcome.exampleTitle')}</h3>
          <p>{t('welcome.exampleBody')}</p>
          {available.length === 0 ? (
            <small className="bad">{t('welcome.sample.unavailable')}</small>
          ) : (
            <>
              {available.map((sample, index) => (
                <div key={sample.id} className="welcome-sample">
                  <button
                    type="button"
                    className={index === 0 ? 'primary' : 'link-button'}
                    onClick={() => onAnalyzeSample(sample)}
                    disabled={busy || preparingSample}
                  >
                    {index === 0 ? t('welcome.exampleCta') : sample.title}
                  </button>
                  <details>
                    <summary>{sample.title} — {t('welcome.sample.expected')}</summary>
                    <p className="hint">{sample.description}</p>
                    <ul className="welcome-list">
                      {sample.expected.map((line) => <li key={line}>{line}</li>)}
                    </ul>
                    {sample.learningSteps.length > 0 && (
                      <>
                        <b>{t('welcome.sample.learningSteps')}</b>
                        <ol className="welcome-list">
                          {sample.learningSteps.map((line) => <li key={line}>{line}</li>)}
                        </ol>
                      </>
                    )}
                  </details>
                </div>
              ))}
              {preparingSample && <small aria-live="polite">{t('welcome.sample.preparing')}</small>}
              {sampleNotice && <small aria-live="polite">{sampleNotice}</small>}
            </>
          )}
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
