import XCTest
final class AuditUITests: XCTestCase {
    func testNativeReload() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        let alert = app.alerts["Editing paused"]
        XCTAssertTrue(alert.waitForExistence(timeout: 10))
        XCTAssertTrue(alert.buttons["Later"].exists)
        XCTAssertTrue(alert.buttons["Reload"].isHittable)
        alert.buttons["Reload"].tap()
        XCTAssertTrue(alert.waitForNonExistence(timeout: 10))
        XCTAssertTrue(app.webViews.firstMatch.waitForExistence(timeout: 15))
        print("AUDIT_NATIVE_RELOAD_BUTTON_PASS")
    }
}
