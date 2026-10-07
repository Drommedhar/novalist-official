import XCTest
final class AuditUITests:XCTestCase {
    func testCleanBuildRecoveredScene() {
        continueAfterFailure=false
        let app=XCUIApplication(bundleIdentifier:"com.novalist.app");app.terminate();app.launch();XCUIDevice.shared.orientation = .portrait
        let book=app.switches["Select Audit Book Alpha — Audit Simulator Fixture"]
        XCTAssertTrue(book.waitForExistence(timeout:20));book.doubleTap();app.buttons["Write"].tap()
        let scene=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@","Audit M01 late acknowledgement B")).firstMatch
        XCTAssertTrue(scene.waitForExistence(timeout:10))
        for _ in 0..<8 {
            if scene.frame.maxY < app.frame.height*0.78 && scene.frame.minY>80 {break}
            app.coordinate(withNormalizedOffset:CGVector(dx:0.5,dy:0.75)).press(forDuration:0.05,thenDragTo:app.coordinate(withNormalizedOffset:CGVector(dx:0.5,dy:0.3)))
        }
        scene.tap();let editor=app.textViews.firstMatch;XCTAssertTrue(editor.waitForExistence(timeout:10));XCTAssertTrue((editor.value as? String ?? "").contains("M01OLDC26M01NEWD26"));XCTAssertFalse(app.staticTexts["Editing paused"].exists)
        print("M01_CLEAN_IPHONE_RECOVERED_PROSE_PASS")
        let shot=XCTAttachment(screenshot:app.screenshot());shot.name="clean-iphone-recovered-prose";shot.lifetime = .keepAlways;add(shot)
    }
}
