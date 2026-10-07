import XCTest

// Run on a fresh, demo-seeded simulator with capture-iphone.py. Captures the
// unmodified native app using only accessibility actions and touch gestures.
final class AuditUITests: XCTestCase {
    private let app = XCUIApplication(bundleIdentifier: "com.novalist.app")

    override func setUpWithError() throws {
        continueAfterFailure = false
        app.activate()
        XCUIDevice.shared.orientation = .portrait
    }

    private func buttonStarting(_ title: String) -> XCUIElement {
        app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", title)).firstMatch
    }

    func test00Welcome() throws {
        continueAfterFailure = false
        app.terminate()
        app.launch()
        XCUIDevice.shared.orientation = .portrait
        let card = app.descendants(matching: .any).matching(
            NSPredicate(format: "label BEGINSWITH %@", "Select The Cartographer")
        ).firstMatch
        XCTAssertTrue(card.waitForExistence(timeout: 30))
    }

    func test01Dashboard() throws {
        let card = app.descendants(matching: .any).matching(
            NSPredicate(format: "label BEGINSWITH %@", "Select The Cartographer")
        ).firstMatch
        XCTAssertTrue(card.waitForExistence(timeout: 15))
        card.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.5)).doubleTap()
        XCTAssertTrue(app.staticTexts["Project Dashboard"].waitForExistence(timeout: 30))
        if app.buttons["Close tour"].waitForExistence(timeout: 3) {
            app.buttons["Close tour"].tap()
        }

        // Show the real daily goal, streak and history cards using natural scroll.
        let progress = app.buttons["DAILY PROGRESS"]
        XCTAssertTrue(progress.waitForExistence(timeout: 15))
        for _ in 0..<8 {
            let offset = progress.frame.minY - 140
            if abs(offset) < 25 { break }
            let amount = max(-300, min(offset, app.frame.height * 0.5))
            let start = app.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: amount > 0 ? 0.8 : 0.3))
            let end = start.withOffset(CGVector(dx: 0, dy: -amount))
            start.press(forDuration: 0.3, thenDragTo: end, withVelocity: .slow, thenHoldForDuration: 0.5)
            sleep(1)
        }
        XCTAssertTrue(progress.isHittable)
    }

    func test02Write() throws {
        app.buttons["Write"].tap()
        let scene = buttonStarting("A Letter from Bellhaven ")
        XCTAssertTrue(scene.waitForExistence(timeout: 15))
    }

    func test03Editor() throws {
        buttonStarting("A Letter from Bellhaven ").tap()
        XCTAssertTrue(app.textViews.firstMatch.waitForExistence(timeout: 15))
        if app.buttons["selected"].waitForExistence(timeout: 5) {
            app.buttons["selected"].tap()
        }
        XCTAssertFalse(app.keyboards.firstMatch.exists)
    }

    func test04Codex() throws {
        app.buttons["Codex"].tap()
        let mira = app.buttons["Mira Aldencourt Protagonist F"]
        XCTAssertTrue(mira.waitForExistence(timeout: 15))
    }

    func test05CodexEntity() throws {
        app.buttons["Mira Aldencourt Protagonist F"].tap()
        XCTAssertTrue(app.textFields.matching(NSPredicate(format: "value == %@", "Mira")).firstMatch.waitForExistence(timeout: 15))
    }

    func test06Wiki() throws {
        app.buttons["Wiki"].tap()
        let article = app.buttons["Mira Aldencourt Protagonist"]
        XCTAssertTrue(article.waitForExistence(timeout: 15))
    }

    func test07WikiArticle() throws {
        app.buttons["Mira Aldencourt Protagonist"].tap()
        XCTAssertTrue(app.buttons["Edit in Codex"].waitForExistence(timeout: 15))
    }

    func test08PlanMenu() throws {
        app.buttons["Plan"].tap()
        XCTAssertTrue(app.buttons["Timeline"].waitForExistence(timeout: 10))
    }

    func test09Timeline() throws {
        app.buttons["Timeline"].tap()
        XCTAssertTrue(app.buttons["Add Event"].waitForExistence(timeout: 15))
        sleep(1)
    }
}
