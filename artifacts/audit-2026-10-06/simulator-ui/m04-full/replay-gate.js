(() => {
  if (window.auditReplay) throw new Error('Gate already installed');
  const state = window.auditReplay = { taskRequestCount: 0, taskReplyHeld: 0, taskId: null, backendRejected: 0 };
  const send = window.HybridWebView.SendRawMessage.bind(window.HybridWebView);
  window.HybridWebView.SendRawMessage = (message) => {
    if (typeof message === 'string' && !message.startsWith('{')) {
      const raw = atob(message);
      const header = raw.indexOf('\r\n\r\n');
      if (header >= 0) {
        const body = new TextDecoder().decode(Uint8Array.from(raw.slice(header + 4), c => c.charCodeAt(0)));
        const request = JSON.parse(body);
        if (request.method === 'tasks/save') { state.taskRequestCount++; state.taskId = request.id; }
      }
    }
    return send(message);
  };
  const receive = window.__novalistRecv;
  let buffer = '';
  window.__novalistRecv = (payload) => {
    buffer += atob(payload);
    for (;;) {
      const header = buffer.indexOf('\r\n\r\n');
      if (header < 0) break;
      const size = /Content-Length:\s*(\d+)/i.exec(buffer.slice(0, header));
      if (!size) throw new Error('Missing frame length');
      const end = header + 4 + Number(size[1]);
      if (buffer.length < end) break;
      const frame = buffer.slice(0, end);
      buffer = buffer.slice(end);
      const body = new TextDecoder().decode(Uint8Array.from(frame.slice(header + 4), c => c.charCodeAt(0)));
      const response = JSON.parse(body);
      if (response.id === state.taskId && response.method === undefined && state.taskId !== null) {
        state.taskReplyHeld++;
      } else receive(btoa(frame));
    }
    return 'accepted';
  };
  window.addEventListener('unhandledrejection', event => {
    if (event.reason?.message === 'Backend restarted.') state.backendRejected++;
  });
  return true;
})()
