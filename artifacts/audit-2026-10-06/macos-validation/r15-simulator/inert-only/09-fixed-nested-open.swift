import XCTest
final class AuditUITests: XCTestCase {
    func testFixedNativeNestedOpen() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        XCTAssertTrue(app.buttons["Close"].waitForExistence(timeout: 10))
        app.buttons["To do"].tap()
        let input = app.textFields["What needs doing"]
        XCTAssertTrue(input.waitForExistence(timeout: 5)); input.tap()
        app.typeKey("p", modifierFlags: [.command, .shift])
        let palette = app.descendants(matching: .any).matching(NSPredicate(format: "label == %@ OR placeholderValue == %@", "Type a command name...", "Type a command name...")).firstMatch
        XCTAssertTrue(palette.waitForExistence(timeout: 5))
        XCTAssertFalse(input.exists)
        XCTAssertFalse(app.buttons["Close"].exists)
        app.typeText("r15nomatchingcommand")
        XCTAssertTrue(app.staticTexts["No matching commands."].waitForExistence(timeout: 5))
        app.typeKey(XCUIKeyboardKey.tab, modifierFlags: [])
        app.typeKey(XCUIKeyboardKey.tab, modifierFlags: [.shift])
        print("R15_FIXED_NATIVE_NESTED_OPEN_PASS\n" + app.debugDescription)
        let shot = XCTAttachment(screenshot: app.screenshot()); shot.name = "fixed-native-nested"; shot.lifetime = .keepAlways; add(shot)
    }
}
