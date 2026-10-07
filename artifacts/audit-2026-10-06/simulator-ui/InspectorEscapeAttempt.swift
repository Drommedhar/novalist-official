import XCTest
final class AuditUITests: XCTestCase {
    func testInspectorLayoutsAndEscape() throws {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        for (name, orientation) in [("portrait", UIDeviceOrientation.portrait), ("landscape", UIDeviceOrientation.landscapeLeft)] {
            XCUIDevice.shared.orientation = orientation
            if app.buttons["Close"].waitForExistence(timeout: 1) { app.buttons["Close"].tap() }
            if app.buttons["selected"].exists { app.buttons["selected"].tap() }
            XCTAssertTrue(app.buttons["Inspector"].waitForExistence(timeout: 10))
            app.buttons["Inspector"].tap()
            XCTAssertTrue(app.buttons["Close"].waitForExistence(timeout: 10))
            XCTAssertTrue(app.buttons["Close"].isHittable)
            XCTAssertTrue(app.buttons["Context"].exists)
            XCTAssertFalse(app.buttons["Inspector"].exists)
            let image = XCTAttachment(screenshot: app.screenshot())
            image.name = "inspector-" + name
            image.lifetime = .keepAlways
            add(image)
            app.typeKey(XCUIKeyboardKey.escape, modifierFlags: [])
            XCTAssertTrue(app.buttons["Inspector"].waitForExistence(timeout: 10))
            XCTAssertFalse(app.buttons["Close"].exists)
            print("AUDIT_INSPECTOR_\(name)_ESCAPE_PASS")
        }
        XCUIDevice.shared.orientation = .portrait
    }
}
