import XCTest
final class AuditUITests: XCTestCase {
    let bookLabel = "Select Audit Tablet Book — Audit Tablet Fixture"
    let noteTitle = "Audit Fixed Research 20261007"
    let marker = "AUDIT_FIXED_RESEARCH_20261007_B"
    func openResearch(_ app: XCUIApplication) {
        let book = app.switches[bookLabel]
        XCTAssertTrue(book.waitForExistence(timeout: 20))
        book.doubleTap()
        let nav = app.scrollViews.staticTexts["Research"].firstMatch
        if !nav.waitForExistence(timeout: 2) { app.switches["Toggle sidebar"].tap() }
        XCTAssertTrue(nav.waitForExistence(timeout: 10))
        nav.tap()
    }
    func testFixedNativeEvaluatorResearchLifecycle() throws {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        openResearch(app)
        XCTAssertTrue(app.buttons["+ Note"].waitForExistence(timeout: 10))
        app.buttons["+ Note"].tap()
        let title = app.textFields["Title"]
        XCTAssertTrue(title.waitForExistence(timeout: 10))
        title.tap()
        title.typeText(noteTitle)
        let content = app.textViews["Content"]
        XCTAssertFalse((content.value as? String ?? "").contains(marker))
        content.tap()
        content.typeText(marker)
        XCUIDevice.shared.press(.home)
        app.terminate()
        app.launch()
        openResearch(app)
        let note = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", noteTitle)).firstMatch
        XCTAssertTrue(note.waitForExistence(timeout: 10))
        note.tap()
        XCTAssertTrue(content.waitForExistence(timeout: 10))
        XCTAssertTrue((content.value as? String ?? "").contains(marker))
        print("AUDIT_FIXED_RESEARCH_LIFECYCLE_PASS")
        let image = XCTAttachment(screenshot: app.screenshot())
        image.name = "fixed-research-after-relaunch"
        image.lifetime = .keepAlways
        add(image)
    }
}
