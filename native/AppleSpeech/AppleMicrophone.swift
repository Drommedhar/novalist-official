#if os(iOS)
import Foundation
import AVFoundation
import UIKit

// MAUI serves its web view through app://, where browser microphone capture
// is unavailable. Capture natively and hand bounded WAV clips to the same
// editor queue used on desktop. No file or recording is retained on disk.
@MainActor
enum MicrophoneAccess {
    private static var current: SystemMicrophone?
    static func start() throws -> Bool {
        current?.halt()
        current = nil
        let microphone = try SystemMicrophone()
        do { try microphone.start() } catch { microphone.halt(); throw error }
        current = microphone
        return true
    }
    static func read() -> [String: Any] { current?.read() ?? ["clips": [], "ended": true] }
    static func stop() -> [String: Any] {
        current?.halt()
        let result = read()
        current = nil
        return result
    }
}

private final class SystemMicrophone {
    private let engine = AVAudioEngine()
    private let queue = DispatchQueue(label: "com.novalist.dictation.capture")
    private var frames: [Float] = []
    private var voiced = 0
    private var silence = 0
    private var clips: [String] = []
    private var ended = false
    private var tapped = false
    private var interruption: NSObjectProtocol?
    private var background: NSObjectProtocol?

    init() throws {
        let session = AVAudioSession.sharedInstance()
        guard session.recordPermission == .granted else { throw CaptureFailure.permission }
        try session.setCategory(.record, mode: .measurement)
        try session.setActive(true)
    }

    func start() throws {
        let input = engine.inputNode
        let format = input.outputFormat(forBus: 0)
        guard format.sampleRate > 0, format.channelCount > 0,
              let target = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 16000, channels: 1, interleaved: false),
              let converter = AVAudioConverter(from: format, to: target) else { throw CaptureFailure.format }
        input.installTap(onBus: 0, bufferSize: 1024, format: format) { [weak self] buffer, _ in
            guard let self else { return }
            let capacity = AVAudioFrameCount(ceil(Double(buffer.frameLength) * 16000 / format.sampleRate)) + 256
            guard let converted = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: capacity) else { return }
            var supplied = false
            var error: NSError?
            converter.convert(to: converted, error: &error) { _, status in
                if supplied { status.pointee = .noDataNow; return nil }
                supplied = true
                status.pointee = .haveData
                return buffer
            }
            guard error == nil, let channel = converted.floatChannelData?[0] else { return }
            let samples = Array(UnsafeBufferPointer(start: channel, count: Int(converted.frameLength)))
            self.queue.async { self.push(samples) }
        }
        tapped = true
        interruption = NotificationCenter.default.addObserver(forName: AVAudioSession.interruptionNotification,
            object: nil, queue: .main) { [weak self] _ in self?.halt() }
        background = NotificationCenter.default.addObserver(forName: UIApplication.didEnterBackgroundNotification,
            object: nil, queue: .main) { [weak self] _ in self?.halt() }
        do { try engine.start() } catch { halt(); throw error }
    }

    func halt() {
        engine.stop()
        if tapped { engine.inputNode.removeTap(onBus: 0); tapped = false }
        queue.sync {
            if !ended { flush() }
            ended = true
        }
        if let interruption { NotificationCenter.default.removeObserver(interruption); self.interruption = nil }
        if let background { NotificationCenter.default.removeObserver(background); self.background = nil }
        try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation)
    }

    func read() -> [String: Any] {
        queue.sync {
            let result: [String: Any] = ["clips": clips, "ended": ended]
            clips.removeAll(keepingCapacity: true)
            return result
        }
    }

    private func push(_ samples: [Float]) {
        guard !ended, !samples.isEmpty else { return }
        frames.append(contentsOf: samples)
        let rms = sqrt(samples.reduce(Float(0)) { $0 + $1 * $1 } / Float(samples.count))
        if rms > 0.008 { voiced += samples.count; silence = 0 } else { silence += samples.count }
        if voiced == 0, frames.count > 4800 { frames.removeFirst(frames.count - 4800) }
        if voiced > 0, (silence >= 12800 && frames.count >= 24000) || frames.count >= 400000 { flush() }
        if clips.count >= 12 {
            // The web view may be suspended. Stop before memory can grow with
            // an unattended recording; the next poll reports the interruption.
            ended = true
            DispatchQueue.main.async { [weak self] in self?.halt() }
        }
    }

    private func flush() {
        defer { frames.removeAll(keepingCapacity: true); voiced = 0; silence = 0 }
        guard voiced >= 1920 else { return }
        var wav = Data()
        func text(_ value: String) { wav.append(contentsOf: value.utf8) }
        func u16(_ value: UInt16) { wav.append(UInt8(value & 255)); wav.append(UInt8(value >> 8)) }
        func u32(_ value: UInt32) { u16(UInt16(value & 65535)); u16(UInt16(value >> 16)) }
        text("RIFF"); u32(UInt32(36 + frames.count * 2)); text("WAVEfmt "); u32(16)
        u16(1); u16(1); u32(16000); u32(32000); u16(2); u16(16)
        text("data"); u32(UInt32(frames.count * 2))
        for sample in frames {
            let value = max(-1, min(1, sample))
            u16(UInt16(bitPattern: Int16((value * (value < 0 ? 32768 : 32767)).rounded())))
        }
        clips.append(wav.base64EncodedString())
    }
    deinit {
        if let interruption { NotificationCenter.default.removeObserver(interruption) }
        if let background { NotificationCenter.default.removeObserver(background) }
    }
    private enum CaptureFailure: Error { case permission, format }
}
#endif
