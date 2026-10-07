import XCTest
final class AuditUITests: XCTestCase {
    let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
    func create(_ title: String) {
        app.buttons["Scene"].firstMatch.tap()
        app.textFields["Enter scene title..."].typeText(title)
        if app.buttons["Hide keyboard"].exists { app.buttons["Hide keyboard"].tap() }
        app.buttons["OK"].tap()
        XCTAssertTrue(app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@",title)).firstMatch.waitForExistence(timeout:10))
    }
    func testPrepareTwoIndependentPanes() {
        continueAfterFailure = false
        app.activate(); XCUIDevice.shared.orientation = .landscapeLeft
        let book = app.switches["Select Audit Tablet Book — Audit Tablet Fixture"]
        XCTAssertTrue(book.waitForExistence(timeout:20));book.doubleTap()
        let editorNav=app.scrollViews.staticTexts["Editor"].firstMatch
        if !editorNav.exists {app.switches["Toggle sidebar"].tap()}
        XCTAssertTrue(editorNav.waitForExistence(timeout:10));editorNav.tap()
        if !app.buttons["Scene"].firstMatch.exists {app.switches["Toggle binder"].tap()}
        create("Audit M01 Pane A"); create("Audit M01 Pane B")
        let first=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@","Audit M01 Pane A")).firstMatch
        let second=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@","Audit M01 Pane B")).firstMatch
        first.tap();second.press(forDuration:1.2)
        XCTAssertTrue(app.buttons["Open in split"].waitForExistence(timeout:5))
        app.buttons["Open in split"].tap()
        XCTAssertEqual(app.textViews.count,2)
        print("M01_TWO_NATIVE_EDITOR_PANES_READY")
        print(app.debugDescription)
    }
}
