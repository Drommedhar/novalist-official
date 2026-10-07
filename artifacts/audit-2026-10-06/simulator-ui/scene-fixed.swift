import XCTest
final class AuditUITests: XCTestCase {
    let bookLabel = "Select Audit Book Alpha — Audit Simulator Fixture"
    let sceneTitle = "Audit Fixed Scene 20261007"
    let marker = "AUDIT_FIXED_SCENE_20261007_A"
    func openBook(_ app: XCUIApplication) {
        let book = app.switches[bookLabel]
        XCTAssertTrue(book.waitForExistence(timeout: 20))
        book.doubleTap()
        XCTAssertTrue(app.buttons["Write"].waitForExistence(timeout: 15))
        if app.buttons["Close tour"].exists { app.buttons["Close tour"].tap() }
        app.buttons["Write"].tap()
    }
    func testFixedNativeEvaluatorSceneLifecycle() throws {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        openBook(app)
        XCTAssertTrue(app.buttons["Scene"].firstMatch.waitForExistence(timeout: 10))
        app.buttons["Scene"].firstMatch.tap()
        app.textFields["Enter scene title..."].typeText(sceneTitle)
        if app.buttons["selected"].exists && app.buttons["selected"].isHittable { app.buttons["selected"].tap() }
        app.buttons["OK"].tap()
        XCTAssertTrue(app.buttons[sceneTitle].waitForExistence(timeout: 10))
        app.buttons[sceneTitle].tap()
        let editor = app.textViews.firstMatch
        XCTAssertTrue(editor.waitForExistence(timeout: 10))
        XCTAssertFalse((editor.value as? String ?? "").contains(marker))
        editor.tap()
        editor.typeText(marker)
        XCUIDevice.shared.press(.home)
        app.terminate()
        app.launch()
        openBook(app)
        let scene = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", sceneTitle)).firstMatch
        XCTAssertTrue(scene.waitForExistence(timeout: 10))
        scene.tap()
        XCTAssertTrue(editor.waitForExistence(timeout: 10))
        XCTAssertTrue((editor.value as? String ?? "").contains(marker))
        print("AUDIT_FIXED_SCENE_LIFECYCLE_PASS")
        let image = XCTAttachment(screenshot: app.screenshot())
        image.name = "fixed-scene-after-relaunch"
        image.lifetime = .keepAlways
        add(image)
    }
}
