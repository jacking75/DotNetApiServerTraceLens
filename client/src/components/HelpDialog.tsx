import { useEffect, useRef, useState } from 'react'
import { useModal } from '../hooks/useModal'
import { shortcuts } from '../shortcuts'
import { recipes, type Recipe } from '../content/recipes'
import { useI18n } from '../i18n/I18nProvider'
import type { TranslationKey } from '../i18n/ko'

/**
 * 화면에 정의 없이 등장하는 용어들이다.
 * 각 항목은 `glossary.<term>.title`·`glossary.<term>.body` 두 키로 번역한다.
 */
export const glossaryTerms = [
  'entryPoint',
  'confidence',
  'freshness',
  'snapshot',
  'resolutionRate',
  'diRegistration',
  'mcp',
  'hop',
  'shortCircuit',
  'compensation',
  'callSite',
  'boundary',
] as const

export type GlossaryTerm = (typeof glossaryTerms)[number]

export type HelpTab = 'glossary' | 'shortcuts' | 'recipes'

interface HelpDialogProps {
  /** 처음 열 탭. `?` 링크로 들어오면 용어집이다. */
  initialTab: HelpTab
  /** 용어집에서 펼쳐 둘 항목. 없으면 전부 접는다. */
  focusTerm: GlossaryTerm | null
  /** 분석 전이면 레시피에 선행 조건을 붙인다. */
  analyzed: boolean
  onStartRecipe: (recipe: Recipe) => void
  onClose: () => void
}

/** 용어집·단축키·실무 절차를 한 곳에서 연다. 매뉴얼로 가는 링크도 여기 있다. */
export function HelpDialog({ initialTab, focusTerm, analyzed, onStartRecipe, onClose }: HelpDialogProps) {
  const { t, lang } = useI18n()
  const [tab, setTab] = useState<HelpTab>(initialTab)
  const modalRef = useModal<HTMLDivElement>(onClose)
  const closeRef = useRef<HTMLButtonElement>(null)

  useEffect(() => { setTab(initialTab) }, [initialTab])

  const manualUrl = `/manual/${lang}`

  return (
    <div className="modal-backdrop" role="presentation" onClick={onClose}>
      <div
        className="modal help-dialog"
        ref={modalRef}
        role="dialog"
        aria-modal="true"
        aria-label={t('help.title')}
        onClick={(event) => event.stopPropagation()}
      >
        <header>
          <span>{t('help.title')}</span>
          <a className="link-button" href={manualUrl} target="_blank" rel="noreferrer">{t('help.manual')}</a>
          <button ref={closeRef} type="button" className="link-button" onClick={onClose}>{t('ask.close')}</button>
        </header>

        <div className="tab-row" role="tablist" aria-label={t('help.title')}>
          {(['glossary', 'shortcuts', 'recipes'] as const).map((item) => (
            <button
              key={item}
              type="button"
              role="tab"
              aria-selected={tab === item}
              className={tab === item ? 'active' : ''}
              onClick={() => setTab(item)}
            >
              {t(`help.tab.${item}` as TranslationKey)}
            </button>
          ))}
        </div>

        {tab === 'glossary' && (
          <dl className="glossary">
            {glossaryTerms.map((term) => (
              <div key={term} id={`glossary-${term}`} className={term === focusTerm ? 'focused' : ''}>
                <dt>{t(`glossary.${term}.title` as TranslationKey)}</dt>
                <dd>{t(`glossary.${term}.body` as TranslationKey)}</dd>
              </div>
            ))}
          </dl>
        )}

        {tab === 'shortcuts' && (
          <dl className="glossary">
            {shortcuts.map((item) => (
              <div key={item.id}>
                <dt><kbd>{item.keys}</kbd></dt>
                <dd>{t(item.bodyKey)} <span className="hint">{t(item.scopeKey)}</span></dd>
              </div>
            ))}
          </dl>
        )}

        {tab === 'recipes' && (
          <ul className="help-recipes">
            {recipes.map((recipe) => (
              <li key={recipe.id}>
                <b>{lang === 'ko' ? recipe.titleKo : recipe.titleEn}</b>
                <ol>
                  {(lang === 'ko' ? recipe.stepsKo : recipe.stepsEn).map((step) => <li key={step}>{step}</li>)}
                </ol>
                <div className="row">
                  <button
                    type="button"
                    className="link-button"
                    disabled={!analyzed}
                    onClick={() => { onStartRecipe(recipe); onClose() }}
                  >
                    {t('recipes.use')}
                  </button>
                  <a
                    className="link-button"
                    href={`${manualUrl}#${recipe.manualAnchor}`}
                    target="_blank"
                    rel="noreferrer"
                  >
                    {t('help.openInManual')}
                  </a>
                </div>
                {!analyzed && <div className="hint">{t('help.needAnalysis')}</div>}
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}

interface HelpLinkProps {
  term: GlossaryTerm
  onOpen: (term: GlossaryTerm) => void
}

/** 용어 옆에 붙는 작은 `?` 버튼. 누르면 용어집의 해당 항목으로 간다. */
export function HelpLink({ term, onOpen }: HelpLinkProps) {
  const { t } = useI18n()
  const label = t(`glossary.${term}.title` as TranslationKey)
  return (
    <button
      type="button"
      className="help-link"
      aria-label={t('help.explainTerm', { term: label })}
      title={t('help.explainTerm', { term: label })}
      onClick={(event) => { event.stopPropagation(); onOpen(term) }}
    >
      ?
    </button>
  )
}
