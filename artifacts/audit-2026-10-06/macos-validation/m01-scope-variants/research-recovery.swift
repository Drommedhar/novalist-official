import XCTest
final class AuditUITests:XCTestCase {
 let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
 func navigate(_ target:String) {
  let item=app.scrollViews.staticTexts[target].firstMatch
  if !item.waitForExistence(timeout:1) {app.switches["Toggle sidebar"].tap()}
  XCTAssertTrue(item.waitForExistence(timeout:10));item.tap()
 }
 func testActualResearchExactOwnerReopen() {
  continueAfterFailure=false;app.activate();XCUIDevice.shared.orientation = .landscapeLeft
  navigate("Research");let note=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@","Audit Research Scope Note")).firstMatch;XCTAssertTrue(note.waitForExistence(timeout:10));note.tap();let editor=app.textViews["Content"]
  XCTAssertTrue(editor.waitForExistence(timeout:10));XCTAssertTrue((editor.value as? String ?? "").contains("M01RSCOPE28"))
  navigate("Dashboard");navigate("Research")
  let noteAgain=app.buttons.matching(NSPredicate(format:"label BEGINSWITH %@","Audit Research Scope Note")).firstMatch;XCTAssertTrue(noteAgain.waitForExistence(timeout:10));noteAgain.tap()
  XCTAssertTrue(editor.waitForExistence(timeout:10));XCTAssertTrue((editor.value as? String ?? "").contains("M01RSCOPE28"))
  let shot=XCTAttachment(screenshot:app.screenshot());shot.name="research-exact-scope-reopened";shot.lifetime = .keepAlways;add(shot)
  print("M01_SCOPE_EXACT_OWNER_NATIVE_REOPENED",Date().timeIntervalSince1970)
 }
}
