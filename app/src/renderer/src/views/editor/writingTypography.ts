import type { EffectiveSettings } from '../../stores/settingsStore'
import { useWritingZoomStore } from '../../stores/writingZoomStore'
import { PAGE_FORMAT_WIDTHS } from '../settings/settingsTypography'

interface WritingSurface {
  setFont(family: string, size: number): void
  setBookWidth(enabled: boolean, widthPx: number): void
  setWritingZoom(factor: number, typewriterAnchor?: string | null): void
}

export function writingFontSize(settings: EffectiveSettings): number {
  return settings.enableBookWidth ? settings.bookFontSize : settings.editorFontSize
}

export function pushWritingTypography(surface: WritingSurface, settings: EffectiveSettings): void {
  const inches = settings.bookPageFormat === 'Custom'
    ? settings.bookTextBlockWidth ?? PAGE_FORMAT_WIDTHS.USTrade6x9
    : PAGE_FORMAT_WIDTHS[settings.bookPageFormat] ?? PAGE_FORMAT_WIDTHS.USTrade6x9
  surface.setFont(
    settings.enableBookWidth ? settings.bookFontFamily : settings.editorFontFamily,
    writingFontSize(settings)
  )
  surface.setBookWidth(settings.enableBookWidth, inches * 96)
  surface.setWritingZoom(
    useWritingZoomStore.getState().percent / 100,
    settings.typewriterScrollEnabled && window.novalist.isMobile !== true ? settings.typewriterScrollAnchor : null
  )
}
