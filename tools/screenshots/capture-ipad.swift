import XCTest

// Run in an XCTest UI harness on a fresh iPad seeded by sim-seed.sh.
// Export its named attachments with xcresulttool; normalize their orientation
// metadata before framing. Navigation uses the unmodified native app.
final class AuditUITests: XCTestCase {
    private let app = XCUIApplication(bundleIdentifier: "com.novalist.app")

    private func capture(_ name: String) {
        if app.buttons["Hide keyboard"].exists { app.buttons["Hide keyboard"].tap() }
        Thread.sleep(forTimeInterval: 2)
        XCTAssertFalse(app.alerts["Editing paused"].exists)
        let shot = XCTAttachment(screenshot: XCUIScreen.main.screenshot())
        shot.name = name
        shot.lifetime = .keepAlways
        add(shot)
    }

    private func dismissGuidance() {
        if app.buttons["Close tour"].exists { app.buttons["Close tour"].tap() }
        if app.buttons["Skip"].exists { app.buttons["Skip"].tap() }
        if app.buttons["Got it"].waitForExistence(timeout: 1) { app.buttons["Got it"].tap() }
    }

    private func binder(_ visible: Bool) {
        let toggle = app.switches["Toggle binder"]
        if toggle.exists && (toggle.value as? String == "1") != visible { toggle.tap() }
    }

    private func navigate(_ title: String) {
        let nav = app.scrollViews.staticTexts[title].firstMatch
        if !nav.exists { app.switches["Toggle sidebar"].tap() }
        XCTAssertTrue(nav.waitForExistence(timeout: 5), title)
        nav.tap()
        let toggle = app.switches["Toggle sidebar"]
        if toggle.value as? String == "1" { toggle.tap() }
        dismissGuidance()
    }

    private func buttonStarting(_ title: String) -> XCUIElement {
        app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", title)).firstMatch
    }

    func testCaptureViews() {
        continueAfterFailure = false
        app.terminate()
        app.launch()
        XCUIDevice.shared.orientation = .landscapeLeft
        let book = app.switches.matching(NSPredicate(
            format: "label BEGINSWITH %@ AND label CONTAINS %@", "Select ", "Cartographer"
        )).firstMatch
        XCTAssertTrue(book.waitForExistence(timeout: 20))
        capture("00-welcome")
        book.doubleTap()
        Thread.sleep(forTimeInterval: 2)
        dismissGuidance()
        binder(false)
        app.coordinate(withNormalizedOffset: CGVector(dx: 0.8, dy: 0.85)).press(
            forDuration: 0.05,
            thenDragTo: app.coordinate(withNormalizedOffset: CGVector(dx: 0.8, dy: 0.25))
        )
        capture("01-dashboard")
        navigate("Editor")
        binder(true)
        let scene = buttonStarting("What the Ink Concealed")
        XCTAssertTrue(scene.waitForExistence(timeout: 10))
        scene.tap()
        dismissGuidance()
        capture("02-editor")
        navigate("Manuscript")
        binder(false)
        capture("03-manuscript")
        navigate("Timeline")
        capture("04-timeline")
        navigate("Relationships")
        Thread.sleep(forTimeInterval: 2)
        capture("05-relationships")
        navigate("Codex")
        let mira = buttonStarting("Mira Aldencourt")
        XCTAssertTrue(mira.waitForExistence(timeout: 10))
        mira.tap()
        capture("06-codex")
        navigate("Wiki")
        let wikiMira = buttonStarting("Mira Aldencourt")
        XCTAssertTrue(wikiMira.waitForExistence(timeout: 10))
        wikiMira.tap()
        capture("07-wiki")
        navigate("Plot Grid")
        capture("08-plotgrid")
    }

    // Run after testCaptureViews and use this later dashboard attachment.
    // The fresh view's native scrolling reveals word goals and writing history.
    func testDashboardFraming() {
        continueAfterFailure = false
        app.activate()
        XCUIDevice.shared.orientation = .landscapeLeft
        navigate("Dashboard")
        if app.buttons["Chapters"].firstMatch.isHittable {
            app.switches["Toggle binder"].tap()
        }
        Thread.sleep(forTimeInterval: 1)
        app.coordinate(withNormalizedOffset: CGVector(dx: 0.8, dy: 0.85)).press(
            forDuration: 0.05,
            thenDragTo: app.coordinate(withNormalizedOffset: CGVector(dx: 0.8, dy: 0.25))
        )
        app.coordinate(withNormalizedOffset: CGVector(dx: 0.8, dy: 0.80)).press(
            forDuration: 0.05,
            thenDragTo: app.coordinate(withNormalizedOffset: CGVector(dx: 0.8, dy: 0.52))
        )
        capture("01-dashboard")
    }
}
