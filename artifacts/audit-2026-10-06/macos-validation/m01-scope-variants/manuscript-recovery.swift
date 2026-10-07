import XCTest
final class AuditUITests:XCTestCase {
 let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
 func navigate(_ target:String) {
  let item=app.scrollViews.staticTexts[target].firstMatch
  if !item.waitForExistence(timeout:1) {app.switches["Toggle sidebar"].tap()}
  XCTAssertTrue(item.waitForExistence(timeout:10));item.tap()
 }
 func testActualManuscriptExactOwnerReopen() {
  continueAfterFailure=false;app.activate();XCUIDevice.shared.orientation = .landscapeLeft
  navigate("Manuscript");XCTAssertTrue(app.buttons["Corkboard"].waitForExistence(timeout:10));let editor=app.textViews.firstMatch
  XCTAssertTrue(editor.waitForExistence(timeout:10));XCTAssertTrue((editor.value as? String ?? "").contains("M01MSCOPE28"))
  navigate("Dashboard");navigate("Manuscript")
  XCTAssertTrue(editor.waitForExistence(timeout:10));XCTAssertTrue((editor.value as? String ?? "").contains("M01MSCOPE28"))
  let shot=XCTAttachment(screenshot:app.screenshot());shot.name="manuscript-exact-scope-reopened";shot.lifetime = .keepAlways;add(shot)
  print("M01_SCOPE_EXACT_OWNER_NATIVE_REOPENED",Date().timeIntervalSince1970)
 }
}
