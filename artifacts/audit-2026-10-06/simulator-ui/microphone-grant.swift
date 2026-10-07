import XCTest
final class AuditUITests: XCTestCase {
    func testMicrophoneGrantInNativeSettings() throws {
        continueAfterFailure = false
        let settings = XCUIApplication(bundleIdentifier: "com.apple.Preferences")
        settings.activate()
        let microphone = settings.buttons["MICROPHONE"]
        for _ in 0..<5 { if microphone.exists && microphone.isHittable { break }; settings.swipeDown() }
        XCTAssertTrue(microphone.exists)
        microphone.tap()
        print("AUDIT_MICROPHONE_SETTINGS_BEGIN")
        print(settings.debugDescription)
        print("AUDIT_MICROPHONE_SETTINGS_END")
        let permission = settings.switches["Novalist"]
        XCTAssertTrue(permission.waitForExistence(timeout: 10))
        XCTAssertEqual(permission.value as? String, "0")
        permission.tap()
        XCTAssertEqual(permission.value as? String, "1")
        print("AUDIT_MICROPHONE_NATIVE_SETTINGS_GRANT_PASS")
    }
}
