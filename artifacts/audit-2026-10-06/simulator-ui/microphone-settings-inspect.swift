import XCTest
final class AuditUITests: XCTestCase {
    func testMicrophoneSettingsInspection() throws {
        continueAfterFailure = false
        let settings = XCUIApplication(bundleIdentifier: "com.apple.Preferences")
        settings.launch()
        print("AUDIT_IOS_SETTINGS_BEGIN")
        print(settings.debugDescription)
        print("AUDIT_IOS_SETTINGS_END")
    }
}
