declare const __NOVALIST_STEAM_BUILD__: boolean

export const selfUpdateDisabled =
  __NOVALIST_STEAM_BUILD__ ||
  (process as NodeJS.Process & { mas?: boolean }).mas === true ||
  process.env.NOVALIST_DISABLE_SELF_UPDATE === '1'
