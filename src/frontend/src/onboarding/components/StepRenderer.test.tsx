import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { StepRenderer } from './StepRenderer'
import type { FlowNode } from '../types/flow'

const makeNode = (overrides: Partial<FlowNode> = {}): FlowNode => ({
  id: 'node-1',
  key: 'step-1',
  type: 'Form',
  title: 'Test Step',
  jsonContent: '{}',
  ...overrides,
})

describe('StepRenderer', () => {
  it('renders completion message when node is null', () => {
    render(<StepRenderer node={null} onSubmit={vi.fn()} />)
    expect(screen.getByText('Journey complete 🎉')).toBeInTheDocument()
  })

  it('renders form fields from jsonContent', () => {
    const jsonContent = JSON.stringify({
      fields: [
        { name: 'firstName', type: 'text', required: true },
        { name: 'age', type: 'number' },
      ],
    })
    render(<StepRenderer node={makeNode({ jsonContent })} onSubmit={vi.fn()} />)

    expect(screen.getByLabelText(/firstName/)).toBeInTheDocument()
    expect(screen.getByLabelText(/age/)).toBeInTheDocument()
  })

  it('renders select field with options', () => {
    const jsonContent = JSON.stringify({
      fields: [{ name: 'country', type: 'select', options: ['UK', 'US', 'DE'] }],
    })
    render(<StepRenderer node={makeNode({ jsonContent })} onSubmit={vi.fn()} />)

    const select = screen.getByRole('combobox')
    expect(select).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'UK' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'US' })).toBeInTheDocument()
  })

  it('renders checkbox field', () => {
    const jsonContent = JSON.stringify({
      fields: [{ name: 'agreed', type: 'checkbox' }],
    })
    render(<StepRenderer node={makeNode({ jsonContent })} onSubmit={vi.fn()} />)

    expect(screen.getByRole('checkbox')).toBeInTheDocument()
  })

  it('calls onSubmit with field values when form is submitted', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined)
    const jsonContent = JSON.stringify({
      fields: [{ name: 'email', type: 'email' }],
    })
    render(<StepRenderer node={makeNode({ jsonContent })} onSubmit={onSubmit} />)

    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'test@example.com' } })
    fireEvent.click(screen.getByRole('button', { name: /submit/i }))

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith({ email: 'test@example.com' })
    })
  })

  it('renders Information node with Continue button', () => {
    const node = makeNode({ type: 'Information', jsonContent: '{"message":"Read this carefully"}' })
    render(<StepRenderer node={node} onSubmit={vi.fn()} />)
    expect(screen.getByText('Read this carefully')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /continue/i })).toBeInTheDocument()
  })

  it('renders Redirect node with external link', () => {
    const node = makeNode({ type: 'Redirect', jsonContent: '{"url":"https://example.com"}' })
    render(<StepRenderer node={node} onSubmit={vi.fn()} />)
    const link = screen.getByRole('link')
    // new URL() normalises to trailing slash
    expect(link).toHaveAttribute('href', 'https://example.com/')
  })

  it('renders no-fields message when form schema has empty fields array', () => {
    const jsonContent = JSON.stringify({ fields: [] })
    render(<StepRenderer node={makeNode({ jsonContent })} onSubmit={vi.fn()} />)
    expect(screen.getByText('No fields defined for this step.')).toBeInTheDocument()
  })
})
