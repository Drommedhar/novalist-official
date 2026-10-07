import XCTest
final class AuditUITests: XCTestCase {
    let sceneTitle = "Audit M04 BACKENDA"
    let marker = "M04BACKENDA26"
    let task = "Audit M04 task BACKENDA"
    func openBook(_ app: XCUIApplication) {
        let book = app.switches["Select Audit Book Alpha — Audit Simulator Fixture"]
        XCTAssertTrue(book.waitForExistence(timeout: 20))
        book.doubleTap()
        XCTAssertTrue(app.buttons["Write"].waitForExistence(timeout: 15))
        app.buttons["Write"].tap()
    }
    func testActualUnsavedRecoveryAndMutationNonreplay() throws {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        openBook(app)
        app.buttons["Scene"].firstMatch.tap()
        app.textFields["Enter scene title..."].typeText(sceneTitle)
        if app.buttons["selected"].exists && app.buttons["selected"].isHittable { app.buttons["selected"].tap() }
        app.buttons["OK"].tap()
        XCTAssertTrue(app.buttons[sceneTitle].waitForExistence(timeout: 10))
        app.buttons[sceneTitle].tap()
        XCTAssertTrue(app.buttons["Inspector"].waitForExistence(timeout: 10))
        app.buttons["Inspector"].tap()
        app.buttons["To do"].tap()
        let taskInput = app.textFields["What needs doing"]
        XCTAssertTrue(taskInput.waitForExistence(timeout: 10))
        taskInput.tap()
        taskInput.typeText(task + "\n")
        print("AUDIT_M04_TASK_SUBMITTED")
        if app.buttons["selected"].exists && app.buttons["selected"].isHittable { app.buttons["selected"].tap() }
        app.buttons["Close"].tap()
        XCUIDevice.shared.orientation = .landscapeLeft
        let manuscript = app.scrollViews.staticTexts["Manuscript"].firstMatch
        if !manuscript.exists { app.switches["Toggle sidebar"].tap() }
        XCTAssertTrue(manuscript.waitForExistence(timeout: 10))
        manuscript.tap()
        XCTAssertTrue(app.buttons["Corkboard"].waitForExistence(timeout: 10))
        let manuscriptEditor = app.textViews.element(boundBy: app.textViews.count - 1)
        for _ in 0..<6 { if manuscriptEditor.isHittable { break }; app.webViews.firstMatch.swipeUp() }
        let editor = manuscriptEditor
        XCTAssertTrue(editor.waitForExistence(timeout: 10))
        XCTAssertFalse((editor.value as? String ?? "").contains(marker))
        editor.tap()
        print("AUDIT_M04_ARM_NOW")
        Thread.sleep(forTimeInterval: 2)
        editor.typeText(marker)
        print("AUDIT_M04_TYPED")
        let alert = app.alerts["Editing paused"]
        XCTAssertTrue(alert.waitForExistence(timeout: 25))
        XCTAssertTrue(alert.buttons["Reload"].exists)
        XCTAssertTrue(alert.buttons["Later"].exists)
        print("AUDIT_M04_BEFORE_ACTUAL_RELOAD")
        Thread.sleep(forTimeInterval: 3)
        alert.buttons["Reload"].tap()
        XCUIDevice.shared.orientation = .portrait
        openBook(app)
        let scene = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", sceneTitle)).firstMatch
        if scene.exists { scene.tap() }
        let restoredEditor = app.textViews.firstMatch
        XCTAssertTrue(restoredEditor.waitForExistence(timeout: 10))
        let retained = (restoredEditor.value as? String ?? "").contains(marker)
        print("AUDIT_M04_MARKER_AFTER_ACTUAL_RELOAD=\(retained)")
        XCTAssertTrue(retained, "Actual unsaved UI marker must survive native Reload")
        app.buttons["Inspector"].tap()
        app.buttons["To do"].tap()
        let taskRow = app.switches[task]
        XCTAssertTrue(taskRow.waitForExistence(timeout: 10))
        XCTAssertEqual(app.switches.matching(NSPredicate(format: "label == %@", task)).count, 1)
        print("AUDIT_M04_SINGLE_TASK_AFTER_RELOAD_PASS")
        app.buttons["Close"].tap()
    }
}
