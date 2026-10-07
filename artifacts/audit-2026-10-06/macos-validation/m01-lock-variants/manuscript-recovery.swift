import XCTest
final class AuditUITests:XCTestCase {
 let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
 let marker="M01MANUSCRIPTLOCK27"
 func navigate(_ target:String) {
  let item=app.scrollViews.staticTexts[target].firstMatch
  if !item.waitForExistence(timeout:1) {app.switches["Toggle sidebar"].tap()}
  XCTAssertTrue(item.waitForExistence(timeout:10));item.tap()
 }
 func assertMarker() {
  let editor=app.textViews.matching(NSPredicate(format:"value CONTAINS %@",marker)).firstMatch
  XCTAssertTrue(editor.waitForExistence(timeout:15));XCTAssertTrue((editor.value as? String ?? "").contains(marker))
 }
 func testRecoverExactLockedManuscriptScene() {
  continueAfterFailure=false;app.launch();XCUIDevice.shared.orientation = .landscapeLeft
  let book=app.switches["Select Audit Lock Variants Book — Audit M01 Lock Variants"];XCTAssertTrue(book.waitForExistence(timeout:20));book.doubleTap()
  navigate("Manuscript");XCTAssertTrue(app.buttons["Corkboard"].waitForExistence(timeout:10));assertMarker()
  print("M01_MANUSCRIPT_LOCK_RECOVERED",Date().timeIntervalSince1970)
  navigate("Editor")
  let row=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@ AND NOT label CONTAINS %@","Audit Manuscript Lock Scene","Close tab")).firstMatch
  if !row.waitForExistence(timeout:1) {app.switches["Toggle binder"].tap()}
  XCTAssertTrue(row.waitForExistence(timeout:10));row.tap();assertMarker()
  navigate("Manuscript");XCTAssertTrue(app.buttons["Corkboard"].waitForExistence(timeout:10));assertMarker()
  let shot=XCTAttachment(screenshot:app.screenshot());shot.name="manuscript-lock-recovered-exact-scene";shot.lifetime = .keepAlways;add(shot)
  print("M01_MANUSCRIPT_LOCK_EXACT_SCENE_REOPENED",Date().timeIntervalSince1970)
 }
}
