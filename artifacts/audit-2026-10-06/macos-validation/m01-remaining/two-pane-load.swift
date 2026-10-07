import XCTest
final class AuditUITests: XCTestCase {
    func testLoadIndependentSceneInNewPane() {
        continueAfterFailure=false
        let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
        app.activate()
        app.coordinate(withNormalizedOffset:CGVector(dx:0.85,dy:0.5)).tap()
        let scene=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@","Audit M01 Pane A")).firstMatch
        XCTAssertTrue(scene.waitForExistence(timeout:10));scene.tap()
        Thread.sleep(forTimeInterval:1)
        XCTAssertEqual(app.textViews.count,2)
        print("M01_TWO_INDEPENDENT_EDITOR_PANES_LOADED")
    }
}
