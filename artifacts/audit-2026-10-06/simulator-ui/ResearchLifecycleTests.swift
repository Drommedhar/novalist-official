import XCTest
final class AuditUITests: XCTestCase {
    func testResearchBackgroundTerminateRelaunch() throws {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        let title = app.textFields["Title"]
        XCTAssertTrue(title.waitForExistence(timeout: 10))
        title.tap()
        title.typeText("Audit Research Alpha")
        let content = app.textViews["Content"]
        content.tap()
        let marker = "AUDIT_RESEARCH_HOME_20261007"
        content.typeText(marker)
        XCUIDevice.shared.press(.home)
        app.terminate()
        app.launch()
        let book = app.switches["Select Audit Tablet Book — Audit Tablet Fixture"]
        XCTAssertTrue(book.waitForExistence(timeout: 20))
        book.doubleTap()
        let researchNav = app.scrollViews.staticTexts["Research"].firstMatch
        if !researchNav.waitForExistence(timeout: 3) { app.switches["Toggle sidebar"].tap() }
        XCTAssertTrue(researchNav.waitForExistence(timeout: 10))
        researchNav.tap()
        let note = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Audit Research Alpha")).firstMatch
        XCTAssertTrue(note.waitForExistence(timeout: 10))
        note.tap()
        XCTAssertTrue(content.waitForExistence(timeout: 10))
        XCTAssertTrue((content.value as? String ?? "").contains(marker))
        print("AUDIT_RESEARCH_BACKGROUND_TERMINATE_RELAUNCH_PASS")
        let image = XCTAttachment(screenshot: app.screenshot())
        image.name = "research-after-relaunch"
        image.lifetime = .keepAlways
        add(image)
    }
}
