import XCTest
final class AuditUITests: XCTestCase {
 func testActualPendingEditForLock() {
  continueAfterFailure=false
  let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
  app.activate();XCUIDevice.shared.orientation = .portrait
  let book=app.switches["Select Audit M01 Lock Book — Audit M01 Lock Fixture"]
  XCTAssertTrue(book.waitForExistence(timeout:20));book.doubleTap()
  XCTAssertTrue(app.buttons["Write"].waitForExistence(timeout:20));app.buttons["Write"].tap()
  let row=app.buttons["Audit M01 Real Lock Scene"];XCTAssertTrue(row.waitForExistence(timeout:10));row.tap()
  let editor=app.textViews.firstMatch;XCTAssertTrue(editor.waitForExistence(timeout:10));editor.tap()
  XCTAssertFalse((editor.value as? String ?? "").contains("M01REALLOCKPENDING26"))
  print("M01_PENDING_GATE_READY",Date().timeIntervalSince1970);Thread.sleep(forTimeInterval:3)
  editor.typeText("M01REALLOCKPENDING26")
  XCTAssertTrue((editor.value as? String ?? "").contains("M01REALLOCKPENDING26"))
  print("M01_PENDING_EDIT_DONE",Date().timeIntervalSince1970)
 }
}
