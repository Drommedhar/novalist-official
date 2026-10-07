(() => {
const describe = node => ({tag:node?.tagName,classes:typeof node?.className==='string'?node.className:null,role:node?.getAttribute?.('role'),label:node?.getAttribute?.('aria-label'),placeholder:node?.getAttribute?.('placeholder'),text:node?.tagName==='BUTTON'?node.textContent:null});
window.auditR15Pointer={events:[],describe};
for(const type of ['pointerdown','pointerup','click','focusin']) document.addEventListener(type,e=>{
 const item={type:e.type,trusted:e.isTrusted,target:describe(e.target),active:describe(document.activeElement),x:e.clientX,y:e.clientY,pointerType:e.pointerType,dialogs:document.querySelectorAll('[role="dialog"]').length};
 window.auditR15Pointer.events.push(item);
 setTimeout(()=>{item.after=describe(document.activeElement);item.prevented=e.defaultPrevented;item.dialogsAfter=document.querySelectorAll('[role="dialog"]').length},0);
},true);
return {installed:true,active:describe(document.activeElement)};
})()
