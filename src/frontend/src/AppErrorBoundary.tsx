import { Component, type ErrorInfo, type ReactNode } from 'react'

type Props = {
  children: ReactNode
  fallback?: ReactNode
}

type State = {
  hasError: boolean
  error: Error | null
}

/**
 * React error boundary that catches runtime errors in its child tree.
 * Renders a fallback UI instead of propagating the crash to the root.
 */
export class AppErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props)
    this.state = { hasError: false, error: null }
  }

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error }
  }

  override componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('[AppErrorBoundary] Uncaught error:', error, info.componentStack)
  }

  override render() {
    if (this.state.hasError) {
      if (this.props.fallback) {
        return this.props.fallback
      }

      return (
        <main
          role="alert"
          className="mx-auto flex min-h-screen max-w-xl items-center p-6"
        >
          <section className="w-full space-y-4 rounded-lg border border-rose-200 bg-rose-50 p-6 shadow-sm">
            <h1 className="text-xl font-bold text-rose-900">Something went wrong</h1>
            <p className="text-sm text-rose-700">
              An unexpected error occurred. Please refresh the page or contact support.
            </p>
            {this.state.error && (
              <pre className="overflow-auto rounded bg-rose-100 p-3 text-xs text-rose-800">
                {this.state.error.message}
              </pre>
            )}
            <button
              type="button"
              onClick={() => window.location.reload()}
              className="rounded bg-rose-900 px-4 py-2 text-sm font-medium text-white hover:bg-rose-800"
            >
              Reload page
            </button>
          </section>
        </main>
      )
    }

    return this.props.children
  }
}
