import XCTest
final class AuditUITests: XCTestCase {
 func testInspectCleanLandscapeAfterDismiss() {
  let app = XCUIApplication(bundleIdentifier: "com.novalist.app"); app.activate()
  print("R15_LANDSCAPE_AFTER_DISMISS\n" + app.debugDescription)
  let shot = XCTAttachment(screenshot: XCUIScreen.main.screenshot()); shot.name = "landscape-after-dismiss"; shot.lifetime = .keepAlways; add(shot)
 }
}
