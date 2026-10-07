(() => {
  const policyText = "default-src 'self' data: blob:; connect-src 'none'; img-src 'self' data: blob:; media-src 'self' data: blob:; font-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; frame-src 'self' data: blob:; worker-src 'self' blob:";
  const state = window.auditHelp = {status:'running', location:{protocol:location.protocol,origin:location.origin}, policy:policyText, pages:[], images:{}, networkControls:[], violations:[], errors:[]};
  document.addEventListener('securitypolicyviolation', e => state.violations.push({directive:e.effectiveDirective,uri:e.blockedURI,disposition:e.disposition}));
  window.addEventListener('error',e=>state.errors.push({kind:'error',message:e.message}));
  window.addEventListener('unhandledrejection',e=>state.errors.push({kind:'rejection',message:String(e.reason)}));
  const policy = document.createElement('meta'); policy.httpEquiv='Content-Security-Policy';policy.content=policyText;document.head.append(policy);
  const pause = ms => new Promise(resolve=>setTimeout(resolve,ms));
  const bounded = (promise,label,ms=5000) => Promise.race([promise,new Promise((_,reject)=>setTimeout(()=>reject(new Error(label+' timed out')),ms))]);
  void (async()=>{
    try {
      for(const scheme of ['http','https']) {
        const fetchUrl=scheme+'://example.invalid/novalist-audit-fetch';let rejected=false;
        try {await bounded(fetch(fetchUrl),'external fetch');} catch(error){rejected=error instanceof TypeError;}
        const imageUrl=scheme+'://example.invalid/novalist-audit-image.png';
        const imageRejected=await bounded(new Promise(resolve=>{const image=new Image();image.onload=()=>resolve(false);image.onerror=()=>resolve(true);image.src=imageUrl;}),'external image');
        await pause(50);
        const fetchViolation=state.violations.some(v=>v.directive==='connect-src'&&[fetchUrl,scheme+'://example.invalid',scheme+'://example.invalid/'].includes(v.uri)&&v.disposition==='enforce');
        const imageViolation=state.violations.some(v=>v.directive==='img-src'&&[imageUrl,scheme+'://example.invalid',scheme+'://example.invalid/'].includes(v.uri)&&v.disposition==='enforce');
        state.networkControls.push({scheme,fetchRejected:rejected,fetchEnforcedViolation:fetchViolation,imageRejected,imageEnforcedViolation:imageViolation});
        if(!rejected||!fetchViolation||!imageRejected||!imageViolation)throw new Error('Network blocking was not proved for '+scheme);
      }
      const initialButtons=[...document.querySelectorAll('.help-page-item')];
      if(initialButtons.length!==47)throw new Error('Expected 47 actual Help page buttons, saw '+initialButtons.length);
      for(let index=0;index<47;index++) {
        const button=document.querySelectorAll('.help-page-item')[index];const title=button.querySelector('.help-result-page')?.textContent;
        button.click();
        await bounded(new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))),'Help render');
        const active=document.querySelector('.help-page-item[aria-current="location"]');
        const heading=document.querySelector('.help-content h1')?.textContent;
        if(active!==button||!heading)throw new Error('Help page failed to render at '+index);
        const images=[...document.querySelectorAll('.help-content img')];const decoded=[];
        for(const image of images) {
          await bounded(image.decode(),'image decode');
          const url=new URL(image.currentSrc||image.src);if(url.origin!==location.origin||!url.pathname.startsWith('/assets/'))throw new Error('Nonlocal Help image');
          if(image.naturalWidth!==1440||image.naturalHeight!==900)throw new Error('Unexpected screenshot dimensions');
          const asset=url.pathname.slice(1);state.images[asset]={width:image.naturalWidth,height:image.naturalHeight,complete:image.complete,alt:image.alt};decoded.push(asset);
        }
        state.pages.push({index,title,heading,active:true,imageCount:images.length,decoded});
      }
      await pause(100);
      if(new Set(state.pages.map(p=>p.title)).size!==47)throw new Error('Duplicate Help page title');
      if(Object.keys(state.images).length!==11)throw new Error('Expected all 11 decoded refreshed screenshots');
      if(state.violations.some(v=>!/^https?:\/\/example\.invalid(?:\/|$)/.test(v.uri)))throw new Error('Unexpected resource policy violation');
      if(state.errors.length)throw new Error('Help runtime errors');
      state.status='passed';
    } catch(error) {state.status='failed';state.error=String(error);}
  })();
  return {started:true,policy:policyText};
})()
