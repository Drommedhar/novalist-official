import XCTest
final class AuditUITests: XCTestCase {
 func testCleanCurrentNativeHelpRelaunch() {
  continueAfterFailure = false
  let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
  app.terminate(); app.launch(); XCUIDevice.shared.orientation = .landscapeLeft
  let more = app.buttons["More project actions"]
  XCTAssertTrue(more.waitForExistence(timeout: 25)); more.tap()
  let manual = app.buttons["Novalist Manual"]
  if !manual.waitForExistence(timeout: 2) { more.tap() }
  XCTAssertTrue(manual.waitForExistence(timeout: 5)); manual.tap()
  XCTAssertTrue(app.textFields["Search the manual…"].waitForExistence(timeout: 15))
  XCTAssertFalse(app.alerts["Editing paused"].exists)
  print("P01_CLEAN_NATIVE_HELP_RELAUNCH_PASS")
 }
}
