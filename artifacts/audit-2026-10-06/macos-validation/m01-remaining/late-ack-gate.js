(() => {
  if (window.auditM01) throw new Error('M01 gate already exists');
  const state = window.auditM01 = { writes: [], firstId: null, repliesHeld: 0, writesHeld: 0, firstReplyReleased: false };
  const send = window.HybridWebView.SendRawMessage.bind(window.HybridWebView);
  const receive = window.__novalistRecv;
  let responseBuffer = '', heldReply = null;
  const parse = raw => {
    const split = raw.indexOf('\r\n\r\n');
    return JSON.parse(new TextDecoder().decode(Uint8Array.from(raw.slice(split + 4), c => c.charCodeAt(0))));
  };
  window.HybridWebView.SendRawMessage = message => {
    if (typeof message === 'string' && !message.startsWith('{')) {
      const request = parse(atob(message));
      if (request.method === 'scenes/write') {
        state.writes.push({ id: request.id, params: request.params });
        if (state.firstId === null) state.firstId = request.id;
        else { state.writesHeld++; return; }
      }
    }
    return send(message);
  };
  window.__novalistRecv = payload => {
    responseBuffer += atob(payload);
    for (;;) {
      const split = responseBuffer.indexOf('\r\n\r\n');
      if (split < 0) break;
      const length = /Content-Length:\s*(\d+)/i.exec(responseBuffer.slice(0, split));
      if (!length) throw new Error('Missing response frame length');
      const end = split + 4 + Number(length[1]);
      if (responseBuffer.length < end) break;
      const frame = responseBuffer.slice(0, end);
      responseBuffer = responseBuffer.slice(end);
      const response = parse(frame);
      if (response.id === state.firstId && state.firstId !== null && !response.method) {
        heldReply = frame; state.repliesHeld++;
      } else receive(btoa(frame));
    }
    return 'accepted';
  };
  state.releaseFirst = () => {
    if (!heldReply || state.firstReplyReleased) throw new Error('No unreleased first reply');
    state.firstReplyReleased = true;
    receive(btoa(heldReply)); heldReply = null;
    return true;
  };
  return true;
})()
