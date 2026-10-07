import XCTest
final class AuditUITests: XCTestCase {
    func testReleaseDismissalCleanNativeMatrix() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        for (name, orientation) in [("portrait", UIDeviceOrientation.portrait), ("landscape", UIDeviceOrientation.landscapeLeft)] {
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
            close.tap()
            XCTAssertTrue(app.buttons["Inspector"].waitForExistence(timeout: 5))
            XCTAssertTrue(app.textViews.firstMatch.exists)
            print("R15_CLEAN_FIXED_\(name)_CONTAINMENT_AND_DISMISS_PASS")
        }
        app.terminate(); app.launch(); XCUIDevice.shared.orientation = .portrait
    }
}
