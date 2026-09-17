import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { en } from './en'
import { ko, type Dictionary, type TranslationKey } from './ko'

export type Language = 'ko' | 'en'

const dictionaries: Record<Language, Dictionary> = { ko, en }

const storageKey = 'tracelens.lang'

/** 저장한 언어가 없으면 브라우저 언어를 참고하고, 그것도 없으면 한국어로 시작한다. */
function detectLanguage(): Language {
  try {
    const saved = window.localStorage.getItem(storageKey)
    if (saved === 'ko' || saved === 'en') return saved
  } catch {
    // 시크릿 모드처럼 localStorage를 못 쓰는 환경에서도 화면은 그대로 동작해야 한다.
  }

  return typeof navigator !== 'undefined' && navigator.language.toLowerCase().startsWith('ko') ? 'ko' : 'en'
}

export interface I18n {
  lang: Language
  setLang: (lang: Language) => void
  /** `{name}` 자리를 params로 채운다. 키가 없으면 키를 그대로 돌려줘서 누락을 눈에 띄게 한다. */
  t: (key: TranslationKey, params?: Record<string, string | number>) => string
  formatDateTime: (value: string | Date) => string
  formatTime: (value: string | Date) => string
  formatPercent: (value: number) => string
}

const I18nContext = createContext<I18n | null>(null)

export function I18nProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Language>(detectLanguage)

  // 화면 언어가 바뀌면 문서의 lang도 따라가야 한다(스크린 리더의 발음·브라우저 번역 판단이 이 값을 본다).
  useEffect(() => {
    document.documentElement.lang = lang
  }, [lang])

  const setLang = useCallback((next: Language) => {
    setLangState(next)
    try {
      window.localStorage.setItem(storageKey, next)
    } catch {
      // 저장을 못 해도 이번 세션에는 적용된다.
    }
  }, [])

  const value = useMemo<I18n>(() => {
    const dictionary = dictionaries[lang]
    const locale = lang === 'ko' ? 'ko-KR' : 'en-US'
    return {
      lang,
      setLang,
      t: (key, params) => {
        const template = dictionary[key] ?? key
        if (!params) return template
        return template.replace(/\{(\w+)\}/g, (match, name: string) =>
          name in params ? String(params[name]) : match)
      },
      formatDateTime: (value) => new Intl.DateTimeFormat(locale, {
        dateStyle: 'medium',
        timeStyle: 'medium',
      }).format(typeof value === 'string' ? new Date(value) : value),
      formatTime: (value) => new Intl.DateTimeFormat(locale, {
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
      }).format(typeof value === 'string' ? new Date(value) : value),
      formatPercent: (value) => new Intl.NumberFormat(locale, {
        style: 'percent',
        maximumFractionDigits: 1,
      }).format(value),
    }
  }, [lang, setLang])

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>
}

export function useI18n(): I18n {
  const value = useContext(I18nContext)
  if (!value) throw new Error('useI18n는 I18nProvider 안에서만 쓸 수 있다.')
  return value
}

/** 서버가 돌려준 오류 코드를 사용자가 읽을 문장으로 바꾼다. 모르는 코드는 서버 메시지를 그대로 쓴다. */
export function translateError(t: I18n['t'], code: string | undefined, fallback: string): string {
  const key = `error.${code ?? 'unknown'}` as TranslationKey
  const translated = t(key)
  return translated === key ? fallback : translated
}
