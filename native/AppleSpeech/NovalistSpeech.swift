import Foundation
import Speech
import AVFoundation

// Bundled native code only. Speech assets are installed and maintained by the OS.
private enum SpeechFailure: Error { case unsupported, languageNotPrepared, invalidRequest }
private let jobsLock = NSLock()
private var jobs: [Int64: Task<Void, Never>] = [:]

private func finish(_ id: Int64) {
    jobsLock.lock()
    jobs.removeValue(forKey: id)
    jobsLock.unlock()
}

@_cdecl("nl_speech_request")
public func speechRequest(_ id: Int64, _ input: UnsafePointer<CChar>,
                          _ reply: @escaping @convention(c) (Int64, UnsafePointer<CChar>) -> Void) {
    let json = String(cString: input)
    jobsLock.lock()
    jobs[id] = Task {
        defer { finish(id) }
        let response: [String: Any]
        do {
            let request = try JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any]
            guard let request else { throw SpeechFailure.invalidRequest }
            response = ["result": try await perform(request)]
        } catch {
            // Never return native error descriptions: they can contain input.
            response = ["error": Task.isCancelled ? "cancelled" : "systemSpeechFailed"]
        }
        let data = (try? JSONSerialization.data(withJSONObject: response)) ?? Data("{}".utf8)
        String(decoding: data, as: UTF8.self).withCString { reply(id, $0) }
    }
    jobsLock.unlock()
}

@_cdecl("nl_speech_cancel")
public func speechCancel(_ id: Int64) {
    jobsLock.lock()
    jobs[id]?.cancel()
    jobsLock.unlock()
}

private struct Selection {
    let locale: Locale
    let modern: Bool
    let installed: Bool
}

private func select(_ language: String) async -> Selection? {
    let requested = Locale(identifier: language == "de" ? "de-DE" : "en-US")
    if SpeechTranscriber.isAvailable,
       let locale = await SpeechTranscriber.supportedLocale(equivalentTo: requested) {
        let installed = await SpeechTranscriber.installedLocales.contains { $0.identifier == locale.identifier }
        return Selection(locale: locale, modern: true, installed: installed)
    }
    if let locale = await DictationTranscriber.supportedLocale(equivalentTo: requested) {
        let installed = await DictationTranscriber.installedLocales.contains { $0.identifier == locale.identifier }
        return Selection(locale: locale, modern: false, installed: installed)
    }
    return nil
}

private func perform(_ request: [String: Any]) async throws -> Any {
    try Task.checkCancellation()
    #if os(iOS)
    switch request["operation"] as? String {
    case "microphoneStart": return try await MicrophoneAccess.start()
    case "microphoneRead": return await MicrophoneAccess.read()
    case "microphoneStop": return await MicrophoneAccess.stop()
    default: break
    }
    #endif
    if request["operation"] as? String == "status" {
        var languages: [[String: Any]] = []
        for language in ["en", "de"] {
            let choice = await select(language)
            languages.append(["language": language, "supported": choice != nil,
                              "installed": choice?.installed ?? false])
        }
        return ["engine": "apple", "available": languages.contains { $0["supported"] as? Bool == true },
                "online": false, "usesSystemPanel": false, "languages": languages]
    }
    guard let language = request["language"] as? String, ["en", "de"].contains(language),
          let choice = await select(language) else { throw SpeechFailure.unsupported }
    let modern = choice.modern ? SpeechTranscriber(locale: choice.locale, preset: .transcription) : nil
    let legacy = choice.modern ? nil : DictationTranscriber(locale: choice.locale, preset: .longDictation)
    let module: any SpeechModule
    if let modern { module = modern } else { module = legacy! }
    if request["operation"] as? String == "prepare" {
        try await AssetInventory.reserve(locale: choice.locale)
        if let installation = try await AssetInventory.assetInstallationRequest(supporting: [module]) {
            try await installation.downloadAndInstall()
        }
        try Task.checkCancellation()
        return true
    }
    guard request["operation"] as? String == "transcribe", choice.installed,
          let base64 = request["audio"] as? String, let audio = Data(base64Encoded: base64),
          !audio.isEmpty, audio.count <= 8 * 1024 * 1024 else { throw SpeechFailure.languageNotPrepared }
    let file = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".wav")
    defer { try? FileManager.default.removeItem(at: file) }
    try audio.write(to: file, options: .atomic)
    let audioFile = try AVAudioFile(forReading: file)
    let analyzer = SpeechAnalyzer(modules: [module])
    return try await withTaskCancellationHandler {
        // Consume results while the analyzer works so long clips cannot fill
        // a result buffer. Only final results reach the editor, exactly once.
        let results = Task<String, Error> {
            var pieces: [String] = []
            if let modern {
                for try await result in modern.results {
                    if result.isFinal { pieces.append(String(result.text.characters)) }
                }
            } else if let legacy {
                for try await result in legacy.results {
                    if result.isFinal { pieces.append(String(result.text.characters)) }
                }
            }
            return pieces.joined(separator: " ").trimmingCharacters(in: .whitespacesAndNewlines)
        }
        do {
            _ = try await analyzer.analyzeSequence(from: audioFile)
            try await analyzer.finalizeAndFinishThroughEndOfInput()
            let text = try await results.value
            try Task.checkCancellation()
            return text
        } catch {
            results.cancel()
            await analyzer.cancelAndFinishNow()
            throw error
        }
    } onCancel: {
        Task { await analyzer.cancelAndFinishNow() }
    }
}
