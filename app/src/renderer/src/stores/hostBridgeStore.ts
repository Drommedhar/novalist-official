import { create } from 'zustand'
import { rpc } from '../rpc/client'

/**
 * Bridges the backend extension host's imperative UI capabilities — toasts,
 * busy-progress dialogs, and interactive wizards — to React. The backend pushes
 * these as `ui/*` JSON-RPC notifications over the same MessagePort the RPC client
 * already owns; this store subscribes once (at module load) and buffers them so
 * the overlay components stay purely declarative.
 */

export interface HostToast {
  id: number
  message: string
}

export interface HostProgress {
  token: string
  title: string
  status: string
  indeterminate: boolean
  showProgressBar: boolean
  progress: number
  allowCancel: boolean
  cancelLabel: string | null
  isModal: boolean
  details: string[]
}

export interface WizardCondition {
  stepId: string
  operator: string
  value: string | null
}

export interface WizardChoice {
  value: string
  label: string
  description: string | null
}

export interface WizardStepDef {
  kind: string
  id: string
  title: string
  help: string | null
  skippable: boolean
  visibleWhen: WizardCondition | null
  hasValidator: boolean
  multiline: boolean
  maxLength: number | null
  placeholder: string | null
  exampleValue: string | null
  choices: WizardChoice[] | null
  multiSelect: boolean
  hasDynamicChoices: boolean
  autoSkipIfChoicesEmpty: boolean
  min: number | null
  max: number | null
  defaultNumber: number
  unit: string | null
  allowInWorld: boolean
  targetEntityTypeKey: string | null
  minCount: number | null
  maxCount: number | null
  subSteps: WizardStepDef[] | null
}

export interface WizardDefinitionDto {
  id: string
  displayName: string
  description: string
  scope: string
  entityTypeKey: string | null
  steps: WizardStepDef[]
}

export interface WizardAnswer {
  text?: string
  number?: number
  multi?: string[]
}

export interface WizardResultDto {
  definitionId: string
  answers: Record<string, WizardAnswer>
  currentStepIndex: number
  completed: boolean
}

export interface WizardSession {
  token: string
  definition: WizardDefinitionDto
  seed: WizardResultDto | null
}

interface HostBridgeState {
  toasts: HostToast[]
  progress: HostProgress[]
  wizard: WizardSession | null
  pushToast(message: string): void
  dismissToast(id: number): void
  cancelProgress(token: string): void
  closeWizard(): void
}

/** Reads a JSON-RPC notification's first positional param (StreamJsonRpc sends
 * single-arg notifications as a one-element array). */
function firstParam<T>(params: unknown): T {
  return (Array.isArray(params) ? params[0] : params) as T
}

let nextToastId = 1
const TOAST_TIMEOUT_MS = 5000

export const useHostBridgeStore = create<HostBridgeState>((set, get) => ({
  toasts: [],
  progress: [],
  wizard: null,

  pushToast: (message) => {
    const id = nextToastId++
    set((s) => ({ toasts: [...s.toasts, { id, message }] }))
    setTimeout(() => get().dismissToast(id), TOAST_TIMEOUT_MS)
  },

  dismissToast: (id) => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),

  cancelProgress: (token) => {
    rpc.notify('ui/progress/cancel', [token])
  },

  closeWizard: () => set({ wizard: null })
}))

/** Applies a `ui/progress/update` field patch onto the matching dialog. */
function applyProgressUpdate(list: HostProgress[], token: string, field: string, value: unknown): HostProgress[] {
  return list.map((p) => {
    if (p.token !== token) return p
    switch (field) {
      case 'status':
        return { ...p, status: String(value ?? '') }
      case 'title':
        return { ...p, title: String(value ?? '') }
      case 'progress':
        return { ...p, progress: typeof value === 'number' ? value : p.progress }
      case 'indeterminate':
        return { ...p, indeterminate: Boolean(value) }
      case 'details':
        return { ...p, details: Array.isArray(value) ? (value as string[]) : [] }
      default:
        return p
    }
  })
}

let registered = false

/** Subscribes the store to the backend's `ui/*` notifications. Idempotent. */
export function registerHostBridge(): void {
  if (registered) return
  registered = true

  rpc.onNotification('ui/showNotification', (params) => {
    const message = firstParam<string>(params)
    if (typeof message === 'string' && message.length > 0) {
      useHostBridgeStore.getState().pushToast(message)
    }
  })

  rpc.onNotification('ui/progress/open', (params) => {
    const dto = firstParam<{
      token: string
      title: string
      initialStatus: string
      isIndeterminate: boolean
      showProgressBar: boolean
      allowCancel: boolean
      cancelLabel: string | null
      isModal: boolean
    }>(params)
    const entry: HostProgress = {
      token: dto.token,
      title: dto.title,
      status: dto.initialStatus,
      indeterminate: dto.isIndeterminate,
      showProgressBar: dto.showProgressBar,
      progress: 0,
      allowCancel: dto.allowCancel,
      cancelLabel: dto.cancelLabel,
      isModal: dto.isModal,
      details: []
    }
    useHostBridgeStore.setState((s) => ({ progress: [...s.progress.filter((p) => p.token !== dto.token), entry] }))
  })

  rpc.onNotification('ui/progress/update', (params) => {
    const dto = firstParam<{ token: string; field: string; value: unknown }>(params)
    useHostBridgeStore.setState((s) => ({
      progress: applyProgressUpdate(s.progress, dto.token, dto.field, dto.value)
    }))
  })

  rpc.onNotification('ui/progress/close', (params) => {
    const token = firstParam<string>(params)
    useHostBridgeStore.setState((s) => ({ progress: s.progress.filter((p) => p.token !== token) }))
  })

  rpc.onNotification('ui/wizard/open', (params) => {
    const dto = firstParam<{ token: string; definition: WizardDefinitionDto; seed: WizardResultDto | null }>(params)
    useHostBridgeStore.setState({
      wizard: { token: dto.token, definition: dto.definition, seed: dto.seed ?? null }
    })
  })
}

registerHostBridge()
