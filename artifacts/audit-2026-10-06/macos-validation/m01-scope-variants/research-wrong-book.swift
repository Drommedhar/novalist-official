import XCTest
final class AuditUITests:XCTestCase {
 let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
 func navigate(_ target:String) {
  let item=app.scrollViews.staticTexts[target].firstMatch
  if !item.waitForExistence(timeout:1) {app.switches["Toggle sidebar"].tap()}
  XCTAssertTrue(item.waitForExistence(timeout:10));item.tap()
 }
 func testOpenWrongBookForResearchRecovery() {
  continueAfterFailure=false;app.launch();XCUIDevice.shared.orientation = .landscapeLeft
  let book=app.switches["Select Audit Scope Book B — Audit M01 Scope Variants"];XCTAssertTrue(book.waitForExistence(timeout:20));book.doubleTap()
  navigate("Research");let note=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@","Audit Research Scope Note")).firstMatch;XCTAssertTrue(note.waitForExistence(timeout:10));note.tap();let editor=app.textViews["Content"]
  XCTAssertTrue(editor.waitForExistence(timeout:10));XCTAssertFalse((editor.value as? String ?? "").contains("M01RSCOPE28"))
  print("M01_SCOPE_WRONG_BOOK_NATIVE_READY",Date().timeIntervalSince1970)
 }
}
