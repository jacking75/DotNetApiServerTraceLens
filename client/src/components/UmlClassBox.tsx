import { captionOf, classMembers, memberKey, memberSign, type DiagramLayoutMode } from '../graph/classDiagram'
import type { PlacedBox } from '../graph/treeLayout'
import type { GraphClass, GraphClassMember } from '../types/graph'
import { kindColors } from './kindColors'

interface UmlClassBoxProps {
  graphClass: GraphClass
  box: PlacedBox
  layout: DiagramLayoutMode
  namespacePrefix: string
  showFields: boolean
  showMethods: boolean
  selected: boolean
  /** 선택한 클래스와 관계가 없어 흐리게 그릴 박스. */
  dimmed: boolean
  selectedMemberKey: string | null
  onSelectClass: (classId: string) => void
  onSelectMember: (graphClass: GraphClass, member: GraphClassMember) => void
}

/** UML 박스. 이름(캡션 포함) / 필드 / 메서드 3단이고, 멤버 줄을 누르면 그 멤버가 선택된다. */
export function UmlClassBox({
  graphClass,
  box,
  layout,
  namespacePrefix,
  showFields,
  showMethods,
  selected,
  dimmed,
  selectedMemberKey,
  onSelectClass,
  onSelectMember,
}: UmlClassBoxProps) {
  const caption = captionOf(graphClass, layout, namespacePrefix)
  const { fields, methods } = classMembers(graphClass)
  const className = ['uml', selected ? 'selected' : '', dimmed ? 'dim' : ''].filter(Boolean).join(' ')

  return (
    <div
      className={className}
      style={{
        left: box.x,
        top: box.y,
        width: box.w,
        height: box.h,
        borderTop: `2px solid ${kindColors[graphClass.kind]}`,
      }}
      onClick={() => onSelectClass(graphClass.id)}
      role="button"
      tabIndex={-1}
      title={graphClass.id}
    >
      <div className={`uml-name${caption ? ' with-stereo' : ''}`}>
        {caption && <div className="uml-stereo">{caption}</div>}
        <div className="uml-title">
          <b className={graphClass.isAbstract ? 'abs' : undefined}>{graphClass.name}</b>
        </div>
      </div>
      {showFields && (
        <MemberSection
          members={fields}
          emptyLabel="필드 없음"
          graphClass={graphClass}
          selectedMemberKey={selectedMemberKey}
          onSelectMember={onSelectMember}
        />
      )}
      {showMethods && (
        <MemberSection
          members={methods}
          emptyLabel="메서드 없음"
          graphClass={graphClass}
          selectedMemberKey={selectedMemberKey}
          onSelectMember={onSelectMember}
        />
      )}
    </div>
  )
}

interface MemberSectionProps {
  members: GraphClassMember[]
  emptyLabel: string
  graphClass: GraphClass
  selectedMemberKey: string | null
  onSelectMember: (graphClass: GraphClass, member: GraphClassMember) => void
}

function MemberSection({ members, emptyLabel, graphClass, selectedMemberKey, onSelectMember }: MemberSectionProps) {
  return (
    <div className="uml-sec">
      {members.length === 0 && <div className="uml-row empty">{emptyLabel}</div>}
      {members.map((member) => {
        const key = memberKey(graphClass.id, member)
        const head = member.signature ?? member.name
        return (
          <div
            key={key}
            className={`uml-row${selectedMemberKey === key ? ' selected' : ''}`}
            onClick={(event) => { event.stopPropagation(); onSelectMember(graphClass, member) }}
            title={`${head}${member.type ? ` : ${member.type}` : ''}`}
          >
            <span className="vis">{memberSign(member)}</span>
            <span className="grow">{head}</span>
            {member.type && <span className="ret">: {member.type}</span>}
          </div>
        )
      })}
    </div>
  )
}
