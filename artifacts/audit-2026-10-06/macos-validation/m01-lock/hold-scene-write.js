(() => {
  if (window.auditLock) throw new Error('Lock gate already installed');
  const project=window.novalistStores.project.getState();
  const pane=project.activeEditorPaneId, editor=project.editors[pane];
  if (!editor?.sceneId || !editor.chapterGuid || editor.isDirty) throw new Error('Expected empty clean audit scene');
  const state=window.auditLock={pane,chapterGuid:editor.chapterGuid,sceneId:editor.sceneId,scope:{projectPath:project.projectPath,bookId:project.activeBookId,draftId:project.activeDraftId},held:[],otherSceneWritesForwarded:0};
  const send=window.HybridWebView.SendRawMessage.bind(window.HybridWebView);
  window.HybridWebView.SendRawMessage=message=>{
    if (typeof message==='string' && !message.startsWith('{')) {
      const raw=atob(message),split=raw.indexOf('\r\n\r\n');
      const request=JSON.parse(new TextDecoder().decode(Uint8Array.from(raw.slice(split+4),c=>c.charCodeAt(0))));
      if(request.method==='scenes/write') {
        if(request.params[0]===state.chapterGuid && request.params[1]===state.sceneId) {
          state.held.push({id:request.id,chapterGuid:request.params[0],sceneId:request.params[1],marker:request.params[2].includes('M01REALLOCKPENDING26'),htmlLength:request.params[2].length});
          return;
        }
        state.otherSceneWritesForwarded++;
      }
    }
    return send(message);
  };
  return {pane:state.pane,chapterGuid:state.chapterGuid,sceneId:state.sceneId,scope:state.scope};
})()
