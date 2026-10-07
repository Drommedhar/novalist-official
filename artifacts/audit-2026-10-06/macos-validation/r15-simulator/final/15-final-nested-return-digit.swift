import XCTest
final class AuditUITests: XCTestCase {
    func testFixedNativeNestedReturnDigit() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        let input = app.textFields["What needs doing"]
        XCTAssertTrue(input.waitForExistence(timeout: 5)); XCTAssertTrue(input.hasFocus)
        XCTAssertEqual(input.value as? String, "Q")
        app.typeKey(XCUIKeyboardKey.delete, modifierFlags: [])
        sleep(1)
        app.typeKey("7", modifierFlags: [])
        XCTAssertEqual(input.value as? String, "7")
        app.typeKey(XCUIKeyboardKey.delete, modifierFlags: [])
        print("R15_FINAL_NATIVE_NESTED_RETURN_DIGIT_PASS inputFocused=\(input.hasFocus)")
    }
}
