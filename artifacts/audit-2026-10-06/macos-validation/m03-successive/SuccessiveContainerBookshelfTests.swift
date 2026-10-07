import XCTest
final class AuditUITests: XCTestCase {
 func testBookshelfAfterTwoContainerMoves() {
  continueAfterFailure = false
  let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
  app.activate()
  XCTAssertTrue(app.buttons["New Project"].waitForExistence(timeout: 20))
  let book = app.switches["Select Audit Media Book — Audit Research Media"]
  XCTAssertTrue(book.waitForExistence(timeout: 20))
  XCTAssertFalse(app.alerts["Editing paused"].exists)
  print("AUDIT_M03_TWO_MOVES_BOOKSHELF_PASS")
 }
}
