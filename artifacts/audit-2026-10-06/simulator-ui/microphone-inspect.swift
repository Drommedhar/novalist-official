import XCTest
final class AuditUITests: XCTestCase {
    func testMicrophoneEntryInspection() throws {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
        app.activate()
        if app.buttons["selected"].exists && app.buttons["selected"].isHittable { app.buttons["selected"].tap() }
        let book = app.switches["Select Audit Book Alpha — Audit Simulator Fixture"]
        if book.exists { book.doubleTap(); app.buttons["Write"].tap() }
        let scene = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Audit Fixed Scene 20261007")).firstMatch
        if scene.exists { scene.tap() }
        print("AUDIT_MICROPHONE_ENTRY_BEGIN")
        print(app.debugDescription)
        print("AUDIT_MICROPHONE_ENTRY_END")
        if app.buttons["Dictation"].exists {
            app.buttons["Dictation"].tap()
            print("AUDIT_DICTATION_PANEL_BEGIN")
            print(app.debugDescription)
            print("AUDIT_DICTATION_PANEL_END")
        }
    }
}
