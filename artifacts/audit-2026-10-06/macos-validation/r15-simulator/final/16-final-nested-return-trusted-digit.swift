import XCTest
final class AuditUITests: XCTestCase {
    func testFixedNativeNestedReturnTrustedDigit() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        let input = app.textFields["What needs doing"]
        XCTAssertTrue(input.waitForExistence(timeout: 5)); XCTAssertTrue(input.hasFocus)
        XCTAssertEqual(input.value as? String, "Q7")
        XCTAssertTrue(app.buttons["Close"].exists)
        XCTAssertFalse(app.staticTexts["No matching commands."].exists)
        print("R15_FINAL_NATIVE_NESTED_RETURN_DIGIT_PASS inputFocused=\(input.hasFocus)")
    }
}
