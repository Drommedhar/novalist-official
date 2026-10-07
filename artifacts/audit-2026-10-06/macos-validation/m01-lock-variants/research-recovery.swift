import XCTest
final class AuditUITests:XCTestCase {
 let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
 func navigate(_ target:String) {
  let item=app.scrollViews.staticTexts[target].firstMatch
  if !item.waitForExistence(timeout:1) {app.switches["Toggle sidebar"].tap()}
  XCTAssertTrue(item.waitForExistence(timeout:10));item.tap()
 }
 func reopenResearch() {
  navigate("Research")
  let note=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@","Audit Research Lock Note")).firstMatch
  XCTAssertTrue(note.waitForExistence(timeout:10));note.tap()
  let editor=app.textViews["Content"];XCTAssertTrue(editor.waitForExistence(timeout:10));XCTAssertTrue((editor.value as? String ?? "").contains("M01RESEARCHLOCK27"))
 }
 func testRecoverExactLockedResearchNote() {
  continueAfterFailure=false;app.launch();XCUIDevice.shared.orientation = .landscapeLeft
  let book=app.switches["Select Audit Lock Variants Book — Audit M01 Lock Variants"];XCTAssertTrue(book.waitForExistence(timeout:20));book.doubleTap()
  reopenResearch();print("M01_RESEARCH_LOCK_RECOVERED",Date().timeIntervalSince1970)
  navigate("Manuscript");XCTAssertTrue(app.buttons["Corkboard"].waitForExistence(timeout:10))
  reopenResearch()
  let shot=XCTAttachment(screenshot:app.screenshot());shot.name="research-lock-recovered-exact-note";shot.lifetime = .keepAlways;add(shot)
  print("M01_RESEARCH_LOCK_EXACT_NOTE_REOPENED",Date().timeIntervalSince1970)
 }
}
