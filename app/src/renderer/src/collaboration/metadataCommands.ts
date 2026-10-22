import i18n from '../i18n'
import { rpc } from '../rpc/client'
import { useHostBridgeStore } from '../stores/hostBridgeStore'
import { persistPendingWrite } from '../stores/pendingWrites'
import { FocusedField, type FieldBinding } from './FocusedField'
import { useParticipantStore, type Participant } from './participants'

async function lockedMessage(binding: FieldBinding, deviceId?: string): Promise<string> {
  let participants = useParticipantStore.getState()[binding.sessionId]
  if (!participants?.some(participant => participant.deviceId === deviceId)) {
    participants = await rpc.request<Participant[]>('collaboration/participants', [binding.sessionId])
    useParticipantStore.setState({ [binding.sessionId]: participants })
  }
  const owner = participants.find(participant => participant.deviceId === deviceId)
  return owner ? i18n.t('collaboration.fieldLockedBy', { name: owner.name }) : i18n.t('collaboration.fieldLocked')
}

export function writeSharedMetadata<T>(binding: FieldBinding, next: T | ((previous: T) => T)): Promise<boolean> {
  return writeField(binding, previous => JSON.stringify(typeof next === 'function'
    ? (next as (previous: T) => T)(JSON.parse(previous) as T) : next))
}

export function writeSharedField(binding: FieldBinding, next: string | ((previous: string) => string)): Promise<boolean> {
  return writeField(binding, previous => typeof next === 'function' ? next(previous) : next)
}

async function writeField(binding: FieldBinding, update: (previous: string) => string): Promise<boolean> {
  const controller = new FocusedField(binding, 'null')
  try {
    await controller.focus()
    const focused = controller.snapshot
    if (focused.phase !== 'editing') {
      await controller.finish()
      const message = focused.error || (focused.phase === 'expired' ? i18n.t('collaboration.fieldExpired')
        : await lockedMessage(binding, focused.ownerDeviceId))
      useHostBridgeStore.getState().pushToast(message)
      return false
    }
    let value: string
    try { value = update(focused.value) }
    catch (error) { await controller.finish(); throw error }
    controller.change(value)
    await persistPendingWrite(controller.pendingKey, () => controller.finish())
    return true
  } catch (error) {
    useHostBridgeStore.getState().pushToast(String(error))
    return false
  }
}
