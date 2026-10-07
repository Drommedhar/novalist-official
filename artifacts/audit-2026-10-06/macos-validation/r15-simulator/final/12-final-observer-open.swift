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
        app.buttons["Inspector"].tap()
        XCTAssertTrue(app.buttons["Close"].waitForExistence(timeout: 10))
        print("R15_OBSERVER_READY")
    }
}
