import XCTest
final class AuditUITests: XCTestCase {
 func testActualSceneImage() {
  continueAfterFailure = false
  let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
  app.activate()
  XCUIDevice.shared.orientation = .portrait
  let book=app.switches["Select Audit Book Alpha — Audit Simulator Fixture"]
  XCTAssertTrue(book.waitForExistence(timeout: 20));book.doubleTap()
  XCTAssertTrue(app.buttons["Write"].waitForExistence(timeout:15));app.buttons["Write"].tap()
  let scene=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@","Audit R13 Frame Image")).firstMatch
  XCTAssertTrue(scene.waitForExistence(timeout:10));scene.tap()
  XCTAssertTrue(app.textViews.firstMatch.waitForExistence(timeout:10))
  print("AUDIT_R13_SCENE_OPEN_PASS")
 }
}
