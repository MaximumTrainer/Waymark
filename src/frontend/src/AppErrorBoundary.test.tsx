import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AppErrorBoundary } from './AppErrorBoundary'

// Suppress expected error output during "throws" tests
const suppressConsole = () => {
  const spy = vi.spyOn(console, 'error').mockImplementation(() => undefined)
  return () => spy.mockRestore()
}

const ThrowingChild = ({ shouldThrow = false }: { shouldThrow?: boolean }) => {
  if (shouldThrow) throw new Error('Test error message')
  return <div>Child rendered</div>
}

describe('AppErrorBoundary', () => {
  it('renders children when no error occurs', () => {
    render(
      <AppErrorBoundary>
        <div>Normal child</div>
      </AppErrorBoundary>,
    )
    expect(screen.getByText('Normal child')).toBeInTheDocument()
  })

  it('renders default error UI when a child throws', () => {
    const restore = suppressConsole()
    render(
      <AppErrorBoundary>
        <ThrowingChild shouldThrow />
      </AppErrorBoundary>,
    )
    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(screen.getByText('Something went wrong')).toBeInTheDocument()
    restore()
  })

  it('displays the error message in the default fallback', () => {
    const restore = suppressConsole()
    render(
      <AppErrorBoundary>
        <ThrowingChild shouldThrow />
      </AppErrorBoundary>,
    )
    expect(screen.getByText('Test error message')).toBeInTheDocument()
    restore()
  })

  it('renders custom fallback prop when a child throws', () => {
    const restore = suppressConsole()
    render(
      <AppErrorBoundary fallback={<div>Custom fallback UI</div>}>
        <ThrowingChild shouldThrow />
      </AppErrorBoundary>,
    )
    expect(screen.getByText('Custom fallback UI')).toBeInTheDocument()
    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument()
    restore()
  })

  it('logs the error to console.error via componentDidCatch', () => {
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    render(
      <AppErrorBoundary>
        <ThrowingChild shouldThrow />
      </AppErrorBoundary>,
    )
    expect(consoleSpy).toHaveBeenCalledWith(
      '[AppErrorBoundary] Uncaught error:',
      expect.any(Error),
      expect.anything(),
    )
    consoleSpy.mockRestore()
  })
})
