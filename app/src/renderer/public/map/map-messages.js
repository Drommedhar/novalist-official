export function sendMessage(msg) {
    try {
        const json = JSON.stringify(msg);
        if (typeof window.invokeCSharpAction === 'function') window.invokeCSharpAction(json);
        else if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(json);
        else if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.webview)
            window.webkit.messageHandlers.webview.postMessage(json);
        else if (window.parent && window.parent !== window)
            window.parent.postMessage({ novalistMap: json }, '*');
    } catch (e) { console.error(e); }
}

export function log(msg) {
    console.log('[Map]', msg);
    sendMessage({ type: 'log', text: String(msg) });
}

export function initializeMapMessages() {
    window.addEventListener('error', e => log('window.error: ' + e.message + ' @ ' + e.filename + ':' + e.lineno + (e.error && e.error.stack ? '\n' + e.error.stack : '')));
    window.addEventListener('unhandledrejection', e => {
        const r = e && e.reason;
        log('unhandledrejection: ' + (r && r.stack ? r.stack : (r && r.message ? r.message : String(r))));
    });
}
