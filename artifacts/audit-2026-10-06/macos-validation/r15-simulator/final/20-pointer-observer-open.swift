import XCTest
final class AuditUITests: XCTestCase {
    func testOpenLandscapeForPointerObservation() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        for (name, orientation) in [("landscape", UIDeviceOrientation.landscapeLeft)] {
            app.terminate(); app.launch()
            XCUIDevice.shared.orientation = orientation
            let card = app.descendants(matching: .any).matching(NSPredicate(format: "label BEGINSWITH %@", "Select The Cartographer")).firstMatch
            XCTAssertTrue(card.waitForExistence(timeout: 25)); card.doubleTap()
            XCTAssertTrue(app.buttons["Write"].waitForExistence(timeout: 20))
            if app.buttons["Close tour"].exists { app.buttons["Close tour"].tap() }
            app.buttons["Write"].tap()
            let scene = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "A Letter from Bellhaven ")).firstMatch
            XCTAssertTrue(scene.waitForExistence(timeout: 15)); scene.tap()
            XCTAssertTrue(app.textViews.firstMatch.waitForExistence(timeout: 15))
            if app.buttons["selected"].waitForExistence(timeout: 3) { app.buttons["selected"].tap() }
            app.buttons["Inspector"].tap()
            let close = app.buttons["Close"]
            XCTAssertTrue(close.waitForExistence(timeout: 10)); XCTAssertTrue(close.hasFocus)
            XCTAssertFalse(app.textViews.firstMatch.exists)
            app.buttons["To do"].tap()
            let input = app.textFields["What needs doing"]
            XCTAssertTrue(input.waitForExistence(timeout: 5)); input.tap()
            for _ in 0..<10 {
                if close.hasFocus { break }
                app.typeKey(XCUIKeyboardKey.tab, modifierFlags: [.shift])
            }
            XCTAssertTrue(close.hasFocus)
            app.typeKey(XCUIKeyboardKey.tab, modifierFlags: [.shift])
            XCTAssertTrue(app.textFields["List (optional)"].hasFocus)
            app.typeKey(XCUIKeyboardKey.tab, modifierFlags: [])
            XCTAssertTrue(close.hasFocus)
            input.tap()
            print("R15_LANDSCAPE_POINTER_OBSERVER_READY")
        }
    }
}
