import XCTest
final class AuditUITests: XCTestCase {
    func testUnsavedMarkerAtNativeEvaluatorFailure() throws {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        let editor = app.textViews.firstMatch
        XCTAssertTrue(editor.waitForExistence(timeout: 10))
        XCTAssertFalse((editor.value as? String ?? "").contains("M04EVAL26A"))
        editor.tap()
        print("AUDIT_M04_ARM_NOW")
        Thread.sleep(forTimeInterval: 2)
        editor.typeText("M04EVAL26A")
        let alert = app.alerts["Editing paused"]
        XCTAssertTrue(alert.waitForExistence(timeout: 12))
        XCTAssertTrue(alert.buttons["Reload"].exists)
        XCTAssertTrue(alert.buttons["Later"].exists)
        print("AUDIT_M04_NATIVE_ALERT_READY")
    }
}
