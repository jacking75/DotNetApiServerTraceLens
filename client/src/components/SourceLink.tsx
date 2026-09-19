import { useI18n } from '../i18n/I18nProvider'

/** 화면 어디서든 같은 방식으로 여는 소스 위치. `endLine`이 없으면 그 줄 하나다. */
export interface SourceLocation {
  file: string
  line: number
  endLine?: number
}

interface SourceLinkProps extends SourceLocation {
  /** 누르면 코드 보기를 그 위치로 옮긴다. */
  onOpen: (location: SourceLocation) => void
  /**
   * Shift+클릭이나 컨텍스트 메뉴로 에디터에서 연다.
   * 필수다 — 패널마다 동작과 툴팁이 다르면 "같은 링크"로 읽히지 않는다.
   */
  onOpenInEditor: (location: SourceLocation) => void
}

/**
 * `파일:줄`을 버튼으로 그린다.
 * 안내 문구가 "파일·줄을 눌러 실제 코드를 확인할 수 있다"고 말하는데 눌리지 않으면
 * 문구가 거짓이 된다 — 화면의 모든 `파일:줄`이 이 컴포넌트를 쓴다.
 */
export function SourceLink({ file, line, endLine, onOpen, onOpenInEditor }: SourceLinkProps) {
  const { t } = useI18n()
  const location = { file, line, endLine }

  return (
    <button
      type="button"
      className="source-link"
      title={t('source.openHintWithEditor')}
      onClick={(event) => {
        event.stopPropagation()
        if (event.shiftKey) {
          onOpenInEditor(location)
          return
        }

        onOpen(location)
      }}
      onContextMenu={(event) => {
        event.preventDefault()
        event.stopPropagation()
        onOpenInEditor(location)
      }}
    >
      <code>{file}:{line}</code>
    </button>
  )
}
