// Ambient declaration for the darwin-only optional native module so the
// typecheck passes on Windows/Linux where it is not installed. glass.ts loads
// it lazily and no-ops when absent; on macOS the package's own types apply.
declare module 'electron-liquid-glass' {
  const liquidGlass: {
    addView(handle: Buffer, options?: Record<string, unknown>): number
  }
  export default liquidGlass
}
