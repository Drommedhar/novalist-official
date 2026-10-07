import UIKit
import WebKit

final class Controller: UIViewController, WKNavigationDelegate {
    let web = WKWebView(frame: .zero)
    let directory = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0]
    var iteration = 0
    var accepted = 0
    var errors = 0
    var polling: Timer?
    override func viewDidLoad() {
        super.viewDidLoad()
        web.frame = view.bounds
        web.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        web.navigationDelegate = self
        view.addSubview(web)
        web.loadHTMLString("<html><body>Pure WebKit synthetic evaluation<script>window.auditReceive=function(payload){return 'accepted'}</script></body></html>", baseURL: nil)
    }
    func record(_ extra: [String: Any] = [:]) {
        var value: [String: Any] = ["iterations":iteration,"pid":ProcessInfo.processInfo.processIdentifier,"ready":true,"accepted":accepted,"errors":errors]
        extra.forEach { value[$0.key] = $0.value }
        if let data = try? JSONSerialization.data(withJSONObject:value,options:.sortedKeys) {
            try? data.write(to:directory.appendingPathComponent("state.json"),options:.atomic)
        }
    }
    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        record()
        polling = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in self?.checkCommand() }
    }
    func checkCommand() {
        let url=directory.appendingPathComponent("command.json")
        guard let data=try? Data(contentsOf:url), let object=try? JSONSerialization.jsonObject(with:data) as? [String:Any] else {return}
        try? FileManager.default.removeItem(at:url)
        let count=object["count"] as? Int ?? 12
        let size=object["size"] as? Int ?? 10_190_000
        evaluate(count:count,size:size)
    }
    func evaluate(count:Int,size:Int) {
        guard count>0 else {record(["finished":true]);return}
        let script="window.auditReceive('" + String(repeating:"a",count:size) + String(iteration) + "')"
        web.evaluateJavaScript(script) { [weak self] result,error in
            guard let self=self else{return}
            self.iteration += 1
            if result as? String == "accepted" {self.accepted += 1}
            if error != nil {self.errors += 1}
            self.record(["accepted":result as? String == "accepted","error":error != nil])
            DispatchQueue.main.asyncAfter(deadline:.now()+0.05) { self.evaluate(count:count-1,size:size) }
        }
    }
}
final class SceneDelegate: UIResponder, UIWindowSceneDelegate {
    var window: UIWindow?
    func scene(_ scene:UIScene,willConnectTo session:UISceneSession,options connectionOptions:UIScene.ConnectionOptions) {
        guard let windowScene=scene as? UIWindowScene else{return}
        let win=UIWindow(windowScene:windowScene)
        win.rootViewController=Controller()
        win.makeKeyAndVisible()
        window=win
    }
}
final class AppDelegate: UIResponder, UIApplicationDelegate {
    func application(_ application:UIApplication,configurationForConnecting session:UISceneSession,options:UIScene.ConnectionOptions) -> UISceneConfiguration {
        let config=UISceneConfiguration(name:"Audit",sessionRole:session.role)
        config.delegateClass=SceneDelegate.self
        return config
    }
}
UIApplicationMain(CommandLine.argc,CommandLine.unsafeArgv,nil,NSStringFromClass(AppDelegate.self))
