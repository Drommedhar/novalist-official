import XCTest
final class AuditUITests: XCTestCase {
 func testInspectCleanTabOrder() {
  let app = XCUIApplication(bundleIdentifier: "com.novalist.app"); app.activate()
  for step in 0..<10 {
   print("R15_TAB_DIAGNOSTIC_STEP_\(step)\n" + app.debugDescription)
   app.typeKey(XCUIKeyboardKey.tab, modifierFlags: [.shift])
  }
 }
}
