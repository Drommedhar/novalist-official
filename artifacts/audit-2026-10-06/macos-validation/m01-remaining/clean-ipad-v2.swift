import XCTest
final class AuditUITests:XCTestCase {
    func testCleanBuildRecoveredScenes() {
        continueAfterFailure=false
        let app=XCUIApplication(bundleIdentifier:"com.novalist.app");app.terminate();app.launch();XCUIDevice.shared.orientation = .landscapeLeft
        let book=app.switches["Select Audit Tablet Book — Audit Tablet Fixture"]
        XCTAssertTrue(book.waitForExistence(timeout:20));book.doubleTap()
        for(title,marker)in[("Audit M01 Pane A","M01RIGHT26"),("Audit M01 Pane B","M01LEFT26"),("Audit M01 Scoped Recovery","M01SCOPE26")] {
            let row=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@ AND NOT label CONTAINS %@",title,"Close tab")).firstMatch
            XCTAssertTrue(row.waitForExistence(timeout:10));row.tap();let editor=app.textViews.firstMatch;XCTAssertTrue(editor.waitForExistence(timeout:10));XCTAssertTrue((editor.value as? String ?? "").filter{!$0.isWhitespace}.contains(marker))
        }
        XCTAssertFalse(app.staticTexts["Editing paused"].exists);print("M01_CLEAN_IPAD_THREE_RECOVERED_SCENES_PASS")
        let shot=XCTAttachment(screenshot:app.screenshot());shot.name="clean-ipad-recovered-prose";shot.lifetime = .keepAlways;add(shot)
    }
}
