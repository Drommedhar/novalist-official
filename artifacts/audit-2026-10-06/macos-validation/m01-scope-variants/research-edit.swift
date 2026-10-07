import XCTest
final class AuditUITests:XCTestCase {
 let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
 func navigate(_ target:String) {
  let item=app.scrollViews.staticTexts[target].firstMatch
  if !item.waitForExistence(timeout:1) {app.switches["Toggle sidebar"].tap()}
  XCTAssertTrue(item.waitForExistence(timeout:10));item.tap()
 }
 func testActualResearchScopeEdit() {
  continueAfterFailure=false;app.activate();XCUIDevice.shared.orientation = .landscapeLeft
  navigate("Research");let note=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@","Audit Research Scope Note")).firstMatch;XCTAssertTrue(note.waitForExistence(timeout:10));note.tap();let editor=app.textViews["Content"]
  XCTAssertTrue(editor.waitForExistence(timeout:10));editor.tap();XCTAssertFalse((editor.value as? String ?? "").contains("M01RSCOPE28"))
  print("M01_SCOPE_GATE_READY",Date().timeIntervalSince1970);Thread.sleep(forTimeInterval:3)
  editor.typeText("M01RSCOPE28");XCTAssertTrue((editor.value as? String ?? "").contains("M01RSCOPE28"))
  print("M01_SCOPE_EDIT_DONE",Date().timeIntervalSince1970)
 }
}
