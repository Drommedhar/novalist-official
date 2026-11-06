/**
 * What a scene card's colour means.
 *
 * Novalist had a status dot, plotline swatches inside the Plot Grid and a
 * per-scene label colour, and no way to ask "colour these by viewpoint" - so
 * seeing that four scenes in a row are all Mira's meant reading four cards.
 *
 * Labels and scene stages use the writer's configured colors. Viewpoint and
 * act colors are derived by hash so they need no extra configuration.
 */
export const COLOUR_DIMENSIONS = ['none', 'label', 'stage', 'pov', 'act'] as const

export type ColourDimension = (typeof COLOUR_DIMENSIONS)[number]

/**
 * A stable colour for an arbitrary string.
 *
 * The hash only picks the hue. Saturation and lightness are fixed so every
 * colour in a set has the same weight - a palette where one value shouts and
 * another whispers reads as a ranking that was never meant.
 */
export function autoColour(value: string): string {
  let hash = 0
  for (let i = 0; i < value.length; i++) hash = (hash * 31 + value.charCodeAt(i)) | 0
  return `hsl(${Math.abs(hash) % 360}, 58%, 52%)`
}

interface ColourInputs {
  labelColor?: string | null
  pov?: string | null
  act?: string | null
  stageColor?: string | null
}

/** The card's colour under the chosen dimension, or undefined for no band. */
export function sceneColour(
  dimension: ColourDimension,
  scene: ColourInputs
): string | undefined {
  if (dimension === 'none') return undefined
  // Labels and stages use the colours the writer configured.
  if (dimension === 'label') return scene.labelColor || undefined
  if (dimension === 'stage') return scene.stageColor || undefined

  const value =
    dimension === 'pov' ? scene.pov : scene.act
  // A scene with nothing said gets no band. Colouring it would claim a
  // viewpoint or an act that nobody has decided yet.
  return value && value.trim().length > 0 ? autoColour(value.trim()) : undefined
}
