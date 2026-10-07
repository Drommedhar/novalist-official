import XCTest
final class AuditUITests: XCTestCase {
    func testCleanNestedModal() {
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
            XCTAssertTrue(app.buttons["Close"].waitForExistence(timeout: 10))
            app.buttons["To do"].tap()
            let input = app.textFields["What needs doing"]
            XCTAssertTrue(input.waitForExistence(timeout: 5)); input.tap()
            app.typeKey("p", modifierFlags: [.command, .shift])
            sleep(1)
            let palette = app.descendants(matching: .any).matching(NSPredicate(format: "label == %@ OR placeholderValue == %@", "Type a command name...", "Type a command name...")).firstMatch
            print("R15_CLEAN_\(name)_PALETTE_ACCESSIBLE=\(palette.exists) inspectorCloseAccessible=\(app.buttons["Close"].exists)")
            print("R15_CLEAN_\(name)_HIERARCHY\n" + app.debugDescription)
            let shot = XCTAttachment(screenshot: app.screenshot()); shot.name = "clean-nested-" + name; shot.lifetime = .keepAlways; add(shot)
        }
        app.terminate(); app.launch(); XCUIDevice.shared.orientation = .portrait
    }
}
