import XCTest
final class AuditUITests: XCTestCase {
    let sceneTitle = "Audit Fixed Manuscript Scene 20261007"
    let marker = "AUDIT_FIXED_MANUSCRIPT_20261007_C"
    func navigate(_ app: XCUIApplication, _ destination: String) {
        let nav = app.scrollViews.staticTexts[destination].firstMatch
        if !nav.waitForExistence(timeout: 1) { app.switches["Toggle sidebar"].tap() }
        XCTAssertTrue(nav.waitForExistence(timeout: 10))
        nav.tap()
    }
    func testFixedNativeEvaluatorManuscriptLifecycle() throws {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        navigate(app, "Editor")
        if !app.buttons["Scene"].firstMatch.waitForExistence(timeout: 1) { app.switches["Toggle binder"].tap() }
        XCTAssertTrue(app.buttons["Scene"].firstMatch.waitForExistence(timeout: 10))
        app.buttons["Scene"].firstMatch.tap()
        app.textFields["Enter scene title..."].typeText(sceneTitle)
        if app.buttons["Hide keyboard"].exists && app.buttons["Hide keyboard"].isHittable { app.buttons["Hide keyboard"].tap() }
        app.buttons["OK"].tap()
        XCTAssertTrue(app.buttons[sceneTitle].waitForExistence(timeout: 10))
        navigate(app, "Manuscript")
        XCTAssertTrue(app.buttons["Corkboard"].waitForExistence(timeout: 10))
        XCTAssertTrue(app.textViews.firstMatch.waitForExistence(timeout: 10))
        let editor = try XCTUnwrap(app.textViews.allElementsBoundByIndex.first {
            ($0.value as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        }, "New manuscript scene must initially be empty")
        XCTAssertFalse((editor.value as? String ?? "").contains(marker))
        editor.tap()
        editor.typeText(marker)
        XCUIDevice.shared.press(.home)
        app.terminate()
        app.launch()
        let book = app.switches["Select Audit Tablet Book — Audit Tablet Fixture"]
        XCTAssertTrue(book.waitForExistence(timeout: 20))
        book.doubleTap()
        navigate(app, "Manuscript")
        XCTAssertTrue(app.buttons["Corkboard"].waitForExistence(timeout: 10))
        let restored = app.textViews.matching(NSPredicate(format: "value CONTAINS %@", marker)).firstMatch
        XCTAssertTrue(restored.waitForExistence(timeout: 10))
        print("AUDIT_FIXED_MANUSCRIPT_LIFECYCLE_PASS")
        let image = XCTAttachment(screenshot: app.screenshot())
        image.name = "fixed-manuscript-after-relaunch"
        image.lifetime = .keepAlways
        add(image)
    }
}
