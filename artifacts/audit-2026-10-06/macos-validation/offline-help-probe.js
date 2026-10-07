// Temporary evaluation in the actual simulator WKWebView; no product code changes.
(() => {
  const policy = document.createElement('meta');
  policy.httpEquiv = 'Content-Security-Policy';
  policy.content = "connect-src 'none'; img-src 'self' data: blob:; media-src 'self' data: blob:";
  document.head.append(policy);
  window.auditHelp = { status: 'running', pages: [], images: {}, externalFetchBlocked: false, blockedByPolicy: false };
  document.addEventListener('securitypolicyviolation', event => {
    if (event.effectiveDirective === 'connect-src' && event.blockedURI.startsWith('https://example.invalid/'))
      window.auditHelp.blockedByPolicy = true;
  }, { once: true });
  void (async () => {
    try {
      try { await fetch('https://example.invalid/audit-network-must-be-blocked'); }
      catch { window.auditHelp.externalFetchBlocked = true; }
      const buttons = [...document.querySelectorAll('.help-page-item')];
      for (const button of buttons) {
        button.click();
        await new Promise(resolve => setTimeout(resolve, 120));
        const heading = document.querySelector('.help-content h1')?.textContent;
        if (!heading) throw new Error('Missing Help page heading');
        const images = [...document.querySelectorAll('.help-content img')];
        for (const image of images) {
          await image.decode();
          if (!image.naturalWidth || !image.naturalHeight) throw new Error('Empty screenshot');
          if (!image.src.startsWith(location.origin + '/assets/')) throw new Error('Nonlocal screenshot');
          window.auditHelp.images[image.src.split('/').pop()] = {
            width: image.naturalWidth, height: image.naturalHeight
          };
        }
        window.auditHelp.pages.push({ heading, screenshotCount: images.length });
      }
      if (!window.auditHelp.externalFetchBlocked || !window.auditHelp.blockedByPolicy)
        throw new Error('External fetch was not blocked by browser policy');
      if (Object.keys(window.auditHelp.images).length !== 11) throw new Error('Expected all eleven screenshots');
      window.auditHelp.status = 'passed';
    } catch (error) {
      window.auditHelp.status = 'failed';
      window.auditHelp.error = String(error);
    }
  })();
  return { started: true };
})()
