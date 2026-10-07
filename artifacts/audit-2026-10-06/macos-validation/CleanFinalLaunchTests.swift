import XCTest
final class AuditUITests: XCTestCase {
    func testCleanFinalLaunch() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        XCTAssertTrue(app.webViews.firstMatch.waitForExistence(timeout: 20))
        XCTAssertTrue(app.buttons["New Project"].waitForExistence(timeout: 20))
        XCTAssertTrue(app.buttons["Browse for Project Folder..."].isHittable)
        XCTAssertFalse(app.alerts["Editing paused"].exists)
        print("AUDIT_CLEAN_FINAL_LAUNCH_PASS")
    }
}
