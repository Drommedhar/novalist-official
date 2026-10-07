import XCTest
final class AuditUITests: XCTestCase {
    func testOpenRealSplit() {
        continueAfterFailure = false
        let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
        app.activate();XCUIDevice.shared.orientation = .landscapeLeft
        let editor=app.textViews.firstMatch
        XCTAssertTrue(editor.waitForExistence(timeout:10));editor.tap()
        app.typeKey(XCUIKeyboardKey.rightArrow,modifierFlags:[.command,.alternate])
        Thread.sleep(forTimeInterval:1)
        print("M01_SPLIT_KEYBOARD_AX_BEGIN");print(app.debugDescription);print("M01_SPLIT_KEYBOARD_AX_END")
    }
}
