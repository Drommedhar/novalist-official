import XCTest
final class AuditUITests: XCTestCase {
    func testMicrophoneGrantControl() throws {
        continueAfterFailure = false
        let settings = XCUIApplication(bundleIdentifier: "com.apple.Preferences")
        settings.activate()
        let permission = settings.switches["Novalist"]
        XCTAssertTrue(permission.waitForExistence(timeout: 10))
        XCTAssertEqual(permission.value as? String, "0")
        permission.switches.firstMatch.tap()
        expectation(for: NSPredicate(format: "value == %@", "1"), evaluatedWith: permission)
        waitForExpectations(timeout: 5)
        print("AUDIT_MICROPHONE_NATIVE_SETTINGS_GRANT_PASS")
    }
}
