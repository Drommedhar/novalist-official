import XCTest
final class AuditUITests: XCTestCase {
    func navigate(_ app: XCUIApplication, _ destination: String) {
        let nav = app.scrollViews.staticTexts[destination].firstMatch
        if !nav.waitForExistence(timeout: 1) { app.switches["Toggle sidebar"].tap() }
        XCTAssertTrue(nav.waitForExistence(timeout: 10))
        nav.tap()
    }
    func testActualContainerRelocation() throws {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        let book = app.switches["Select Audit Tablet Book — Audit Tablet Fixture"]
        XCTAssertTrue(book.waitForExistence(timeout: 20))
        book.doubleTap()
        navigate(app, "Research")
        let note = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Audit Research Alpha")).firstMatch
        XCTAssertTrue(note.waitForExistence(timeout: 10))
        note.tap()
        let content = app.textViews["Content"]
        XCTAssertTrue(content.waitForExistence(timeout: 10))
        XCTAssertTrue((content.value as? String ?? "").contains("AUDIT_RESEARCH_HOME_20261007"))
        navigate(app, "Manuscript")
        XCTAssertTrue(app.buttons["Corkboard"].waitForExistence(timeout: 10))
        let editor = app.textViews.firstMatch
        XCTAssertTrue(editor.waitForExistence(timeout: 10))
        XCTAssertTrue((editor.value as? String ?? "").contains("AUDIT_MANUSCRIPT_HOME_20261007"))
        print("AUDIT_ACTUAL_CONTAINER_RELOCATION_REOPEN_MARKERS_PASS")
        let image = XCTAttachment(screenshot: app.screenshot())
        image.name = "manuscript-after-container-relocation"
        image.lifetime = .keepAlways
        add(image)
    }
}
