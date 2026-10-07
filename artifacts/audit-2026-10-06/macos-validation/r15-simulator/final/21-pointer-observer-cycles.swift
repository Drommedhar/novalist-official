import XCTest
final class AuditUITests: XCTestCase {
    func testObserveNativePointerDismissal() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app"); app.activate()
        let input = app.textFields["What needs doing"]
        let close = app.buttons["Close"]
        let name = "landscape-pointer"
            for opening in 0..<2 {
                app.typeKey("p", modifierFlags: [.command, .shift])
                let palette = app.descendants(matching: .any).matching(NSPredicate(format: "label == %@ OR placeholderValue == %@", "Type a command name...", "Type a command name...")).firstMatch
                XCTAssertTrue(palette.waitForExistence(timeout: 5))
                XCTAssertFalse(input.exists); XCTAssertFalse(close.exists)
                app.typeText("r15nomatchingcommand")
                XCTAssertTrue(app.staticTexts["No matching commands."].waitForExistence(timeout: 5))
                app.typeKey(XCUIKeyboardKey.tab, modifierFlags: [])
                app.typeKey(XCUIKeyboardKey.tab, modifierFlags: [.shift])
                let shot = XCTAttachment(screenshot: XCUIScreen.main.screenshot()); shot.name = "fixed-clean-" + name + "-" + String(opening); shot.lifetime = .keepAlways; add(shot)
                app.coordinate(withNormalizedOffset: CGVector(dx: 0.02, dy: 0.25)).tap()
                XCTAssertTrue(input.waitForExistence(timeout: 5)); XCTAssertTrue(input.hasFocus)
                XCTAssertFalse(app.staticTexts["No matching commands."].exists)
                sleep(1)
                app.typeKey("7", modifierFlags: [])
                XCTAssertEqual(input.value as? String, String(repeating: "7", count: opening + 1))
                print("R15_CLEAN_FIXED_\(name)_NESTED_\(opening)_PASS")
            }
    }
}
