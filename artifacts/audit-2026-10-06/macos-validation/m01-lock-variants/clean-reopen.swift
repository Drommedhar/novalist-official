import XCTest
final class AuditUITests:XCTestCase {
 let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
 func navigate(_ target:String) {
  let item=app.scrollViews.staticTexts[target].firstMatch
  if !item.waitForExistence(timeout:1) {app.switches["Toggle sidebar"].tap()}
  XCTAssertTrue(item.waitForExistence(timeout:10));item.tap()
 }
 func testCleanAppRetainsBothLockedMarkers() {
  continueAfterFailure=false;app.terminate();app.launch();XCUIDevice.shared.orientation = .landscapeLeft
  let book=app.switches["Select Audit Lock Variants Book — Audit M01 Lock Variants"];XCTAssertTrue(book.waitForExistence(timeout:20));book.doubleTap()
  navigate("Manuscript");XCTAssertTrue(app.buttons["Corkboard"].waitForExistence(timeout:10))
  let manuscript=app.textViews.matching(NSPredicate(format:"value CONTAINS %@","M01MANUSCRIPTLOCK27")).firstMatch
  XCTAssertTrue(manuscript.waitForExistence(timeout:15))
  print("M01_CLEAN_MANUSCRIPT_READY",Date().timeIntervalSince1970);Thread.sleep(forTimeInterval:2)
  let first=XCTAttachment(screenshot:app.screenshot());first.name="clean-manuscript-lock-marker";first.lifetime = .keepAlways;add(first)
  navigate("Research")
  let note=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@","Audit Research Lock Note")).firstMatch
  XCTAssertTrue(note.waitForExistence(timeout:10));note.tap()
  let research=app.textViews["Content"];XCTAssertTrue(research.waitForExistence(timeout:10));XCTAssertTrue((research.value as? String ?? "").contains("M01RESEARCHLOCK27"))
  XCTAssertFalse(app.staticTexts["Editing paused"].exists)
  print("M01_CLEAN_RESEARCH_READY",Date().timeIntervalSince1970);Thread.sleep(forTimeInterval:2)
  let second=XCTAttachment(screenshot:app.screenshot());second.name="clean-research-lock-marker";second.lifetime = .keepAlways;add(second)
  print("M01_CLEAN_BOTH_LOCK_VARIANTS_REOPENED",Date().timeIntervalSince1970)
 }
}
