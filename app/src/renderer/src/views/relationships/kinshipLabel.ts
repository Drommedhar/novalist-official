import type { TFunction } from 'i18next'

/** What the backend returns: shape, not words. */
export interface KinshipRow {
  entityId: string
  kind: string
  degree: number
  removed: number
}

/**
 * The words for a derived relationship.
 *
 * Kept out of the locale plural machinery because kinship is not a plural: a
 * degree of one is a parent, two is a grandparent, and only from three does the
 * word start collecting "greats". Expressing that as one pluralised string gets
 * "2x great-grandparent" for somebody's gran, which is wrong in a way that
 * makes the whole feature untrustworthy.
 */
export function kinshipLabel(t: TFunction, row: KinshipRow): string {
  const greats = (base: string): string =>
    row.degree <= 1
      ? t(`kinship.${base}`)
      : row.degree === 2
        ? t(`kinship.grand${base}`)
        : row.degree === 3
          ? t(`kinship.great${base}`)
          : t(`kinship.greats${base}`, { greats: row.degree - 2 })

  switch (row.kind) {
    case 'Ancestor':
      return greats('parent')
    case 'Descendant':
      return greats('child')
    case 'Sibling':
      return t('kinship.sibling')
    case 'AuntUncle':
      return row.degree <= 1
        ? t('kinship.auntUncle')
        : row.degree === 2
          ? t('kinship.greatAuntUncle')
          : t('kinship.greatsAuntUncle', { greats: row.degree - 1 })
    case 'NieceNephew':
      return row.degree <= 1
        ? t('kinship.nieceNephew')
        : row.degree === 2
          ? t('kinship.greatNieceNephew')
          : t('kinship.greatsNieceNephew', { greats: row.degree - 1 })
    case 'Cousin':
      return row.removed === 0
        ? t('kinship.cousin', { count: row.degree })
        : t('kinship.cousinRemoved', { count: row.degree, removed: row.removed })
    default:
      return ''
  }
}
