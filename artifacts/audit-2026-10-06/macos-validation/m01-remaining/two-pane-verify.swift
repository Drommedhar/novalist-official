import XCTest
final class AuditUITests: XCTestCase {
    func testVerifyBothRecoveredSceneBuffers() {
        continueAfterFailure=false
        let app=XCUIApplication(bundleIdentifier:"com.novalist.app");app.activate()
        for (title,marker) in [("Audit M01 Pane A","M01RIGHT26"),("Audit M01 Pane B","M01LEFT26")] {
            let row=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@ AND NOT label CONTAINS %@",title,"Close tab")).firstMatch
            XCTAssertTrue(row.waitForExistence(timeout:10));row.tap()
            let editor=app.textViews.firstMatch
            XCTAssertTrue(editor.waitForExistence(timeout:10))
            print("M01_NATIVE_VALUE_"+marker+":"+(editor.value as? String ?? "nil"))
            XCTAssertTrue((editor.value as? String ?? "").filter {!$0.isWhitespace}.contains(marker))
        }
        print("M01_TWO_NATIVE_BUFFERS_VERIFIED")
        let shot=XCTAttachment(screenshot:app.screenshot());shot.name="two-pane-recovered-B";shot.lifetime = .keepAlways;add(shot)
    }
}
