import XCTest
final class AuditUITests: XCTestCase {
 func testOpenActualResearch() {
  continueAfterFailure = false
  let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
  app.activate()
  XCUIDevice.shared.orientation = .landscapeLeft
  if app.buttons["Close tour"].waitForExistence(timeout: 2) { app.buttons["Close tour"].tap() }
  let book=app.switches["Select Audit Media Book — Audit Research Media"]
  if book.exists { book.doubleTap() }
  let nav = app.scrollViews.staticTexts["Research"].firstMatch
  if !nav.waitForExistence(timeout: 3) { app.switches["Toggle sidebar"].tap() }
  XCTAssertTrue(nav.waitForExistence(timeout: 10))
  nav.tap()
  XCTAssertTrue(app.buttons["+ Note"].waitForExistence(timeout: 10))
  print("AUDIT_R13_REAL_RESEARCH_OPEN_PASS")
 }
}
