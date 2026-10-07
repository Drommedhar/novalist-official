import XCTest
final class AuditUITests: XCTestCase {
    func testMicrophoneSettingsSearch() throws {
        continueAfterFailure = false
        let settings = XCUIApplication(bundleIdentifier: "com.apple.Preferences")
        settings.activate()
        let search = settings.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 10))
        search.tap()
        search.typeText("Novalist")
        XCTAssertTrue(settings.staticTexts["Novalist"].firstMatch.waitForExistence(timeout: 10))
        settings.staticTexts["Novalist"].firstMatch.tap()
        print("AUDIT_NOVALIST_SETTINGS_BEGIN")
        print(settings.debugDescription)
        print("AUDIT_NOVALIST_SETTINGS_END")
    }
}
