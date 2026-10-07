import XCTest
final class AuditUITests: XCTestCase {
    func testTwoDirtyPaneRecovery() {
        continueAfterFailure=false
        let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
        app.activate();XCUIDevice.shared.orientation = .landscapeLeft
        XCTAssertEqual(app.textViews.count,2)
        let editors=app.textViews.allElementsBoundByIndex.sorted {$0.frame.minX < $1.frame.minX}
        XCTAssertFalse((editors[0].value as? String ?? "").contains("M01LEFT26"))
        XCTAssertFalse((editors[1].value as? String ?? "").contains("M01RIGHT26"))
        print("M01_TWO_READY_GATE");Thread.sleep(forTimeInterval:3)
        editors[0].coordinate(withNormalizedOffset:CGVector(dx:0.5,dy:0.05)).tap();editors[0].typeText("M01LEFT26")
        editors[1].coordinate(withNormalizedOffset:CGVector(dx:0.5,dy:0.05)).tap();editors[1].typeText("M01RIGHT26")
        print("M01_TWO_TYPED");Thread.sleep(forTimeInterval:5)
        print("M01_TWO_BEFORE_HOME");XCUIDevice.shared.press(.home);Thread.sleep(forTimeInterval:1.5)
        app.terminate();print("M01_TWO_TERMINATED")
        app.launch()
        let book=app.switches["Select Audit Tablet Book — Audit Tablet Fixture"]
        XCTAssertTrue(book.waitForExistence(timeout:20));book.doubleTap()
        Thread.sleep(forTimeInterval:3)
        for (title,marker) in [("Audit M01 Pane A","M01RIGHT26"),("Audit M01 Pane B","M01LEFT26")] {
            let row=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@ AND NOT label CONTAINS %@",title,"Close tab")).firstMatch
            if !row.exists {app.switches["Toggle binder"].tap()}
            XCTAssertTrue(row.waitForExistence(timeout:10));row.tap()
            let recovered=app.textViews.matching(NSPredicate(format:"value CONTAINS %@",marker)).firstMatch
            XCTAssertTrue(recovered.waitForExistence(timeout:10))
        }
        print("M01_TWO_RESTORED_PASS")
        let shot=XCTAttachment(screenshot:app.screenshot());shot.name="two-independent-panes-after-recovery";shot.lifetime = .keepAlways;add(shot)
    }
}
