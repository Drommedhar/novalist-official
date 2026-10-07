import XCTest
final class AuditUITests: XCTestCase {
    func testNativeInspectorInput() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.terminate(); app.launch()
        XCUIDevice.shared.orientation = .portrait
        let card = app.descendants(matching: .any).matching(NSPredicate(format: "label BEGINSWITH %@", "Select The Cartographer")).firstMatch
        XCTAssertTrue(card.waitForExistence(timeout: 25)); card.doubleTap()
        XCTAssertTrue(app.buttons["Write"].waitForExistence(timeout: 20))
        if app.buttons["Close tour"].exists { app.buttons["Close tour"].tap() }
        app.buttons["Write"].tap()
        let scene = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "A Letter from Bellhaven ")).firstMatch
        XCTAssertTrue(scene.waitForExistence(timeout: 15)); scene.tap()
        XCTAssertTrue(app.textViews.firstMatch.waitForExistence(timeout: 15))
        if app.buttons["selected"].waitForExistence(timeout: 3) { app.buttons["selected"].tap() }
        for (name, orientation) in [("portrait", UIDeviceOrientation.portrait), ("landscape", UIDeviceOrientation.landscapeLeft)] {
            XCUIDevice.shared.orientation = orientation
            let trigger = app.buttons["Inspector"]
            XCTAssertTrue(trigger.waitForExistence(timeout: 10)); trigger.tap()
            let close = app.buttons["Close"]
            XCTAssertTrue(close.waitForExistence(timeout: 10))
            print("R15_\(name)_OPEN closeFocused=\(close.hasFocus) editorVisible=\(app.textViews.firstMatch.exists) triggerVisible=\(trigger.exists)")
            let shot = XCTAttachment(screenshot: app.screenshot()); shot.name = "inspector-" + name; shot.lifetime = .keepAlways; add(shot)
            app.typeKey(XCUIKeyboardKey.escape, modifierFlags: [])
            let initialDismissed = trigger.waitForExistence(timeout: 3)
            print("R15_\(name)_INITIAL_ESCAPE dismissed=\(initialDismissed) triggerFocused=\(initialDismissed ? trigger.hasFocus : false)")
            if initialDismissed { trigger.tap(); XCTAssertTrue(close.waitForExistence(timeout: 5)) }
            app.buttons["To do"].tap()
            let input = app.textFields["What needs doing"]
            XCTAssertTrue(input.waitForExistence(timeout: 5)); input.tap()
            app.typeKey("r", modifierFlags: [])
            XCTAssertEqual(input.value as? String, "r")
            print("R15_\(name)_KEY_DELIVERY r=true inputFocused=\(input.hasFocus)")
            app.typeKey(XCUIKeyboardKey.delete, modifierFlags: [])
            app.typeKey(XCUIKeyboardKey.tab, modifierFlags: [])
            let list = app.textFields["List (optional)"]
            print("R15_\(name)_TAB inputFocused=\(input.hasFocus) listFocused=\(list.hasFocus)")
            app.typeKey(XCUIKeyboardKey.tab, modifierFlags: [.shift])
            print("R15_\(name)_SHIFT_TAB inputFocused=\(input.hasFocus) listFocused=\(list.hasFocus)")
            app.typeKey(XCUIKeyboardKey.escape, modifierFlags: [])
            let escaped = trigger.waitForExistence(timeout: 3)
            print("R15_\(name)_FOCUSED_ESCAPE dismissed=\(escaped) triggerFocused=\(escaped ? trigger.hasFocus : false)")
            if !escaped { print("R15_\(name)_HIERARCHY\n" + app.debugDescription); close.tap() }
            XCTAssertTrue(trigger.waitForExistence(timeout: 5))
            print("R15_\(name)_CLOSE_RETURN triggerFocused=\(trigger.hasFocus)")
        }
        XCUIDevice.shared.orientation = .portrait
    }
}
