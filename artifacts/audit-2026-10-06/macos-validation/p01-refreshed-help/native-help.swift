import XCTest
final class AuditUITests: XCTestCase {
 let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
 func capture(_ name:String) {
  Thread.sleep(forTimeInterval:2)
  let shot=XCTAttachment(screenshot:XCUIScreen.main.screenshot())
  shot.name=name;shot.lifetime = .keepAlways;add(shot)
 }
 func testRefreshedHelpImages() {
  continueAfterFailure=false
  app.terminate();app.launch();XCUIDevice.shared.orientation = .landscapeLeft
  let more=app.buttons["More project actions"]
  XCTAssertTrue(more.waitForExistence(timeout:20));more.coordinate(withNormalizedOffset:CGVector(dx:0.5,dy:0.5)).tap()
  let manual=app.buttons["Novalist Manual"]
  if !manual.waitForExistence(timeout:2) {more.coordinate(withNormalizedOffset:CGVector(dx:0.5,dy:0.5)).tap()}
  XCTAssertTrue(manual.waitForExistence(timeout:5));manual.tap()
  XCTAssertTrue(app.textFields["Search the manual…"].waitForExistence(timeout:10))
  app.buttons["Dashboard"].firstMatch.tap()
  let dashboard=app.images["The project Dashboard"]
  XCTAssertTrue(dashboard.waitForExistence(timeout:10));XCTAssertTrue(dashboard.isHittable)
  capture("native-help-dashboard")
  app.buttons["Editor"].firstMatch.tap()
  let editor=app.images["A scene open in the editor with the Context inspector and scene-notes dock"]
  XCTAssertTrue(editor.waitForExistence(timeout:10));XCTAssertTrue(editor.isHittable)
  capture("native-help-editor")
  XCTAssertFalse(app.alerts["Editing paused"].exists)
  print("NATIVE_HELP_ACCESSIBILITY_BEGIN");print(app.debugDescription);print("NATIVE_HELP_ACCESSIBILITY_END")
 }
}
