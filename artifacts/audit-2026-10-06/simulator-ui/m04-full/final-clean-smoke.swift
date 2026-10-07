import XCTest
final class AuditUITests: XCTestCase {
    func testFinalCleanBuildRetainsRecoveredManuscript() throws {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        let book = app.switches["Select Audit Book Alpha — Audit Simulator Fixture"]
        XCTAssertTrue(book.waitForExistence(timeout: 20))
        book.doubleTap()
        XCTAssertTrue(app.buttons["Write"].waitForExistence(timeout: 15))
        app.buttons["Write"].tap()
        let scene = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Audit M04 WEBKITA")).firstMatch
        XCTAssertTrue(scene.waitForExistence(timeout: 10))
        scene.tap()
        let editor = app.textViews.firstMatch
        XCTAssertTrue(editor.waitForExistence(timeout: 10))
        XCTAssertTrue((editor.value as? String ?? "").contains("M04WEBKITA26"))
        XCTAssertFalse(app.alerts["Editing paused"].exists)
        print("AUDIT_M04_FINAL_CLEAN_UI_PASS")
    }
}
