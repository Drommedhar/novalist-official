import XCTest
final class AuditUITests: XCTestCase {
    func testMicrophoneSettingsSearchResultInspection() throws {
        let settings = XCUIApplication(bundleIdentifier: "com.apple.Preferences")
        settings.activate()
        print("AUDIT_SETTINGS_SEARCH_RESULTS_BEGIN")
        print(settings.debugDescription)
        print("AUDIT_SETTINGS_SEARCH_RESULTS_END")
    }
}
