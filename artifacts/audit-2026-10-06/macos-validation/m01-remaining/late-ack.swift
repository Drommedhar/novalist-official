import XCTest
final class AuditUITests: XCTestCase {
    let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
    let title = "Audit M01 late acknowledgement A"
    let oldMarker = "M01OLDA26"
    let newMarker = "M01NEWB26"
    func openBook() {
        let book = app.switches["Select Audit Book Alpha — Audit Simulator Fixture"]
        XCTAssertTrue(book.waitForExistence(timeout: 20)); book.doubleTap()
        XCTAssertTrue(app.buttons["Write"].waitForExistence(timeout: 15)); app.buttons["Write"].tap()
    }
    func testLateAcknowledgementAndInterruptedFlush() {
        continueAfterFailure = false
        app.activate(); XCUIDevice.shared.orientation = .portrait; openBook()
        app.buttons["Scene"].firstMatch.tap()
        app.textFields["Enter scene title..."].typeText(title)
        if app.buttons["selected"].exists && app.buttons["selected"].isHittable { app.buttons["selected"].tap() }
        app.buttons["OK"].tap()
        XCTAssertTrue(app.buttons[title].waitForExistence(timeout: 10)); app.buttons[title].tap()
        let editor = app.textViews.firstMatch
        XCTAssertTrue(editor.waitForExistence(timeout: 10)); editor.tap()
        XCTAssertFalse((editor.value as? String ?? "").contains(oldMarker))
        print("M01_READY_GATE"); Thread.sleep(forTimeInterval: 3)
        editor.typeText(oldMarker)
        print("M01_OLD_TYPED"); Thread.sleep(forTimeInterval: 5)
        editor.typeText(newMarker)
        print("M01_NEW_TYPED"); Thread.sleep(forTimeInterval: 5)
        XCTAssertTrue((editor.value as? String ?? "").contains(oldMarker + newMarker))
        print("M01_BEFORE_HOME"); XCUIDevice.shared.press(.home)
        Thread.sleep(forTimeInterval: 1.5)
        app.terminate(); print("M01_TERMINATED")
        app.launch(); openBook()
        let scene = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", title)).firstMatch
        XCTAssertTrue(scene.waitForExistence(timeout: 10)); scene.tap()
        XCTAssertTrue(editor.waitForExistence(timeout: 10))
        XCTAssertTrue((editor.value as? String ?? "").contains(oldMarker + newMarker))
        print("M01_REOPENED_NEW_BUFFER_PASS")
    }
}
