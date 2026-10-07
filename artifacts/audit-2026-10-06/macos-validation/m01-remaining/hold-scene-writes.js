(() => {
  if (window.auditHold) throw new Error('Gate already present');
  const state=window.auditHold={writes:[],released:[],replies:[],forwardedReturns:[]};
  const send=window.HybridWebView.SendRawMessage.bind(window.HybridWebView);
  const receive=window.__novalistRecv;
  const pending=[];let buffer='';
  const parse=raw=>JSON.parse(new TextDecoder().decode(Uint8Array.from(raw.slice(raw.indexOf('\r\n\r\n')+4),c=>c.charCodeAt(0))));
  window.HybridWebView.SendRawMessage=message=>{
    if(typeof message==='string'&&!message.startsWith('{')){
      const request=parse(atob(message));
      if(request.method==='scenes/write'){
        state.writes.push({id:request.id,params:request.params});pending.push(message);return;
      }
    }
    return send(message);
  };
  window.__novalistRecv=payload=>{
    buffer+=atob(payload);
    for(;;){
      const split=buffer.indexOf('\r\n\r\n');if(split<0)break;
      const length=/Content-Length:\s*(\d+)/i.exec(buffer.slice(0,split));if(!length)throw new Error('Missing frame length');
      const end=split+4+Number(length[1]);if(buffer.length<end)break;
      const frame=buffer.slice(0,end);buffer=buffer.slice(end);const response=parse(frame);
      if(state.writes.some(w=>w.id===response.id))state.replies.push({id:response.id,hasError:!!response.error,sceneId:response.result?.sceneId,hash:response.result?.hash,conflicted:response.result?.conflicted});
      const returned=receive(btoa(frame));state.forwardedReturns.push(returned);if(returned!=='accepted')throw new Error('Original receiver did not accept frame');
    }
    return 'accepted';
  };
  state.release=index=>{if(!pending[index]||state.released.includes(index))throw new Error('Missing unreleased write');state.released.push(index);return send(pending[index]);};
  return true;
})()
