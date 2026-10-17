export const MAX_FOCUS_IMAGE_BYTES = 5 * 1024 * 1024

export function focusColor(value: unknown): string | null {
  return typeof value === 'string' && /^#[\da-f]{6}$/i.test(value) ? value : null
}

export function focusImage(value: unknown): string | null {
  return typeof value === 'string' && value.length <= Math.ceil(MAX_FOCUS_IMAGE_BYTES / 3) * 4 + 32 &&
    /^data:image\/(png|jpeg|webp);base64,[A-Za-z0-9+/]+=*$/.test(value) ? value : null
}

export function focusPeekDelay(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? Math.min(5000, Math.max(0, value)) : 500
}
