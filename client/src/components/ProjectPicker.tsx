import { useI18n } from '../i18n/I18nProvider'

interface ProjectPickerProps {
  projectKeys: string[]
  projectKey: string | null
  onChange: (key: string) => void
}

/**
 * 실행 프로젝트 선택. Api + Worker + Gateway처럼 실행 프로젝트가 여럿이면
 * 시작 흐름·미들웨어가 첫 프로젝트 하나만 보이던 문제를 없앤다.
 * 프로젝트가 하나뿐이면 아무것도 그리지 않는다(고를 것이 없는 선택은 소음이다).
 */
export function ProjectPicker({ projectKeys, projectKey, onChange }: ProjectPickerProps) {
  const { t } = useI18n()
  if (projectKeys.length <= 1) return null

  return (
    <label className="project-picker">
      {t('side.project')}
      <select value={projectKey ?? ''} onChange={(event) => onChange(event.target.value)}>
        {projectKeys.map((key) => <option key={key} value={key}>{key}</option>)}
      </select>
    </label>
  )
}
