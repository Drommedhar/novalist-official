import XCTest
final class AuditUITests:XCTestCase {
 let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
 func navigate(_ target:String) {
  let item=app.scrollViews.staticTexts[target].firstMatch
  if !item.waitForExistence(timeout:1) {app.switches["Toggle sidebar"].tap()}
  XCTAssertTrue(item.waitForExistence(timeout:10));item.tap()
 }
 func testActualManuscriptScopeEdit() {
  continueAfterFailure=false;app.launch();XCUIDevice.shared.orientation = .landscapeLeft
  let book=app.switches["Select Audit Scope Book A — Audit M01 Scope Variants"];XCTAssertTrue(book.waitForExistence(timeout:20));book.doubleTap()
  navigate("Manuscript");XCTAssertTrue(app.buttons["Corkboard"].waitForExistence(timeout:10));let editor=app.textViews.firstMatch
  XCTAssertTrue(editor.waitForExistence(timeout:10));editor.tap();XCTAssertFalse((editor.value as? String ?? "").contains("M01MSCOPE28"))
  print("M01_SCOPE_GATE_READY",Date().timeIntervalSince1970);Thread.sleep(forTimeInterval:3)
  editor.typeText("M01MSCOPE28");XCTAssertTrue((editor.value as? String ?? "").contains("M01MSCOPE28"))
  print("M01_SCOPE_EDIT_DONE",Date().timeIntervalSince1970)
 }
}
