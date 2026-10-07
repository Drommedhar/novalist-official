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
  print("AUDIT_MEMORY_RESEARCH_NAVIGATION_PASS")
 }
}
