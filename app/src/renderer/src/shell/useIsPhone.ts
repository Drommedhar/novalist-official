import { useEffect, useState } from 'react'

/**
 * The phone layout: the mobile shell on a screen narrow enough to need it.
 *
 * Not the same question as "is this the mobile build". The iOS app ships for
 * iPad as well (UIDeviceFamily 1 and 2), and an iPad - or an iPhone held
 * sideways - has the room for the full layouts. Asking about width keeps the
 * phone reshapes off the screens that do not need them, and matches the width
 * bound the phone token block and the mobile overrides use.
 */
const PHONE_QUERY = '(max-width: 700px)'

export function useIsPhone(): boolean {
  const [phone, setPhone] = useState(
    () => window.novalist.isMobile === true && window.matchMedia(PHONE_QUERY).matches
  )

  useEffect(() => {
    if (window.novalist.isMobile !== true) return undefined
    const query = window.matchMedia(PHONE_QUERY)
    const update = (): void => setPhone(query.matches)
    update()
    query.addEventListener('change', update)
    return () => query.removeEventListener('change', update)
  }, [])

  return phone
}
