import XCTest
final class AuditUITests: XCTestCase {
    func testUnsavedMarkerSurvivesActualNativeReload() throws {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        let alert = app.alerts["Editing paused"]
        XCTAssertTrue(alert.waitForExistence(timeout: 10))
        print("AUDIT_M04_BEFORE_ACTUAL_RELOAD")
        Thread.sleep(forTimeInterval: 2)
        alert.buttons["Reload"].tap()
        let book = app.switches["Select Audit Book Alpha — Audit Simulator Fixture"]
        XCTAssertTrue(book.waitForExistence(timeout: 20))
        book.doubleTap()
        XCTAssertTrue(app.buttons["Write"].waitForExistence(timeout: 15))
        app.buttons["Write"].tap()
        let scene = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Audit M04 Evaluator A")).firstMatch
        XCTAssertTrue(scene.waitForExistence(timeout: 10))
        scene.tap()
        let editor = app.textViews.firstMatch
        XCTAssertTrue(editor.waitForExistence(timeout: 10))
        let retained = (editor.value as? String ?? "").contains("M04EVAL26A")
        print("AUDIT_M04_MARKER_AFTER_ACTUAL_RELOAD=\(retained)")
        XCTAssertTrue(retained, "Actual unsaved UI marker must survive native Reload")
    }
}
