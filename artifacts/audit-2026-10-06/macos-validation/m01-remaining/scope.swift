import XCTest
final class AuditUITests:XCTestCase {
    func testRetainedRecoveryScopeIsolation() {
        continueAfterFailure=false
        let app=XCUIApplication(bundleIdentifier:"com.novalist.app");app.activate();XCUIDevice.shared.orientation = .landscapeLeft
        app.buttons["Scene"].firstMatch.tap();app.textFields["Enter scene title..."].typeText("Audit M01 Scoped Recovery")
        if app.buttons["Hide keyboard"].exists && app.buttons["Hide keyboard"].isHittable {app.buttons["Hide keyboard"].tap()}
        app.buttons["OK"].tap()
        let scene=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@","Audit M01 Scoped Recovery")).firstMatch
        XCTAssertTrue(scene.waitForExistence(timeout:10));scene.tap()
        let editor=app.textViews.firstMatch;XCTAssertTrue(editor.waitForExistence(timeout:10));editor.coordinate(withNormalizedOffset:CGVector(dx:0.5,dy:0.1)).tap()
        print("M01_SCOPE_READY_GATE");Thread.sleep(forTimeInterval:3)
        editor.typeText("M01SCOPE26");print("M01_SCOPE_TYPED");Thread.sleep(forTimeInterval:4)
        XCUIDevice.shared.press(.home);Thread.sleep(forTimeInterval:1.2);app.terminate();print("M01_SCOPE_TERMINATED")
        Thread.sleep(forTimeInterval:4);app.launch()
        let other=app.switches["Select Audit M01 Other Book — Audit Tablet Fixture"]
        XCTAssertTrue(other.waitForExistence(timeout:20));other.doubleTap();print("M01_SCOPE_OTHER_BOOK_OPEN")
        Thread.sleep(forTimeInterval:30)
        let recovered=app.textViews.firstMatch;XCTAssertTrue(recovered.waitForExistence(timeout:15))
        XCTAssertTrue((recovered.value as? String ?? "").contains("M01SCOPE26"))
        print("M01_SCOPE_RETURNED_EXACT_BUFFER_PASS")
        let shot=XCTAttachment(screenshot:app.screenshot());shot.name="scope-correct-owner-recovered";shot.lifetime = .keepAlways;add(shot)
    }
}
