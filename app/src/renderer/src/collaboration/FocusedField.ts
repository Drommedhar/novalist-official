import { rpc } from '../rpc/client'
import { SharedItemPresence } from './SharedItemPresence'

export interface FieldBinding { sessionId: string; itemId: string }
export interface SharedEntityFields { sessionId: string; fields: Record<string, string>; metadata?: Record<string, string>; presenceItemId?: string }
export interface FieldView { value: string; phase: 'idle' | 'waiting' | 'editing' | 'locked' | 'expired' | 'saving' | 'failed'; error: string; recoveryPath?: string; ownerDeviceId?: string }
interface Lease { acquired: boolean; deviceId: string; remainingMilliseconds: number }
interface Focus {
  id: string
  initial: string
  value: string
  granted: boolean
  ended: boolean
  ready: Promise<void>
  saving: Promise<void> | null
  renewal?: ReturnType<typeof setInterval>
  expiry?: ReturnType<typeof setTimeout>
  renewing: boolean
  storedRevision: number
  presence?: SharedItemPresence
}

export class FocusedField {
  readonly pendingKey = `shared-field:${crypto.randomUUID()}`
  private current: Focus | null = null
  private view: FieldView
  private storedValue: string
  private storedRevision = 0
  private wantsFocus = false
  onChange?: (view: FieldView) => void

  constructor(private readonly binding: FieldBinding, value: string) {
    this.view = { value, phase: 'idle', error: '' }
    this.storedValue = value
  }

  get snapshot(): FieldView { return this.view }
  get hasDraft(): boolean { return this.current !== null && this.current.value !== this.current.initial }

  stored(value: string): void {
    if (this.storedValue !== value) { this.storedValue = value; this.storedRevision++ }
    if (!this.current) this.publish({ value })
    else if (!this.current.granted) {
      this.current.value = value
      this.current.initial = value
      this.publish({ value })
    }
  }
  change(value: string): void {
    if (!this.current || this.current.ended || this.view.phase !== 'editing') return
    this.current.value = value
    this.publish({ value })
  }

  focus(): Promise<void> {
    this.wantsFocus = true
    if (this.current) return this.current.ready
    const focus: Focus = { id: crypto.randomUUID(), initial: this.view.value, value: this.view.value,
      granted: false, ended: false, ready: Promise.resolve(), saving: null, renewing: false,
      storedRevision: this.storedRevision }
    this.current = focus
    this.publish({ phase: 'waiting', error: '' })
    const started = performance.now()
    focus.ready = rpc.request<{ value: string; lease: Lease }>('collaboration/acquireField', this.params(focus))
      .then(result => {
        focus.granted = result.lease.acquired
        focus.initial = result.value
        focus.value = result.value
        if (focus.ended) return
        this.publish({ value: result.value, phase: focus.granted ? 'editing' : 'locked', ownerDeviceId: result.lease.deviceId })
        if (focus.granted) {
          focus.presence = new SharedItemPresence(this.binding, focus.id)
          focus.renewal = setInterval(() => void this.renew(focus), 10_000)
          this.deadline(focus, result.lease, started)
        } else {
          focus.expiry = setTimeout(() => {
            if (this.current !== focus || focus.ended || !this.wantsFocus) return
            this.current = null
            this.focus()
          }, 1000)
        }
      }).catch(error => { if (!focus.ended) this.fail(error) })
    return focus.ready
  }

  blur(): void { this.wantsFocus = false }

  finish(preserveFocus = false): Promise<void> {
    if (!preserveFocus) this.wantsFocus = false
    const focus = this.current
    if (!focus) return Promise.resolve()
    if (focus.saving) return focus.saving
    focus.ended = true
    this.clearTimers(focus)
    this.publish({ phase: 'saving' })
    focus.saving = this.saveAndRelease(focus).catch(error => { this.fail(error); throw error })
      .finally(() => { focus.saving = null })
    return focus.saving
  }

  recover(): Promise<void> {
    const focus = this.current
    if (!focus || !this.hasDraft) return Promise.resolve()
    if (focus.saving) return focus.saving
    this.wantsFocus = false
    focus.ended = true
    this.clearTimers(focus)
    this.publish({ phase: 'saving', error: '' })
    focus.saving = this.recoverAndRelease(focus).catch(error => { this.fail(error); throw error })
      .finally(() => { focus.saving = null })
    return focus.saving
  }

  private async recoverAndRelease(focus: Focus): Promise<void> {
    const recoveryPath = await rpc.request<string>('collaboration/recoverField', [...this.params(focus), focus.value])
    this.current = null
    this.publish({ value: this.storedValue, phase: 'idle', error: '', recoveryPath })
    try { await rpc.request('collaboration/releaseField', this.params(focus)) }
    catch (error) { if (!this.current) this.publish({ error: String(error) }) }
  }

  private async saveAndRelease(focus: Focus): Promise<void> {
    await focus.ready
    if (focus.granted && focus.value !== focus.initial) {
      await rpc.request('collaboration/writeField', [...this.params(focus), focus.value])
      focus.initial = focus.value
    }
    await rpc.request('collaboration/releaseField', this.params(focus))
    if (this.current === focus) {
      this.current = null
      const value = this.storedRevision > focus.storedRevision ? this.storedValue : focus.value
      this.publish({ value, phase: 'idle', error: '' })
      if (this.wantsFocus) this.focus()
    }
  }

  private async renew(focus: Focus): Promise<void> {
    if (focus.ended || focus.renewing || this.current !== focus) return
    focus.renewing = true
    const started = performance.now()
    try {
      const lease = await rpc.request<Lease | null>('collaboration/renewField', this.params(focus))
      if (focus.ended || this.current !== focus) return
      if (!lease?.acquired) this.expire(focus)
      else this.deadline(focus, lease, started)
    } catch (error) {
      if (!focus.ended && this.current === focus) { this.clearTimers(focus); this.fail(error) }
    } finally { focus.renewing = false }
  }

  private deadline(focus: Focus, lease: Lease, started: number): void {
    clearTimeout(focus.expiry)
    const remaining = lease.remainingMilliseconds - (performance.now() - started)
    if (!Number.isFinite(remaining) || remaining <= 0 || remaining > 30_000) { this.expire(focus); return }
    focus.expiry = setTimeout(() => this.expire(focus), remaining)
  }

  private expire(focus: Focus): void {
    this.clearTimers(focus)
    if (!focus.ended && this.current === focus) this.publish({ phase: 'expired' })
  }

  private clearTimers(focus: Focus): void {
    clearInterval(focus.renewal)
    clearTimeout(focus.expiry)
    focus.presence?.close()
  }
  private params(focus: Focus): string[] { return [this.binding.sessionId, this.binding.itemId, focus.id] }
  private fail(error: unknown): void { this.publish({ phase: 'failed', error: String(error) }) }
  private publish(change: Partial<FieldView>): void {
    this.view = { ...this.view, ...change }
    this.onChange?.(this.view)
  }
}
