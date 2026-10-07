import XCTest

final class AuditUITests: XCTestCase {
    let app = XCUIApplication(bundleIdentifier: "com.novalist.app")

    func checkpoint(_ name: String) throws -> Int {
        let completed = expectation(description: "Native PID at \(name)")
        var response: [String: Any]?
        var failure: Error?
        let url = URL(string: "http://127.0.0.1:41097/checkpoint/\(name)")!
        URLSession.shared.dataTask(with: url) { data, _, error in
            failure = error
            if let data { response = try? JSONSerialization.jsonObject(with: data) as? [String: Any] }
            completed.fulfill()
        }.resume()
        wait(for: [completed], timeout: 20)
        if let failure { throw failure }
        let pid = try XCTUnwrap(response?["pid"] as? Int)
        XCTAssertGreaterThan(pid, 0, "Novalist must remain a live native process")
        print("M06_CHECKPOINT \(name) PID=\(pid)")
        return pid
    }

    func capture(_ name: String) {
        let attachment = XCTAttachment(screenshot: XCUIScreen.main.screenshot())
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
    }

    override func tearDownWithError() throws {
        if app.buttons["Stop dictation"].exists { app.buttons["Stop dictation"].tap() }
    }

    func testSameProcessBackgroundInterruptionAndRetry() throws {
        continueAfterFailure = false
        XCUIDevice.shared.orientation = .portrait
        app.terminate()
        app.launch()
        let book = app.switches["Select Audit Book Alpha — Audit Simulator Fixture"]
        if book.waitForExistence(timeout: 15) {
            book.doubleTap()
            XCTAssertTrue(app.buttons["Write"].waitForExistence(timeout: 15))
            app.buttons["Write"].tap()
            let scene = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Audit Fixed Scene 20261007")).firstMatch
            XCTAssertTrue(scene.waitForExistence(timeout: 10))
            scene.tap()
        }
        if !app.buttons["Start dictation"].exists {
            XCTAssertTrue(app.switches["Dictate"].waitForExistence(timeout: 10))
            app.switches["Dictate"].tap()
        }
        let start = app.buttons["Start dictation"]
        let stop = app.buttons["Stop dictation"]
        let listening = app.staticTexts["Listening…"]
        let permission = app.staticTexts["Microphone access was denied. Allow microphone access in your system settings, then try again."]
        let generic = app.staticTexts["The microphone could not be opened. Check that it is connected and available."]
        XCTAssertTrue(start.waitForExistence(timeout: 10))
        XCTAssertFalse(permission.exists)
        XCTAssertFalse(generic.exists)
        let initialPID = try checkpoint("before-first-start")
        start.tap()
        XCTAssertTrue(listening.waitForExistence(timeout: 10), "Native startup must succeed; unavailable startup is a failure")
        XCTAssertTrue(stop.exists)
        XCTAssertFalse(permission.exists)
        XCTAssertFalse(generic.exists)
        XCTAssertEqual(try checkpoint("first-listening"), initialPID)
        capture("01-first-native-listening")
        XCUIDevice.shared.press(.home)
        XCTAssertTrue(app.wait(for: .runningBackground, timeout: 8))
        XCTAssertEqual(try checkpoint("backgrounded"), initialPID)
        app.activate()
        XCTAssertTrue(start.waitForExistence(timeout: 10))
        XCTAssertFalse(stop.exists)
        XCTAssertFalse(listening.exists)
        XCTAssertFalse(permission.exists)
        XCTAssertFalse(generic.exists)
        XCTAssertEqual(try checkpoint("foreground-stopped"), initialPID)
        capture("02-foreground-stopped")
        start.tap()
        XCTAssertTrue(listening.waitForExistence(timeout: 10), "Retry after background interruption must activate native recording again")
        XCTAssertTrue(stop.exists)
        XCTAssertFalse(permission.exists)
        XCTAssertFalse(generic.exists)
        XCTAssertEqual(try checkpoint("retry-listening"), initialPID)
        capture("03-retry-native-listening")
        stop.tap()
        XCTAssertTrue(start.waitForExistence(timeout: 10))
        XCTAssertFalse(stop.exists)
        XCTAssertFalse(listening.exists)
        XCTAssertFalse(permission.exists)
        XCTAssertFalse(generic.exists)
        XCTAssertEqual(try checkpoint("explicitly-stopped"), initialPID)
        capture("04-explicitly-stopped")
        print("M06_SAME_PROCESS_BACKGROUND_RETRY_PASS PID=\(initialPID)")
    }
}
