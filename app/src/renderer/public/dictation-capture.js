class DictationCapture extends globalThis.AudioWorkletProcessor {
  constructor() {
    super()
    this.frame = new Float32Array(2048)
    this.at = 0
    this.stopped = false
    this.port.onmessage = () => {
      this.stopped = true
      if (this.at) this.port.postMessage(this.frame.slice(0, this.at))
      this.port.postMessage('stopped')
    }
  }
  process(inputs) {
    if (this.stopped) return false
    const channel = inputs[0]?.[0]
    if (channel) for (const sample of channel) {
      this.frame[this.at++] = sample
      if (this.at === this.frame.length) {
        this.port.postMessage(this.frame, [this.frame.buffer])
        this.frame = new Float32Array(2048)
        this.at = 0
      }
    }
    return true
  }
}
globalThis.registerProcessor('novalist-dictation', DictationCapture)
