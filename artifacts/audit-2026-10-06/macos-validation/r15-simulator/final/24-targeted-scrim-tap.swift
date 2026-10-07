import XCTest
final class AuditUITests: XCTestCase {
 func testTargetedNativeScrimTap() {
  let app = XCUIApplication(bundleIdentifier: "com.novalist.app"); app.activate()
  XCTAssertTrue(app.staticTexts["No matching commands."].waitForExistence(timeout: 5))
  app.coordinate(withNormalizedOffset: CGVector(dx: 0, dy: 0)).withOffset(CGVector(dx: 80, dy: 160)).tap()
  sleep(1)
  print("R15_TARGETED_SCRIM_AFTER\n" + app.debugDescription)
 }
}
