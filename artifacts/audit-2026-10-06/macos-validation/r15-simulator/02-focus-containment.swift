import XCTest
final class AuditUITests: XCTestCase {
    func testNativeFocusContainment() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        for (name, orientation) in [("portrait", UIDeviceOrientation.portrait), ("landscape", UIDeviceOrientation.landscapeLeft)] {
            XCUIDevice.shared.orientation = orientation
            let trigger = app.buttons["Inspector"]
            XCTAssertTrue(trigger.waitForExistence(timeout: 10)); trigger.tap()
            let close = app.buttons["Close"]
            XCTAssertTrue(close.waitForExistence(timeout: 10)); XCTAssertTrue(close.hasFocus)
            app.buttons["To do"].tap()
            let input = app.textFields["What needs doing"]
            let list = app.textFields["List (optional)"]
            XCTAssertTrue(input.waitForExistence(timeout: 5)); input.tap()
            for _ in 0..<5 { app.typeKey(XCUIKeyboardKey.tab, modifierFlags: [.shift]) }
            print("R15_\(name)_FIRST_CLOSE_FOCUSED=\(close.hasFocus)")
            XCTAssertTrue(close.hasFocus)
            app.typeKey(XCUIKeyboardKey.tab, modifierFlags: [.shift])
            print("R15_\(name)_BACKWARD_WRAP_LAST_FOCUSED=\(list.hasFocus)")
            XCTAssertTrue(list.hasFocus)
            app.typeKey(XCUIKeyboardKey.tab, modifierFlags: [])
            print("R15_\(name)_FORWARD_WRAP_FIRST_FOCUSED=\(close.hasFocus)")
            XCTAssertTrue(close.hasFocus)
            XCTAssertFalse(app.buttons["Inspector"].exists)
            app.typeKey(XCUIKeyboardKey.return, modifierFlags: [])
            let returned = trigger.waitForExistence(timeout: 3)
            print("R15_\(name)_RETURN_ACTIVATES_CLOSE=\(returned) triggerFocused=\(returned ? trigger.hasFocus : false)")
            if !returned { close.tap() }
            XCTAssertTrue(trigger.waitForExistence(timeout: 5))
            app.typeKey(XCUIKeyboardKey.tab, modifierFlags: [.shift])
            print("R15_\(name)_AFTER_CLOSE_SHIFT_TAB\n" + app.debugDescription)
        }
        XCUIDevice.shared.orientation = .portrait
    }
}
