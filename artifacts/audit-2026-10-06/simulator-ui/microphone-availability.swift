import XCTest
final class AuditUITests: XCTestCase {
    func testMicrophoneAvailabilityInspection() throws {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        XCTAssertTrue(app.switches["Dictate"].waitForExistence(timeout: 10))
        app.switches["Dictate"].tap()
        XCTAssertTrue(app.buttons["Start dictation"].waitForExistence(timeout: 15))
        print("AUDIT_DICTATION_PANEL_BEGIN")
        print(app.debugDescription)
        print("AUDIT_DICTATION_PANEL_END")
        print("AUDIT_DICTATION_START_ENABLED=\(app.buttons["Start dictation"].isEnabled)")
        if !app.buttons["Start dictation"].isEnabled {
            app.buttons["Dictation settings"].tap()
            print("AUDIT_DICTATION_SETTINGS_BEGIN")
            print(app.debugDescription)
            print("AUDIT_DICTATION_SETTINGS_END")
        }
    }
}
