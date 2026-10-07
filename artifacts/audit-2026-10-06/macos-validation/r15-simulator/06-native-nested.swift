import XCTest
final class AuditUITests: XCTestCase {
    func testNativeNestedShortcut() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        if !app.buttons["Close"].exists { app.buttons["Inspector"].tap() }
        XCTAssertTrue(app.buttons["Close"].waitForExistence(timeout: 10))
        app.buttons["To do"].tap()
        let input = app.textFields["What needs doing"]
        XCTAssertTrue(input.waitForExistence(timeout: 5)); input.tap()
        app.typeKey("p", modifierFlags: [.command, .shift])
        sleep(1)
        print("R15_NESTED_AFTER_COMMAND\n" + app.debugDescription)
        let shot = XCTAttachment(screenshot: app.screenshot()); shot.name = "native-nested-command"; shot.lifetime = .keepAlways; add(shot)
    }
}
