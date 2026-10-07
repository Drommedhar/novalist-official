(() => {
  const expected = __CONFIG__;
  if (window.auditLockVariant) throw new Error('Variant gate already installed');
  const project = window.novalistStores.project.getState();
  const scope = {projectPath:project.projectPath,bookId:project.activeBookId,draftId:project.activeDraftId};
  if (!scope.projectPath.endsWith('/Audit M01 Lock Variants') || scope.bookId !== expected.bookId || scope.draftId !== expected.draftId) throw new Error('Wrong synthetic fixture scope');
  const state = window.auditLockVariant = {expected,scope,held:[],editingClaims:[],otherWritesForwarded:0};
  const send = window.HybridWebView.SendRawMessage.bind(window.HybridWebView);
  window.HybridWebView.SendRawMessage = message => {
    if (typeof message === 'string' && !message.startsWith('{')) {
      const raw = atob(message), split = raw.indexOf('\r\n\r\n');
      const request = JSON.parse(new TextDecoder().decode(Uint8Array.from(raw.slice(split+4), c=>c.charCodeAt(0))));
      if (request.method === 'scenes/setEditing') state.editingClaims.push({chapterGuid:request.params[0],sceneId:request.params[1],dirty:request.params[2],at:Date.now()});
      if (request.method === 'scenes/setEditingMany') state.editingClaims.push(...request.params[0].map(e=>({...e,at:Date.now()})));
      if (request.method === 'scenes/write' || request.method === 'research/save') {
        const owns = expected.kind === 'manuscript'
          ? request.method === 'scenes/write' && request.params[0] === expected.chapterGuid && request.params[1] === expected.sceneId
          : request.method === 'research/save' && request.params[0] === expected.researchId;
        if (owns) {
          const content = request.params[expected.kind === 'manuscript' ? 2 : 3];
          state.held.push({id:request.id,method:request.method,chapterGuid:expected.chapterGuid,sceneId:expected.sceneId,researchId:expected.researchId,marker:content.includes(expected.marker),contentLength:content.length,at:Date.now()});
          return;
        }
        state.otherWritesForwarded++;
      }
    }
    return send(message);
  };
  return {expected,scope};
})()
