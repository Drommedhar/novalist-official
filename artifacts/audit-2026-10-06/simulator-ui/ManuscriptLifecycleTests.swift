import XCTest
final class AuditUITests: XCTestCase {
    func testManuscriptBackgroundTerminateRelaunch() throws {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        XCTAssertTrue(app.buttons["Corkboard"].waitForExistence(timeout: 10))
        let editor = app.textViews.firstMatch
        XCTAssertTrue(editor.waitForExistence(timeout: 10))
        editor.tap()
        let marker = "AUDIT_MANUSCRIPT_HOME_20261007"
        editor.typeText(marker)
        XCUIDevice.shared.press(.home)
        app.terminate()
        app.launch()
        let book = app.switches["Select Audit Tablet Book — Audit Tablet Fixture"]
        XCTAssertTrue(book.waitForExistence(timeout: 20))
        book.doubleTap()
        let nav = app.scrollViews.staticTexts["Manuscript"].firstMatch
        if !nav.waitForExistence(timeout: 3) { app.switches["Toggle sidebar"].tap() }
        XCTAssertTrue(nav.waitForExistence(timeout: 10))
        nav.tap()
        XCTAssertTrue(app.buttons["Corkboard"].waitForExistence(timeout: 10))
        XCTAssertTrue(editor.waitForExistence(timeout: 10))
        XCTAssertTrue((editor.value as? String ?? "").contains(marker))
        print("AUDIT_MANUSCRIPT_BACKGROUND_TERMINATE_RELAUNCH_PASS")
        let image = XCTAttachment(screenshot: app.screenshot())
        image.name = "manuscript-after-relaunch"
        image.lifetime = .keepAlways
        add(image)
    }
}
