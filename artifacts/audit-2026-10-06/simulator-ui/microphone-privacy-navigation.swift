import XCTest
final class AuditUITests: XCTestCase {
    func testMicrophonePrivacyNavigation() throws {
        continueAfterFailure = false
        let settings = XCUIApplication(bundleIdentifier: "com.apple.Preferences")
        settings.activate()
        settings.buttons["Schließen"].tap()
        let privacy = settings.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Datenschutz")).firstMatch
        for _ in 0..<6 { if privacy.exists && privacy.isHittable { break }; settings.swipeUp() }
        XCTAssertTrue(privacy.exists)
        privacy.tap()
        print("AUDIT_PRIVACY_SETTINGS_BEGIN")
        print(settings.debugDescription)
        print("AUDIT_PRIVACY_SETTINGS_END")
        let microphone = settings.buttons.matching(NSPredicate(format: "label == %@", "Mikrofon")).firstMatch
        for _ in 0..<4 { if microphone.exists && microphone.isHittable { break }; settings.swipeUp() }
        XCTAssertTrue(microphone.exists)
        microphone.tap()
        print("AUDIT_MICROPHONE_SETTINGS_BEGIN")
        print(settings.debugDescription)
        print("AUDIT_MICROPHONE_SETTINGS_END")
    }
}
