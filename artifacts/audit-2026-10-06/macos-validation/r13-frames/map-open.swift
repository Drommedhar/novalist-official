import XCTest
final class AuditUITests: XCTestCase {
 func testActualMapImage() {
  continueAfterFailure = false
  let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
  app.activate()
  XCUIDevice.shared.orientation = .landscapeLeft
  let nav=app.scrollViews.staticTexts["Maps"].firstMatch
  if !nav.waitForExistence(timeout:3) { app.switches["Toggle sidebar"].tap() }
  XCTAssertTrue(nav.waitForExistence(timeout:10));nav.tap()
  XCTAssertTrue(app.webViews.firstMatch.waitForExistence(timeout:10))
  print("AUDIT_R13_MAP_OPEN_PASS")
  print(app.debugDescription)
 }
}
