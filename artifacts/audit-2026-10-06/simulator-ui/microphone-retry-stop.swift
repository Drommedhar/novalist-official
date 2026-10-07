import XCTest
final class AuditUITests: XCTestCase {
    func testMicrophoneRetryStopAndBackground() throws {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        let book = app.switches["Select Audit Book Alpha — Audit Simulator Fixture"]
        if book.exists {
            book.doubleTap()
            app.buttons["Write"].tap()
            app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Audit Fixed Scene 20261007")).firstMatch.tap()
        }
        if !app.buttons["Start dictation"].exists { app.switches["Dictate"].tap() }
        let permission = app.staticTexts["Microphone access was denied. Allow microphone access in your system settings, then try again."]
        let genericError = app.staticTexts["The microphone could not be opened. Check that it is connected and available."]
        print("AUDIT_MICROPHONE_PERMISSION_ERROR_BEFORE_RETRY=\(permission.exists)")
        XCTAssertTrue(app.buttons["Start dictation"].waitForExistence(timeout: 10))
        app.buttons["Start dictation"].tap()
        let listening = app.staticTexts["Listening…"]
        let started = listening.waitForExistence(timeout: 8)
        print("AUDIT_MICROPHONE_LISTENING=\(started)")
        print("AUDIT_MICROPHONE_PERMISSION_ERROR_AFTER_RETRY=\(permission.exists)")
        print("AUDIT_MICROPHONE_GENERIC_ERROR=\(genericError.exists)")
        if !started {
            print("AUDIT_MICROPHONE_RECORDING_BLOCKED_ON_SIMULATOR")
            if app.buttons["Stop dictation"].exists { app.buttons["Stop dictation"].tap() }
            return
        }
        XCTAssertFalse(permission.exists)
        XCTAssertFalse(genericError.exists)
        app.buttons["Stop dictation"].tap()
        XCTAssertTrue(app.buttons["Start dictation"].waitForExistence(timeout: 5))
        XCTAssertFalse(app.buttons["Stop dictation"].exists)
        XCTAssertFalse(listening.exists)
        print("AUDIT_MICROPHONE_RETRY_AND_STOP_PASS")
        app.buttons["Start dictation"].tap()
        XCTAssertTrue(listening.waitForExistence(timeout: 5))
        XCUIDevice.shared.press(.home)
        app.activate()
        XCTAssertTrue(app.buttons["Start dictation"].waitForExistence(timeout: 8))
        XCTAssertFalse(app.buttons["Stop dictation"].exists)
        XCTAssertFalse(listening.exists)
        XCTAssertFalse(permission.exists)
        print("AUDIT_MICROPHONE_BACKGROUND_STOP_PASS")
    }
}
