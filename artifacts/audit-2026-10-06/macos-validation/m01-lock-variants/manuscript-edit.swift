import XCTest
final class AuditUITests:XCTestCase {
 func testActualManuscriptEditForLock() {
  continueAfterFailure=false
  let app=XCUIApplication(bundleIdentifier:"com.novalist.app");app.activate();XCUIDevice.shared.orientation = .landscapeLeft
  XCTAssertTrue(app.buttons["Corkboard"].waitForExistence(timeout:10))
  let editor=app.textViews.firstMatch;XCTAssertTrue(editor.waitForExistence(timeout:10));editor.tap()
  XCTAssertFalse((editor.value as? String ?? "").contains("M01MANUSCRIPTLOCK27"))
  print("M01_VARIANT_GATE_READY",Date().timeIntervalSince1970);Thread.sleep(forTimeInterval:3)
  editor.typeText("M01MANUSCRIPTLOCK27")
  XCTAssertTrue((editor.value as? String ?? "").contains("M01MANUSCRIPTLOCK27"))
  print("M01_VARIANT_EDIT_DONE",Date().timeIntervalSince1970)
 }
}
