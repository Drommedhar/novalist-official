import XCTest
final class AuditUITests: XCTestCase {
 func testFinalCleanNativeMap3D() {
  continueAfterFailure = false
  let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
  app.activate()
  XCUIDevice.shared.orientation = .landscapeLeft
  let book=app.switches["Select Audit Book Alpha — Audit Simulator Fixture"]
  XCTAssertTrue(book.waitForExistence(timeout:20));book.doubleTap()
  let nav=app.scrollViews.staticTexts["Maps"].firstMatch
  if !nav.waitForExistence(timeout:3) { app.switches["Toggle sidebar"].tap() }
  XCTAssertTrue(nav.waitForExistence(timeout:10));nav.tap()
  let enter=app.buttons["3D"]
  XCTAssertTrue(enter.waitForExistence(timeout:10));enter.tap()
  sleep(5)
  let sky=app.buttons.matching(NSPredicate(format:"label CONTAINS %@","Sky")).firstMatch
  XCTAssertTrue(sky.waitForExistence(timeout:10));sky.tap()
  print("AUDIT_R13_FINAL_CLEAN_NATIVE_3D_OPEN_PASS")
 }
}
