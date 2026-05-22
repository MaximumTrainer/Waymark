import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import type { FlowDefinition } from '../onboarding/types/flow'

vi.mock('reactflow', async () => ({
  default: () => <div data-testid="reactflow-canvas" />,
  Background: () => null,
  Controls: () => null,
  MiniMap: () => null,
}))

// Import after mock
const { VisualJourneyBuilder } = await import('./VisualJourneyBuilder')

const mockFlow: FlowDefinition = {
  id: 'flow-123',
  name: 'Test Flow',
  description: null,
  version: 1,
  nodes: [
    {
      id: '11111111-1111-1111-1111-111111111111',
      key: 'start',
      type: 'Form',
      title: 'Start',
      jsonContent: '{}',
      isStartNode: true,
    },
  ],
  connections: [],
}

describe('VisualJourneyBuilder', () => {
  it('renders metadata inputs with defaults', () => {
    render(
      <VisualJourneyBuilder
        onLoad={vi.fn()}
        onSave={vi.fn()}
        onCreateNew={vi.fn()}
      />,
    )
    expect(screen.getByPlaceholderText(/00000000/)).toBeInTheDocument()
    expect(screen.getByDisplayValue('New Flow')).toBeInTheDocument()
  })

  it('shows error when loading without a flow ID', async () => {
    render(
      <VisualJourneyBuilder
        onLoad={vi.fn()}
        onSave={vi.fn()}
        onCreateNew={vi.fn()}
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: /^load$/i }))
    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/enter a flow id/i)
    })
  })

  it('loads flow and updates name on success', async () => {
    const onLoad = vi.fn().mockResolvedValue(mockFlow)
    render(
      <VisualJourneyBuilder
        onLoad={onLoad}
        onSave={vi.fn()}
        onCreateNew={vi.fn()}
      />,
    )
    const flowIdInput = screen.getByPlaceholderText(/00000000/)
    fireEvent.change(flowIdInput, { target: { value: 'flow-123' } })
    fireEvent.click(screen.getByRole('button', { name: /^load$/i }))
    await waitFor(() => {
      expect(screen.getByDisplayValue('Test Flow')).toBeInTheDocument()
    })
    expect(onLoad).toHaveBeenCalledWith('flow-123')
  })

  it('shows error message on load failure', async () => {
    const onLoad = vi.fn().mockRejectedValue(new Error('Flow not found'))
    render(
      <VisualJourneyBuilder
        onLoad={onLoad}
        onSave={vi.fn()}
        onCreateNew={vi.fn()}
      />,
    )
    const flowIdInput = screen.getByPlaceholderText(/00000000/)
    fireEvent.change(flowIdInput, { target: { value: 'bad-id' } })
    fireEvent.click(screen.getByRole('button', { name: /^load$/i }))
    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/flow not found/i)
    })
  })

  it('renders add-node palette buttons for all node types', () => {
    render(
      <VisualJourneyBuilder
        onLoad={vi.fn()}
        onSave={vi.fn()}
        onCreateNew={vi.fn()}
      />,
    )
    expect(screen.getByRole('button', { name: /\+ Form/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /\+ DocumentUpload/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /\+ Redirect/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /\+ Information/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /\+ Logic/i })).toBeInTheDocument()
  })

  it('shows error when saving new version without a flow ID', async () => {
    render(
      <VisualJourneyBuilder
        onLoad={vi.fn()}
        onSave={vi.fn()}
        onCreateNew={vi.fn()}
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: /save new version/i }))
    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/enter a flow id/i)
    })
  })

  it('calls onCreateNew when Create New is clicked with valid draft', async () => {
    const onCreateNew = vi.fn().mockResolvedValue(mockFlow)
    render(
      <VisualJourneyBuilder
        onLoad={vi.fn()}
        onSave={vi.fn()}
        onCreateNew={onCreateNew}
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: /create new/i }))
    await waitFor(() => {
      expect(onCreateNew).toHaveBeenCalledWith(
        expect.objectContaining({ name: 'New Flow' }),
      )
    })
  })

  it('resets state when Reset is clicked', () => {
    render(
      <VisualJourneyBuilder
        onLoad={vi.fn()}
        onSave={vi.fn()}
        onCreateNew={vi.fn()}
      />,
    )
    const nameInput = screen.getByDisplayValue('New Flow')
    fireEvent.change(nameInput, { target: { value: 'Modified Flow' } })
    expect(screen.getByDisplayValue('Modified Flow')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /reset/i }))
    expect(screen.getByDisplayValue('New Flow')).toBeInTheDocument()
  })
})
