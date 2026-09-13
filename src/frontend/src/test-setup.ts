import '@testing-library/jest-dom/vitest'

// jsdom implements neither of these, and React Flow (the journey canvas) calls both on mount.
// Without them any test that renders a page containing the canvas throws before asserting.
if (!('ResizeObserver' in globalThis)) {
  globalThis.ResizeObserver = class ResizeObserver {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
}

if (!('DOMMatrixReadOnly' in globalThis)) {
  globalThis.DOMMatrixReadOnly = class DOMMatrixReadOnly {
    m22 = 1
  } as unknown as typeof DOMMatrixReadOnly
}
