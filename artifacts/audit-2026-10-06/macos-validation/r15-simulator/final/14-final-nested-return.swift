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
        sleep(1)
        app.typeKey("q", modifierFlags: [])
        XCTAssertEqual(input.value as? String, "q")
        app.typeKey(XCUIKeyboardKey.delete, modifierFlags: [])
        print("R15_FINAL_NATIVE_NESTED_RETURN_PASS inputFocused=\(input.hasFocus)")
    }
}
