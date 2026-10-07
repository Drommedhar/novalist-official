import XCTest

final class AuditUITests: XCTestCase {
    func openScene(_ app: XCUIApplication) {
        if app.switches["Select Audit Book Alpha — Audit Simulator Fixture"].waitForExistence(timeout: 2) {
            app.switches["Select Audit Book Alpha — Audit Simulator Fixture"].doubleTap()
        }
        XCTAssertTrue(app.buttons["Write"].waitForExistence(timeout: 20))
        if app.buttons["Close tour"].exists { app.buttons["Close tour"].tap() }
        app.buttons["Write"].tap()
        let scene = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Audit Scene Alpha")).firstMatch
        if scene.waitForExistence(timeout: 3) { scene.tap() }
        XCTAssertTrue(app.buttons["Manuscript"].waitForExistence(timeout: 10))
        XCTAssertTrue(app.textViews.firstMatch.waitForExistence(timeout: 10))
    }

    func testSceneBackgroundTerminateRelaunch() throws {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        openScene(app)
        let editor = app.textViews.firstMatch
        print("AUDIT_ORIGINAL_MARKER_PRESENT=\((editor.value as? String ?? "").contains("AUDIT_SCENE_HOME_20261007"))")
        let marker = "AUDIT_LIFECYCLE_SECOND_20261007"
        editor.tap()
        editor.typeText("\n" + marker)
        XCUIDevice.shared.press(.home)
        app.terminate()
        app.launch()
        openScene(app)
        let restored = app.textViews.firstMatch.value as? String ?? ""
        XCTAssertTrue(restored.contains(marker), "Synthetic scene marker must survive background, termination and project reopen")
        print("AUDIT_SCENE_BACKGROUND_TERMINATE_RELAUNCH_PASS")
        let image = XCTAttachment(screenshot: app.screenshot())
        image.name = "scene-after-relaunch"
        image.lifetime = .keepAlways
        add(image)
    }
}
