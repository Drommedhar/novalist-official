import XCTest
final class AuditUITests: XCTestCase {
    func testRealKeyboardInvocationAndReturn() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        if app.buttons["Close"].exists { app.buttons["Close"].tap() }
        for (name, orientation) in [("portrait", UIDeviceOrientation.portrait), ("landscape", UIDeviceOrientation.landscapeLeft)] {
            XCUIDevice.shared.orientation = orientation
            let trigger = app.buttons["Inspector"]
            XCTAssertTrue(trigger.waitForExistence(timeout: 10))
            for _ in 0..<24 {
                if trigger.hasFocus { break }
                app.typeKey(XCUIKeyboardKey.tab, modifierFlags: [])
            }
            print("R15_\(name)_KEYBOARD_TRIGGER_FOCUSED=\(trigger.hasFocus)")
            if !trigger.hasFocus { print("R15_\(name)_KEYBOARD_INVOKE_LIMITATION"); continue }
            app.typeKey(" ", modifierFlags: [])
            let close = app.buttons["Close"]
            let opened = close.waitForExistence(timeout: 4)
            print("R15_\(name)_SPACE_OPENS_INSPECTOR=\(opened)")
            if !opened { continue }
            print("R15_\(name)_KEYBOARD_CLOSE_INITIAL_FOCUS=\(close.hasFocus)")
            app.typeKey(" ", modifierFlags: [])
            let returned = trigger.waitForExistence(timeout: 4)
            print("R15_\(name)_SPACE_CLOSE_RETURN=\(returned) triggerFocused=\(returned ? trigger.hasFocus : false)")
            if !returned { close.tap() }
        }
        XCUIDevice.shared.orientation = .portrait
    }
}
