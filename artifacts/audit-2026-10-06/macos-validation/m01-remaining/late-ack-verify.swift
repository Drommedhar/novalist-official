import XCTest
final class AuditUITests: XCTestCase {
    func testVerifyAutomaticallyRecoveredScene() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        let editor = app.textViews.firstMatch
        XCTAssertTrue(editor.waitForExistence(timeout: 10))
        XCTAssertTrue((editor.value as? String ?? "").contains("M01OLDA26M01NEWB26"))
        app.buttons["Manuscript"].tap()
        let scene = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Audit M01 late acknowledgement A")).firstMatch
        XCTAssertTrue(scene.waitForExistence(timeout: 10)); scene.tap()
        XCTAssertTrue(editor.waitForExistence(timeout: 10))
        XCTAssertTrue((editor.value as? String ?? "").contains("M01OLDA26M01NEWB26"))
        print("M01_AUTOMATIC_RECOVERY_EXACT_SCENE_VERIFIED")
        let shot = XCTAttachment(screenshot: app.screenshot())
        shot.name = "late-ack-recovered-scene"; shot.lifetime = .keepAlways; add(shot)
    }
}
