import XCTest
final class AuditUITests: XCTestCase {
    func testMicrophoneNativePromptDenial() throws {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        let springboard = XCUIApplication(bundleIdentifier: "com.apple.springboard")
        app.activate()
        XCTAssertTrue(app.buttons["Start dictation"].waitForExistence(timeout: 10))
        XCTAssertTrue(app.buttons["Start dictation"].isEnabled)
        app.buttons["Start dictation"].tap()
        let alert = springboard.alerts.firstMatch
        XCTAssertTrue(alert.waitForExistence(timeout: 10))
        print("AUDIT_MICROPHONE_NATIVE_ALERT_BEGIN")
        print(alert.debugDescription)
        print("AUDIT_MICROPHONE_NATIVE_ALERT_END")
        let deny = alert.buttons.matching(NSPredicate(format: "label IN %@", ["Don’t Allow", "Don't Allow", "Nicht erlauben"])).firstMatch
        XCTAssertTrue(deny.exists)
        deny.tap()
        let error = app.staticTexts["Microphone access was denied. Allow microphone access in your system settings, then try again."]
        XCTAssertTrue(error.waitForExistence(timeout: 10))
        XCTAssertTrue(app.buttons["Start dictation"].exists)
        XCTAssertFalse(app.buttons["Stop dictation"].exists)
        print("AUDIT_MICROPHONE_DENIAL_UI_PASS")
        let image = XCTAttachment(screenshot: app.screenshot())
        image.name = "microphone-permission-denied"
        image.lifetime = .keepAlways
        add(image)
    }
}
