import XCTest
final class AuditUITests: XCTestCase {
    func testObserveNativeKeys() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        XCTAssertTrue(app.buttons["Close"].waitForExistence(timeout: 10))
        app.typeKey(XCUIKeyboardKey.escape, modifierFlags: [])
        app.typeKey(XCUIKeyboardKey.return, modifierFlags: [])
        print("R15_CLOSE_STILL_OPEN=\(app.buttons["Close"].exists)")
        app.buttons["To do"].tap()
        let input = app.textFields["What needs doing"]
        XCTAssertTrue(input.waitForExistence(timeout: 5)); input.tap()
        app.typeKey("r", modifierFlags: [])
        XCTAssertEqual(input.value as? String, "r")
        app.typeKey(XCUIKeyboardKey.delete, modifierFlags: [])
        app.typeKey(XCUIKeyboardKey.tab, modifierFlags: [])
        app.typeKey(XCUIKeyboardKey.tab, modifierFlags: [.shift])
        app.typeKey(XCUIKeyboardKey.escape, modifierFlags: [])
        app.typeKey(XCUIKeyboardKey.return, modifierFlags: [])
        print("R15_KEYS_COMPLETE")
    }
}
