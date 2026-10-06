/**
 * Demo project content for App Store and manual screenshots.
 *
 * Entirely fictional. Nothing here comes from a real user project — this file is
 * the single source of truth for what the marketing and documentation captures
 * show, so a re-shoot always produces the same populated views.
 */

export const PROJECT_NAME = 'The Cartographer’s Daughter'
export const BOOK_NAME = 'The Cartographer’s Daughter'

export { CHAPTERS } from './demo-chapters.mjs'
export { CHARACTERS, LOCATIONS, ITEMS, LORE } from './demo-entities.mjs'

export const PLOTLINES = [
  'Mira’s Search',
  'The Guild Conspiracy',
  'Roake & the Meridian',
  'The Chart’s Secret'
]

/** scene title -> plotlines active in it */
export const PLOT_CELLS = {
  'A Letter from Bellhaven': ['Mira’s Search', 'The Chart’s Secret'],
  'The Auction on Quay Street': ['Mira’s Search', 'The Guild Conspiracy'],
  'What the Ink Concealed': ['The Chart’s Secret', 'Mira’s Search'],
  'Passage Aboard the Meridian': ['Roake & the Meridian', 'Mira’s Search'],
  'The Island That Isn’t': ['The Chart’s Secret', 'Roake & the Meridian', 'The Guild Conspiracy'],
  'Mutiny at Third Bell': ['The Guild Conspiracy', 'Roake & the Meridian'],
  'The Keeper of the Light': ['Mira’s Search', 'The Chart’s Secret'],
  'The Verdigris Key': ['The Guild Conspiracy', 'The Chart’s Secret'],
  'The Chart Completed': ['Mira’s Search', 'Roake & the Meridian'],
  Landfall: ['Mira’s Search', 'The Guild Conspiracy', 'The Chart’s Secret']
}

// categoryId must be one of the built-in timeline categories: plot, character, world.
export const TIMELINE_EVENTS = [
  { title: 'Silas takes the Corvid north', date: '1847-10-02', description: 'The last survey voyage entered in the Guild register.', category: 'world' },
  { title: 'The Corvid declared lost', date: '1847-10-19', description: 'Nine witnesses. An inquiry that lasts four days.', category: 'world' },
  { title: 'Silas at Cormorant Light', date: '1848-03-09', description: 'Nan Ellery feeds him at her table and enters it in the keeper’s book.', category: 'character' },
  { title: 'The estate is settled', date: '1848-04-11', description: 'Mira signs her name forty times.', category: 'character' },
  { title: 'The letter arrives', date: '1848-11-02', description: 'Cheap paper, no signature, a compass rose with a broken north arm.', category: 'plot' },
  { title: 'The Quay Street auction', date: '1848-11-06', description: 'Forty-one lots. Mira meets Halvard Crane.', category: 'plot' },
  { title: 'The ink is read', date: '1848-11-07', description: 'Ashgrave appears under the candle and fades again.', category: 'plot' },
  { title: 'The Meridian sails', date: '1848-11-14', description: 'Forty pounds and the glass.', category: 'character' },
  { title: 'The channel is run', date: '1848-11-20', description: 'Nine, seven and a half, five — exactly as written.', category: 'plot' },
  { title: 'Vale takes the chart', date: '1848-11-22', description: 'Third bell. Tamsin chooses.', category: 'plot' },
  { title: 'Landfall at Ashgrave', date: '1848-11-26', description: 'Nineteen ledgers, shelved by year.', category: 'world' },
  { title: 'The northern approaches published', date: '1849-06-01', description: 'Six hundred copies in a fortnight. Two inquiries.', category: 'world' }
]

export const GOALS = { daily: 1200, project: 90000 }

// Story dates for the Calendar view, matching the timeline events above. The
// Calendar plots scenes by resolved story date, so without these it renders an
// empty grid.
export const SCENE_DATES = {
  'A Letter from Bellhaven': '1848-11-02',
  'The Auction on Quay Street': '1848-11-06',
  'What the Ink Concealed': '1848-11-07',
  'Passage Aboard the Meridian': '1848-11-14',
  'The Island That Isn’t': '1848-11-20',
  'Mutiny at Third Bell': '1848-11-22',
  'The Keeper of the Light': '1848-11-24',
  'The Verdigris Key': '1848-11-26',
  'The Chart Completed': '1848-11-28',
  Landfall: '1848-11-30'
}

/** Month the Calendar opens on, so the dated scenes are actually in view. */
export const CALENDAR_ANCHOR = '1848-11-01'
