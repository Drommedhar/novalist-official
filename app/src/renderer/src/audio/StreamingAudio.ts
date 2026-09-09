/** Schedules decoded chunks contiguously on one audio clock. No per-chunk
 * fades, silence, or resampling by hand: the model's decoder owns the joins. */
export class StreamingAudio {
  private pending: AudioBuffer[] = []
  private sources = new Set<AudioBufferSourceNode>()
  private decoding: Promise<void> = Promise.resolve()
  private playing = false
  private finished = false
  private settled = false
  private started = false
  private stalled = false
  private nextTime = 0
  private generatedSeconds = 0
  private chunkCount = 0
  private firstArrival: number | null = null
  private lastArrival: number | null = null
  private slowestIntervalRatio = 0
  private longestIntervalSeconds = 0
  private onBuffering?: (buffering: boolean) => void
  private buffering = false
  private resolve!: () => void
  private reject!: (error: Error) => void
  private readonly completion = new Promise<void>((resolve, reject) => {
    this.resolve = resolve
    this.reject = reject
  })

  constructor(
    private readonly context: AudioContext,
    private readonly generationStartedAt = performance.now(),
    private readonly now: () => number = () => performance.now()
  ) {
    // A decode can fail while this passage is still queued behind another one.
    // Keep its rejection for play(), without an unhandled promise in the meantime.
    void this.completion.catch(() => {})
  }

  /** End-to-end seconds spent generating/delivering each second of audio.
   * Includes request startup and browser decoding, not just model execution. */
  get generationSecondsPerAudioSecond(): number | null {
    return this.lastArrival === null ? null
      : Math.max(0, this.lastArrival - this.generationStartedAt) / 1000 / this.generatedSeconds
  }

  append(base64: string): Promise<void> {
    this.decoding = this.decoding.then(async () => {
      if (this.settled) return
      const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0))
      const buffer = await this.context.decodeAudioData(bytes.buffer)
      if (this.settled) return
      if (!Number.isFinite(buffer.duration) || buffer.duration <= 0)
        throw new Error('empty audio chunk')
      const arrival = this.now()
      if (this.lastArrival !== null) {
        const interval = Math.max(0, arrival - this.lastArrival) / 1000
        this.slowestIntervalRatio = Math.max(this.slowestIntervalRatio, interval / buffer.duration)
        this.longestIntervalSeconds = Math.max(this.longestIntervalSeconds, interval)
      }
      this.firstArrival ??= arrival
      this.lastArrival = arrival
      this.generatedSeconds += buffer.duration
      this.chunkCount++
      this.pending.push(buffer)
      this.pump()
    }).catch(() => this.cancel(new Error('audio-stream-decode-failed')))
    return this.decoding
  }

  finish(): void {
    void this.decoding.then(() => {
      this.finished = true
      this.pump()
    }).catch(() => this.cancel(new Error('audio-stream-playback-failed')))
  }

  async play(onBuffering?: (buffering: boolean) => void): Promise<void> {
    if (!this.settled) {
      this.onBuffering = onBuffering
      this.setBuffering(true)
      this.playing = true
      try {
        await this.context.resume()
        this.pump()
      } catch {
        this.cancel(new Error('audio-stream-playback-failed'))
      }
    }
    return this.completion
  }

  cancel(error: Error): void {
    if (this.settled) return
    this.settled = true
    this.pending = []
    for (const source of this.sources) {
      source.onended = null
      source.stop()
      source.disconnect()
    }
    this.sources.clear()
    this.reject(error)
  }

  private setBuffering(value: boolean): void {
    if (this.buffering === value) return
    this.buffering = value
    this.onBuffering?.(value)
  }

  private canStartEarly(): boolean {
    // A burst of notifications is not evidence of sustained generation speed.
    // Observe several chunks over at least a second before trusting the rate.
    if (this.chunkCount < 7 || this.firstArrival === null || this.lastArrival === null ||
        this.lastArrival - this.firstArrival < 1000) return false
    const elapsed = Math.max(0, this.now() - this.generationStartedAt) / 1000
    const ratio = Math.max(elapsed / this.generatedSeconds, this.slowestIntervalRatio)
    // Require capacity for a 25% slowdown. If the producer cannot comfortably
    // stay ahead, the only reliable duration estimate is the completed passage.
    // Keep incremental decoding for its memory savings while waiting for that.
    const reserve = Math.max(2, this.longestIntervalSeconds * 3)
    return ratio * 1.25 < 1 &&
      this.pending.reduce((seconds, b) => seconds + b.duration, 0) >= reserve
  }

  private pump(): void {
    if (!this.playing || this.settled) return
    if (this.started && !this.finished && this.nextTime <= this.context.currentTime) {
      // A later slowdown invalidates our prediction. Buffer the remainder once
      // instead of repeatedly restarting with 320 ms fragments. Keep every sample.
      this.stalled = true
      this.setBuffering(true)
    }
    if (!this.finished && (this.stalled || (!this.started && !this.canStartEarly()))) return
    this.started = true
    this.setBuffering(false)
    for (const buffer of this.pending.splice(0)) {
      const source = this.context.createBufferSource()
      source.buffer = buffer
      source.connect(this.context.destination)
      source.onended = () => {
        source.disconnect()
        this.sources.delete(source)
        this.pump()
      }
      this.sources.add(source)
      const at = this.nextTime > this.context.currentTime
        ? this.nextTime : this.context.currentTime + 0.02
      source.start(at)
      this.nextTime = at + buffer.duration
    }
    if (this.finished && this.sources.size === 0 && this.pending.length === 0) {
      this.settled = true
      this.resolve()
    }
  }
}
