import XCTest
final class AuditUITests: XCTestCase {
    let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
    func openBook() {
        let book=app.switches["Select Audit Book Alpha — Audit Simulator Fixture"]
        XCTAssertTrue(book.waitForExistence(timeout:20));book.doubleTap()
    }
    func assertConflictMarkers() {
        XCTAssertTrue(app.buttons["Take all of mine"].waitForExistence(timeout:12))
        for marker in ["M01LOCAL26","M01EXTERNAL26"] {
            let row=app.buttons.matching(NSPredicate(format:"label CONTAINS %@",marker)).firstMatch
            XCTAssertTrue(row.waitForExistence(timeout:10))
        }
    }
    func testDiskConflictRemainsRecoverable() {
        continueAfterFailure=false
        app.activate();XCUIDevice.shared.orientation = .portrait
        app.buttons["Write"].tap()
        let scene=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@","Audit M01 disk conflict A")).firstMatch
        XCTAssertTrue(scene.waitForExistence(timeout:10))
        for _ in 0..<6 {
            if scene.frame.maxY < app.frame.height*0.78 && scene.frame.minY > 80 {break}
            app.coordinate(withNormalizedOffset:CGVector(dx:0.5,dy:0.75)).press(forDuration:0.05,thenDragTo:app.coordinate(withNormalizedOffset:CGVector(dx:0.5,dy:0.3)))
        }
        scene.tap()
        let editor=app.textViews.firstMatch;XCTAssertTrue(editor.waitForExistence(timeout:10));editor.tap()
        print("M01_CONFLICT_READY_GATE");Thread.sleep(forTimeInterval:3)
        editor.typeText("M01LOCAL26");print("M01_CONFLICT_LOCAL_TYPED")
        assertConflictMarkers();print("M01_CONFLICT_VISIBLE");Thread.sleep(forTimeInterval:3)
        print("M01_CONFLICT_BEFORE_HOME");XCUIDevice.shared.press(.home);Thread.sleep(forTimeInterval:1.5)
        app.terminate();print("M01_CONFLICT_TERMINATED");app.launch();openBook()
        assertConflictMarkers();print("M01_CONFLICT_RESTORED_PASS")
        let shot=XCTAttachment(screenshot:app.screenshot());shot.name="disk-conflict-after-relaunch";shot.lifetime = .keepAlways;add(shot)
    }
}
