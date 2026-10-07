import XCTest
final class AuditUITests: XCTestCase {
    func testFixedNativeNestedReturn() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        XCTAssertTrue(app.staticTexts["No matching commands."].waitForExistence(timeout: 5))
        app.coordinate(withNormalizedOffset: CGVector(dx: 0.02, dy: 0.25)).tap()
        let input = app.textFields["What needs doing"]
        XCTAssertTrue(input.waitForExistence(timeout: 5)); XCTAssertTrue(input.hasFocus)
        XCTAssertFalse(app.staticTexts["No matching commands."].exists)
        XCTAssertTrue(app.buttons["Close"].exists)
        print("R15_FIXED_NATIVE_NESTED_RETURN_PASS inputFocused=\(input.hasFocus)")
    }
}
