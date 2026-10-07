import XCTest
final class AuditUITests: XCTestCase {
 let app=XCUIApplication(bundleIdentifier:"com.novalist.app")
 func dismissKeyboard() {if app.buttons["selected"].exists && app.buttons["selected"].isHittable {app.buttons["selected"].tap()}}
 func testCreateIsolatedLockFixture() {
  continueAfterFailure=false;app.terminate();app.launch();XCUIDevice.shared.orientation = .portrait
  let create=app.buttons["New Project"];XCTAssertTrue(create.waitForExistence(timeout:20));create.tap()
  let name=app.textFields["My Novel"];XCTAssertTrue(name.waitForExistence(timeout:10));name.tap();name.typeText("Audit M01 Lock Fixture")
  let book=app.textFields["Book One"];book.tap();book.typeText("Audit M01 Lock Book");dismissKeyboard()
  app.buttons["Create Project"].tap()
  let write=app.buttons["Write"];XCTAssertTrue(write.waitForExistence(timeout:20));write.tap()
  app.buttons["Chapter"].tap()
  let chapter=app.textFields["Enter chapter title..."];XCTAssertTrue(chapter.waitForExistence(timeout:10));chapter.tap();chapter.typeText("Audit Lock Chapter");dismissKeyboard();app.buttons["OK"].tap()
  let scene=app.buttons["Scene"].firstMatch;XCTAssertTrue(scene.waitForExistence(timeout:10));scene.tap()
  let title=app.textFields["Enter scene title..."];XCTAssertTrue(title.waitForExistence(timeout:10));title.tap();title.typeText("Audit M01 Real Lock Scene");dismissKeyboard();app.buttons["OK"].tap()
  let row=app.buttons["Audit M01 Real Lock Scene"];XCTAssertTrue(row.waitForExistence(timeout:10));row.tap()
  XCTAssertTrue(app.textViews.firstMatch.waitForExistence(timeout:10))
  print("M01_LOCK_FIXTURE_CREATED")
 }
}
