import { useEffect, useState } from 'react'
import { useI18n } from '../i18n/I18nProvider'
import type { McpSetupPreview } from '../types/context'

interface McpSetupDialogProps {
  setup: McpSetupPreview
  onClose: () => void
}

/** 코드 블록 + 복사 버튼. 신입이 손으로 옮겨 적다 틀리는 자리를 없앤다. */
function CopyBlock({ text, label }: { text: string; label?: string }) {
  const { t } = useI18n()
  const [message, setMessage] = useState<string | null>(null)

  return (
    <div className="copy-block">
      {label && <div className="hint">{label}</div>}
      <pre>{text}</pre>
      <button
        type="button"
        className="link-button"
        onClick={() => void navigator.clipboard.writeText(text)
          .then(() => setMessage(t('mcp.copied')), () => setMessage(t('ask.copyFailed')))}
      >
        {t('mcp.copy')}
      </button>
      {message && <span className="hint">{message}</span>}
    </div>
  )
}

/**
 * MCP 등록을 붙여넣기 한 번으로 끝낸다.
 * 호스트 탭 + "에이전트 안내문" 탭이고, 모든 코드 블록에 복사 버튼이 있다.
 */
export function McpSetupDialog({ setup, onClose }: McpSetupDialogProps) {
  const { t } = useI18n()
  const tabs = [...setup.hosts.map((host) => host.host), 'agentGuide']
  const [tab, setTab] = useState(tabs[0])

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const host = setup.hosts.find((item) => item.host === tab) ?? null
  const oneLiner = setup.oneLiners.find((item) => item.host === tab) ?? null

  return (
    <div className="modal-backdrop" role="presentation" onClick={onClose}>
      <div
        className="modal mcp-dialog"
        role="dialog"
        aria-modal="true"
        aria-label={t('ask.mcpSetupTitle')}
        onClick={(event) => event.stopPropagation()}
      >
        <header>
          <span>{t('ask.mcpSetupTitle')}</span>
          <button type="button" className="link-button" onClick={onClose}>{t('ask.close')}</button>
        </header>

        <div className="hint">{t('ask.mcpSetupNote')}</div>
        {setup.warning && <div className="hint warn">{setup.warning}</div>}

        <div className="tab-row" role="tablist" aria-label={t('ask.mcpSetupTitle')}>
          {tabs.map((item) => (
            <button
              key={item}
              type="button"
              role="tab"
              aria-selected={tab === item}
              className={tab === item ? 'active' : ''}
              onClick={() => setTab(item)}
            >
              {item === 'agentGuide' ? t('mcp.agentGuideTab') : item}
            </button>
          ))}
        </div>

        {host && (
          <>
            <div className="hint">{t('mcp.why')}</div>
            {oneLiner && <CopyBlock text={oneLiner.command} label={t('mcp.oneLiner')} />}
            <CopyBlock text={host.content} label={t('mcp.snippetFor', { file: host.file })} />
            <div className="hint">{host.note}</div>
            <div className="hint">{t('mcp.restart')}</div>
            <div className="hint warn">{t('mcp.workingDirectory')}</div>
            <CopyBlock text={setup.verifyPrompt} label={t('mcp.verify')} />
          </>
        )}

        {tab === 'agentGuide' && (
          <>
            <div className="hint">{t('mcp.agentGuideWhy')}</div>
            <CopyBlock text={setup.agentGuide} />
          </>
        )}
      </div>
    </div>
  )
}
