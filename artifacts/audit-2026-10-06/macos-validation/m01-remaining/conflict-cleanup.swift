import XCTest
final class AuditUITests:XCTestCase {
    func testExplicitlyResolveSyntheticConflict() {
        continueAfterFailure=false
        let app=XCUIApplication(bundleIdentifier:"com.novalist.app");app.activate();XCUIDevice.shared.orientation = .portrait
        let mine=app.buttons["Take all of mine"]
        XCTAssertTrue(mine.waitForExistence(timeout:10));mine.tap();app.buttons["Save this version"].tap()
        XCTAssertTrue(mine.waitForNonExistence(timeout:15));XCTAssertFalse(app.staticTexts["Editing paused"].exists)
        print("M01_SYNTHETIC_CONFLICT_EXPLICITLY_RESOLVED")
    }
}
