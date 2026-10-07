import XCTest
final class AuditUITests: XCTestCase {
 func testClearMapHints() {
  continueAfterFailure = false
  let app = XCUIApplication(bundleIdentifier: "com.novalist.app")
  app.activate()
  if app.buttons["Got it"].exists { app.buttons["Got it"].tap() }
  let sky=app.buttons.matching(NSPredicate(format:"label CONTAINS %@","Sky")).firstMatch
  if sky.exists { sky.tap() }
  print("AUDIT_R13_MAP_HINTS_CLEARED")
 }
}
