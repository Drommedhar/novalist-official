import XCTest
final class AuditUITests: XCTestCase {
 func testRecoverExactLockedScene() {
  continueAfterFailure=false
  let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
  app.launch();XCUIDevice.shared.orientation = .portrait
  let book=app.switches["Select Audit M01 Lock Book — Audit M01 Lock Fixture"]
  XCTAssertTrue(book.waitForExistence(timeout:20));book.doubleTap()
  XCTAssertTrue(app.buttons["Write"].waitForExistence(timeout:20));app.buttons["Write"].tap()
  let editor=app.textViews.firstMatch
  XCTAssertTrue(editor.waitForExistence(timeout:15))
  XCTAssertTrue((editor.value as? String ?? "").contains("M01REALLOCKPENDING26"))
  print("M01_REAL_LOCK_RECOVERED",Date().timeIntervalSince1970)
  app.buttons["Manuscript"].tap()
  let scene=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@","Audit M01 Real Lock Scene")).firstMatch
  XCTAssertTrue(scene.waitForExistence(timeout:10));scene.tap()
  XCTAssertTrue(editor.waitForExistence(timeout:10));XCTAssertTrue((editor.value as? String ?? "").contains("M01REALLOCKPENDING26"))
  let shot=XCTAttachment(screenshot:XCUIScreen.main.screenshot());shot.name="real-lock-recovered-exact-scene";shot.lifetime = .keepAlways;add(shot)
  print("M01_REAL_LOCK_EXACT_SCENE_REOPENED",Date().timeIntervalSince1970)
 }
}
