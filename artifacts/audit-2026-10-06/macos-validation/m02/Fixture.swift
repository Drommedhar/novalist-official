import UIKit
@main
final class FixtureApp: UIResponder, UIApplicationDelegate {
    func application(_ application: UIApplication, configurationForConnecting session: UISceneSession, options: UIScene.ConnectionOptions) -> UISceneConfiguration {
        let config = UISceneConfiguration(name: "Fixture", sessionRole: session.role)
        config.delegateClass = FixtureScene.self
        return config
    }
}
final class FixtureScene: UIResponder, UIWindowSceneDelegate {
    var window: UIWindow?
    func scene(_ scene: UIScene, willConnectTo session: UISceneSession, options: UIScene.ConnectionOptions) {
        guard let scene = scene as? UIWindowScene else { return }
        let documents = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0]
        try! FileManager.default.createDirectory(at: documents, withIntermediateDirectories: true)
        try! "Synthetic M02 manuscript fixture.\n".write(to: documents.appendingPathComponent("manuscript.txt"), atomically: true, encoding: .utf8)
        if !FileManager.default.fileExists(atPath: documents.appendingPathComponent("MovedParent").path) {
        for child in ["SiblingA", "SiblingB"] {
            try! FileManager.default.createDirectory(at: documents.appendingPathComponent("FixtureParent/" + child), withIntermediateDirectories: true)
        }
        }
        let controller = UIViewController()
        controller.view.backgroundColor = .systemBackground
        let label = UILabel(frame: CGRect(x: 24, y: 140, width: 340, height: 120))
        label.text = "Synthetic audit fixtures in Files"
        label.numberOfLines = 0
        controller.view.addSubview(label)
        let button = UIButton(type: .system)
        button.frame = CGRect(x: 24, y: 280, width: 340, height: 60)
        button.setTitle("Move synthetic parent", for: .normal)
        button.addAction(UIAction { _ in
            let original = documents.appendingPathComponent("FixtureParent")
            let destination = documents.appendingPathComponent("MovedParent")
            let sourceURL = FileManager.default.fileExists(atPath: original.path) ? original : destination
            let targetURL = sourceURL == original ? destination : original
            var coordinationError: NSError?
            NSFileCoordinator().coordinate(writingItemAt: sourceURL, options: .forMoving, writingItemAt: targetURL, options: .forReplacing, error: &coordinationError) { source, target in
                do {
                    try FileManager.default.moveItem(at: source, to: target)
                    label.text = "Synthetic parent moved"
                } catch { label.text = "Move failed: " + String(describing: type(of: error)) }
            }
            if coordinationError != nil { label.text = "Coordination failed" }
        }, for: .touchUpInside)
        controller.view.addSubview(button)
        window = UIWindow(windowScene: scene)
        window?.rootViewController = controller
        window?.makeKeyAndVisible()
    }
}
