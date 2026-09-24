// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { I18nProvider } from '../i18n/I18nProvider'
import type { StartupFlowDocument, StartupStep } from '../types/graph'
import { StartupFlowView } from './StartupFlowView'

function step(label: string, overrides: Partial<StartupStep> = {}): StartupStep {
  return {
    kind: 'Registration',
    label,
    serviceType: null,
    serviceTypeId: null,
    implementationType: null,
    implementationTypeId: null,
    lifetime: null,
    file: 'Program.cs',
    line: 1,
    children: [],
    ...overrides,
  }
}

const flow: StartupFlowDocument = {
  projectKey: 'Api',
  programFile: 'C:\\src\\Api\\Program.cs',
  serviceSteps: [
    step('ConfigureDatabase()', {
      kind: 'ExtensionCall',
      children: [step('IGameDb → GameDb', { lifetime: 'Singleton' })],
    }),
    step('AddControllers()'),
  ],
  middlewareSteps: [
    step('UseAuthentication()', { kind: 'Middleware' }),
    step('MapControllers()', { kind: 'Middleware' }),
  ],
}

afterEach(cleanup)

function renderView(onSelect = vi.fn()) {
  render(
    <I18nProvider>
      <StartupFlowView
        flow={flow}
        selection={null}
        onSelect={onSelect}
        projectKeys={['Api']}
        projectKey="Api"
        onChangeProject={() => undefined}
      />
    </I18nProvider>,
  )
  return onSelect
}

describe('StartupFlowView', () => {
  it('renders startup as staged code flow instead of a flat list', () => {
    renderView()

    expect(screen.getByLabelText(/startup code flow diagram|시작 코드 흐름도/i)).toBeTruthy()
    expect(screen.getByText('Program.cs')).toBeTruthy()
    expect(screen.getByText('ConfigureDatabase()')).toBeTruthy()
    expect(screen.getByText('UseAuthentication()')).toBeTruthy()
    expect(document.querySelectorAll('.startup-stage')).toHaveLength(2)
    expect(document.querySelectorAll('.startup-node-cell')).toHaveLength(4)
  })

  it('selects a node and expands an extension method branch', () => {
    const onSelect = renderView()

    fireEvent.click(screen.getByRole('button', { name: /ConfigureDatabase/ }))

    expect(onSelect).toHaveBeenCalledWith('svc:0')
    expect(screen.getByText('IGameDb → GameDb')).toBeTruthy()
  })
})
