/** Encodes an independent clip: timesliced WebM fragments cannot be decoded alone. */
export function wav(samples: Float32Array[], sampleRate: number): Uint8Array {
  const count = samples.reduce((sum, frame) => sum + frame.length, 0)
  const bytes = new Uint8Array(44 + count * 2)
  const view = new DataView(bytes.buffer)
  const ascii = (offset: number, value: string): void => {
    for (let i = 0; i < value.length; i++) bytes[offset + i] = value.charCodeAt(i)
  }
  ascii(0, 'RIFF'); view.setUint32(4, bytes.length - 8, true); ascii(8, 'WAVE')
  ascii(12, 'fmt '); view.setUint32(16, 16, true); view.setUint16(20, 1, true)
  view.setUint16(22, 1, true); view.setUint32(24, sampleRate, true)
  view.setUint32(28, sampleRate * 2, true); view.setUint16(32, 2, true); view.setUint16(34, 16, true)
  ascii(36, 'data'); view.setUint32(40, count * 2, true)
  let offset = 44
  for (const frame of samples) for (const sample of frame) {
    const clamped = Math.max(-1, Math.min(1, sample))
    view.setInt16(offset, Math.round(clamped * (clamped < 0 ? 32768 : 32767)), true)
    offset += 2
  }
  return bytes
}

/** Pause-delimited speech with a bounded clip size; silence is never transcribed. */
export class SpeechChunks {
  private frames: Float32Array[] = []
  private count = 0
  private voiced = 0
  private silence = 0
  constructor(private rate: number, private emit: (audio: Uint8Array) => void, private onLevel?: (level: number) => void) {}
  push(frame: Float32Array): void {
    const rms = Math.sqrt(frame.reduce((sum, v) => sum + v * v, 0) / frame.length)
    this.onLevel?.(Math.max(0, Math.min(1, (20 * Math.log10(Math.max(rms, 0.000001)) + 60) / 60)))
    this.frames.push(frame)
    this.count += frame.length
    if (rms > 0.008) { this.voiced += frame.length; this.silence = 0 }
    else this.silence += frame.length
    // Keep a short lead-in so the start of a word is not clipped by the gate.
    if (!this.voiced && this.count > this.rate * 0.3) {
      this.count -= this.frames.shift()!.length
    }
    if (this.voiced && ((this.silence >= this.rate * 0.8 && this.count >= this.rate * 1.5)
      || this.count >= this.rate * 25)) this.flush()
  }
  flush(): void {
    const frames = this.frames
    const voiced = this.voiced
    this.frames = []; this.count = 0; this.voiced = 0; this.silence = 0
    if (voiced >= this.rate * 0.12) this.emit(wav(frames, this.rate))
  }
}

export interface Microphone { stop(): Promise<void> }

export async function openMicrophone(onClip: (audio: Uint8Array) => void,
  onEnded: () => void, signal: AbortSignal, options: { deviceId?: string; onLevel?: (level: number) => void } = {}): Promise<Microphone> {
  if (window.novalist.systemMicrophone) return openSystemMicrophone(window.novalist.systemMicrophone, onClip, onEnded, signal, options.onLevel)
  const stream = await navigator.mediaDevices.getUserMedia({ audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true,
    ...(options.deviceId ? { deviceId: { exact: options.deviceId } } : {}) } })
  if (signal.aborted) { stream.getTracks().forEach((t) => t.stop()); throw new DOMException('Aborted', 'AbortError') }
  const context = new AudioContext({ sampleRate: 16000 })
  let node: AudioWorkletNode | undefined
  let stopped = false
  const chunks = new SpeechChunks(context.sampleRate, onClip, options.onLevel)
  const stop = async (): Promise<void> => {
    if (stopped) return
    stopped = true
    stream.getTracks().forEach((track) => track.stop())
    if (node) {
      // Flush the processor's last partial frame before closing the context.
      await new Promise<void>((resolve) => {
        const timer = window.setTimeout(resolve, 250)
        node!.port.onmessage = (event: MessageEvent<Float32Array | string>) => {
          if (event.data === 'stopped') { window.clearTimeout(timer); resolve() }
          else if (event.data instanceof Float32Array) chunks.push(event.data)
        }
        node!.port.postMessage('stop')
      })
      node.disconnect()
    }
    chunks.flush()
    await context.close()
    options.onLevel?.(0)
  }
  signal.addEventListener('abort', () => { void stop() }, { once: true })
  try {
    await context.audioWorklet.addModule(new URL('dictation-capture.js', document.baseURI).href)
    if (signal.aborted) throw new DOMException('Aborted', 'AbortError')
    node = new AudioWorkletNode(context, 'novalist-dictation')
    node.port.onmessage = (event: MessageEvent<Float32Array>) => chunks.push(event.data)
    context.createMediaStreamSource(stream).connect(node)
    node.connect(context.destination) // The processor outputs silence.
    for (const track of stream.getAudioTracks()) track.onended = onEnded
    await context.resume()
    return { stop }
  } catch (error) {
    await stop()
    throw error
  }
}

export async function openSystemMicrophone(capture: NonNullable<Window['novalist']['systemMicrophone']>, onClip: (audio: Uint8Array) => void,
  onEnded: () => void, signal: AbortSignal, onLevel?: (level: number) => void): Promise<Microphone> {
  let timer: ReturnType<typeof setTimeout> | undefined
  let stopped = false
  let reading: Promise<void> | undefined
  let stopping: Promise<void> | undefined
  const emit = (clips: string[]): void => {
    for (const clip of clips) onClip(Uint8Array.from(atob(clip), (char) => char.charCodeAt(0)))
  }
  await capture.start()
  const stop = (): Promise<void> => {
    if (stopping) return stopping
    stopped = true
    if (timer) clearTimeout(timer)
    stopping = (async () => {
      await reading
      emit((await capture.stop()).clips)
      onLevel?.(0)
    })()
    return stopping
  }
  if (signal.aborted) { await stop(); throw new DOMException('Aborted', 'AbortError') }
  signal.addEventListener('abort', () => { void stop() }, { once: true })
  const poll = (): void => {
    if (stopped) return
    reading = capture.read().then((result) => {
      onLevel?.(result.level ?? 0)
      emit(result.clips)
      if (result.ended) onEnded()
    }).catch(() => { onEnded() }).finally(() => {
      reading = undefined
      if (!stopped) timer = setTimeout(poll, 200)
    })
  }
  poll()
  return { stop }
}

export async function microphoneDevices(requestAccess = false): Promise<{ id: string; label: string }[]> {
  if (requestAccess) {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
    stream.getTracks().forEach(track => track.stop())
  }
  return (await navigator.mediaDevices.enumerateDevices()).filter(device => device.kind === 'audioinput' && device.deviceId
    && device.deviceId !== 'default').map(device => ({ id: device.deviceId, label: device.label }))
}

export function audioBase64(bytes: Uint8Array): string {
  let binary = ''
  for (let i = 0; i < bytes.length; i += 8192) binary += String.fromCharCode(...bytes.subarray(i, i + 8192))
  return btoa(binary)
}
