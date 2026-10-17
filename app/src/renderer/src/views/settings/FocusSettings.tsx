import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettingsStore } from '../../stores/settingsStore'
import { focusColor, focusImage, focusPeekDelay, MAX_FOCUS_IMAGE_BYTES } from '../../stores/focusPreferences'
import { SettingNumber } from './SettingsInputs'

async function readBackground(file: File): Promise<string> {
  if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) || file.size > MAX_FOCUS_IMAGE_BYTES) {
    throw new Error('invalid image')
  }
  const bitmap = await createImageBitmap(file)
  bitmap.close()
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onerror = () => reject(reader.error)
    reader.onload = () => {
      const image = focusImage(reader.result)
      if (image) resolve(image)
      else reject(new Error('invalid image'))
    }
    reader.readAsDataURL(file)
  })
}

export function FocusSettings(): React.JSX.Element {
  const { t } = useTranslation()
  const settings = useSettingsStore((state) => state.view?.global)
  const update = useSettingsStore((state) => state.update)
  const [imageError, setImageError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const enabled = settings?.focusPeekEnabled !== false
  const image = focusImage(settings?.focusBackgroundImage)
  const chooseImage = async (file: File): Promise<void> => {
    setLoading(true)
    setImageError(null)
    try {
      const data = await readBackground(file)
      await update('global', { focusBackgroundImage: data })
    } catch {
      setImageError(t('settings.focusBackgroundImageError'))
    } finally {
      setLoading(false)
    }
  }
  return <>
    <label className="relationships-toggle">
      <input id="set-focus-peek" type="checkbox" checked={enabled}
        onChange={(event) => void update('global', { focusPeekEnabled: event.target.checked })} />
      {t('settings.focusPeekEnabled')}
    </label>
    <div className="settings-hint">{t('settings.focusPeekEnabledDesc')}</div>
    {enabled && <>
      <label className="inspector-label" htmlFor="set-focus-peek-delay">{t('settings.focusPeekDelay')}</label>
      <SettingNumber id="set-focus-peek-delay" min={0} max={5000} step={100}
        value={focusPeekDelay(settings?.focusPeekDelayMs)}
        onCommit={(value) => void update('global', { focusPeekDelayMs: value })} />
    </>}
    <div className="settings-hint">{t('settings.focusAppearanceDesc')}</div>
    {(['focusPageColor', 'focusBackgroundColor'] as const).map((key) => {
      const id = key === 'focusPageColor' ? 'set-focus-page-color' : 'set-focus-background-color'
      const color = focusColor(settings?.[key])
      return <div key={key}>
        <label className="inspector-label" htmlFor={id}>{t(`settings.${key}`)}</label>
        <input id={id} type="color" value={color ?? '#ffffff'}
          onChange={(event) => void update('global', { [key]: event.target.value })} />
        <button className="dialog-button" disabled={!color}
          onClick={() => void update('global', { [key]: null })}>{t('settings.focusUseTheme')}</button>
      </div>
    })}
    <label className="inspector-label" htmlFor="set-focus-background-image">{t('settings.focusBackgroundImage')}</label>
    <input id="set-focus-background-image" type="file" accept="image/png,image/jpeg,image/webp" disabled={loading}
      onChange={(event) => {
        const file = event.target.files?.[0]
        event.target.value = ''
        if (file) void chooseImage(file)
      }} />
    <div className="settings-hint">{t('settings.focusBackgroundImageDesc')}</div>
    {image && <button className="dialog-button" disabled={loading}
      onClick={() => void update('global', { focusBackgroundImage: null })}>{t('settings.focusRemoveImage')}</button>}
    {imageError && <p role="alert">{imageError}</p>}
  </>
}
