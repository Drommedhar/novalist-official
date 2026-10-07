import XCTest
final class AuditUITests: XCTestCase {
    func testCreateM04EvaluatorFixture() throws {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        let book = app.switches["Select Audit Book Alpha — Audit Simulator Fixture"]
        XCTAssertTrue(book.waitForExistence(timeout: 20))
        book.doubleTap()
        XCTAssertTrue(app.buttons["Write"].waitForExistence(timeout: 15))
        app.buttons["Write"].tap()
        app.buttons["Scene"].firstMatch.tap()
        app.textFields["Enter scene title..."].typeText("Audit M04 Evaluator A")
        if app.buttons["selected"].exists && app.buttons["selected"].isHittable { app.buttons["selected"].tap() }
        app.buttons["OK"].tap()
        XCTAssertTrue(app.buttons["Audit M04 Evaluator A"].waitForExistence(timeout: 10))
        app.buttons["Audit M04 Evaluator A"].tap()
        XCTAssertTrue(app.textViews.firstMatch.waitForExistence(timeout: 10))
        print("AUDIT_M04_NEW_EMPTY_SCENE_READY")
    }
}
