import XCTest
final class AuditUITests: XCTestCase {
 func testActualMap3D() {
  continueAfterFailure = false
  let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
  app.activate()
  let enter=app.buttons["3D"]
  XCTAssertTrue(enter.waitForExistence(timeout:10));enter.tap()
  print("AUDIT_R13_NATIVE_3D_TAP")
  sleep(8)
  print(app.debugDescription)
 }
}
