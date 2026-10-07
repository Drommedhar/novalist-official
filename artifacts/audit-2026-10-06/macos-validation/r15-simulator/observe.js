(() => {
  const describe = node => node instanceof Element ? { tag: node.tagName, id: node.id, classes: node.className, role: node.getAttribute('role'), label: node.getAttribute('aria-label'), placeholder: node.getAttribute('placeholder'), inert: !!node.closest('[inert]'), sheet: !!node.closest('.mobile-sheet') } : null;
  const state = window.auditR15 = { events: [], describe };
  for (const name of ['keydown', 'keyup', 'focusin', 'click']) document.addEventListener(name, event => {
    const record = { type: name, key: event.key, code: event.code, keyCode: event.keyCode, trusted: event.isTrusted, shift: event.shiftKey, meta: event.metaKey, target: describe(event.target), active: describe(document.activeElement) };
    state.events.push(record);
    setTimeout(() => { record.after = describe(document.activeElement); record.prevented = event.defaultPrevented; record.dialogs = document.querySelectorAll('[role="dialog"]').length; }, 0);
  }, true);
  return { installed: true, active: describe(document.activeElement), sheet: !!document.querySelector('.mobile-sheet') };
})()
