import XCTest
final class AuditUITests:XCTestCase {
 let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
 func hideKeyboard() {if app.buttons["Hide keyboard"].exists && app.buttons["Hide keyboard"].isHittable {app.buttons["Hide keyboard"].tap()}}
 func navigate(_ target:String) {
  let item=app.scrollViews.staticTexts[target].firstMatch
  if !item.waitForExistence(timeout:1) {app.switches["Toggle sidebar"].tap()}
  XCTAssertTrue(item.waitForExistence(timeout:10));item.tap()
 }
 func testCreateIsolatedVariantFixture() {
  continueAfterFailure=false;app.activate();XCUIDevice.shared.orientation = .landscapeLeft
  let create=app.buttons["New Project"];XCTAssertTrue(create.waitForExistence(timeout:20));create.tap()
  let name=app.textFields["My Novel"];XCTAssertTrue(name.waitForExistence(timeout:10));name.tap();name.typeText("Audit M01 Scope Variants")
  let book=app.textFields["Book One"];book.tap();book.typeText("Audit Scope Book A");hideKeyboard();app.buttons["Create Project"].tap()
  navigate("Editor")
  if !app.buttons["Chapter"].firstMatch.waitForExistence(timeout:1) {app.switches["Toggle binder"].tap()}
  XCTAssertTrue(app.buttons["Chapter"].firstMatch.waitForExistence(timeout:10));app.buttons["Chapter"].firstMatch.tap()
  let chapter=app.textFields["Enter chapter title..."];XCTAssertTrue(chapter.waitForExistence(timeout:10));chapter.tap();chapter.typeText("Audit Scope Chapter");hideKeyboard();app.buttons["OK"].tap()
  let scene=app.buttons["Scene"].firstMatch;XCTAssertTrue(scene.waitForExistence(timeout:10));scene.tap()
  let title=app.textFields["Enter scene title..."];XCTAssertTrue(title.waitForExistence(timeout:10));title.tap();title.typeText("Audit Manuscript Scope Scene");hideKeyboard();app.buttons["OK"].tap()
  XCTAssertTrue(app.buttons["Audit Manuscript Scope Scene"].waitForExistence(timeout:10))
  navigate("Research")
  XCTAssertTrue(app.buttons["+ Note"].waitForExistence(timeout:10));app.buttons["+ Note"].tap()
  let note=app.textFields["Title"];XCTAssertTrue(note.waitForExistence(timeout:10));note.tap();note.typeText("Audit Research Scope Note")
  let content=app.textViews["Content"];XCTAssertTrue(content.waitForExistence(timeout:10));content.tap();hideKeyboard()
  Thread.sleep(forTimeInterval:1)
  navigate("Manuscript")
  XCTAssertTrue(app.buttons["Corkboard"].waitForExistence(timeout:10));XCTAssertTrue(app.textViews.firstMatch.waitForExistence(timeout:10))
  print("M01_SCOPE_FIXTURE_CREATED",Date().timeIntervalSince1970)
 }
}
