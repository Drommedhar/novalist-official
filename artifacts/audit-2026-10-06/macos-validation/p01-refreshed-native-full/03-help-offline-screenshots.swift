import XCTest
final class AuditUITests: XCTestCase {
 func testNativeOfflineHelpScreenshotsAfterScroll() {
  continueAfterFailure = false
  let app = XCUIApplication(bundleIdentifier: "com.novalist.app"); app.activate()
  XCTAssertTrue(app.textFields["Search the manual…"].waitForExistence(timeout: 10))
  for (title, alt, name) in [
   ("Dashboard", "The project Dashboard", "offline-native-dashboard"),
   ("Editor", "A scene open in the editor with the Context inspector and scene-notes dock", "offline-native-editor")
  ] {
   app.buttons[title].firstMatch.tap()
   let image = app.images[alt]
   XCTAssertTrue(image.waitForExistence(timeout: 10))
   for _ in 0..<4 {
    if image.isHittable { break }
    app.coordinate(withNormalizedOffset: CGVector(dx: 0.78, dy: 0.82)).press(forDuration: 0.05, thenDragTo: app.coordinate(withNormalizedOffset: CGVector(dx: 0.78, dy: 0.25)))
   }
   XCTAssertTrue(image.isHittable)
   let shot = XCTAttachment(screenshot: XCUIScreen.main.screenshot()); shot.name = name; shot.lifetime = .keepAlways; add(shot)
  }
  XCTAssertFalse(app.alerts["Editing paused"].exists)
  print("P01_NATIVE_OFFLINE_SCREENSHOTS_PASS")
 }
}
