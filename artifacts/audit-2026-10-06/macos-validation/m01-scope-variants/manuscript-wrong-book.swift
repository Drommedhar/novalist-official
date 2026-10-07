import XCTest
final class AuditUITests:XCTestCase {
 let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
 func navigate(_ target:String) {
  let item=app.scrollViews.staticTexts[target].firstMatch
  if !item.waitForExistence(timeout:1) {app.switches["Toggle sidebar"].tap()}
  XCTAssertTrue(item.waitForExistence(timeout:10));item.tap()
 }
 func testOpenWrongBookForManuscriptRecovery() {
  continueAfterFailure=false;app.launch();XCUIDevice.shared.orientation = .landscapeLeft
  let book=app.switches["Select Audit Scope Book B — Audit M01 Scope Variants"];XCTAssertTrue(book.waitForExistence(timeout:20));book.doubleTap()
  navigate("Manuscript");XCTAssertTrue(app.buttons["Corkboard"].waitForExistence(timeout:10));let editor=app.textViews.firstMatch
  XCTAssertTrue(editor.waitForExistence(timeout:10));XCTAssertFalse((editor.value as? String ?? "").contains("M01MSCOPE28"))
  print("M01_SCOPE_WRONG_BOOK_NATIVE_READY",Date().timeIntervalSince1970)
 }
}
