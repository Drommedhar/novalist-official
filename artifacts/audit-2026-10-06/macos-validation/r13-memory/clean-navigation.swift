import XCTest
final class AuditUITests: XCTestCase {
 func testOpenMemoryResearch() {
  continueAfterFailure = false
  let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
  app.activate()
  let book=app.switches["Select Audit Memory Book — Audit Memory Media"]
  XCTAssertTrue(book.waitForExistence(timeout: 20))
  book.doubleTap()
  if app.buttons["Skip"].waitForExistence(timeout: 2) { app.buttons["Skip"].tap() }
  let nav=app.scrollViews.staticTexts["Research"].firstMatch
  if !nav.waitForExistence(timeout: 2) { app.switches["Toggle sidebar"].tap() }
  XCTAssertTrue(nav.waitForExistence(timeout: 10))
  nav.tap()
  XCTAssertTrue(app.buttons["+ Note"].waitForExistence(timeout: 10))
  let preview=app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Audit Memory 1")).firstMatch
  XCTAssertTrue(preview.waitForExistence(timeout: 10))
  preview.tap()
  XCTAssertTrue(app.images["Audit Memory 1"].waitForExistence(timeout: 15))
  let attachment=XCTAttachment(screenshot: app.screenshot())
  attachment.name="clean-memory-preview"
  attachment.lifetime = .keepAlways
  add(attachment)
  let dispose=app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Audit Memory Dispose")).firstMatch
  XCTAssertTrue(dispose.waitForExistence(timeout: 10))
  dispose.tap()
  XCTAssertFalse(app.alerts["Editing paused"].exists)
  print("AUDIT_MEMORY_CLEAN_PREVIEW_DISPOSAL_PASS")
 }
}
