function animatePopup(element, phase) {
    const preference = window.matchMedia('(prefers-reduced-motion: reduce)');
    const finish = () => {
        preference.removeEventListener('change', skip);
        if (element.dataset.popupMotion !== phase) return;
        delete element.dataset.popupMotion;
        if (phase === 'exit') element.remove();
    };
    const skip = () => { if (preference.matches) finish(); };
    element.dataset.popupMotion = phase;
    if (preference.matches || window.parent.document.documentElement.dataset.shell !== 'desktop') {
        finish();
        return;
    }
    const tokens = getComputedStyle(window.parent.document.documentElement);
    for (const property of ['--nl-motion-fast', '--nl-motion-ease', '--nl-motion-distance']) {
        element.style.setProperty(property, tokens.getPropertyValue(property));
    }
    const animations = element.getAnimations().filter(animation =>
        animation instanceof CSSAnimation && animation.animationName === `map-popup-${phase}`);
    if (animations.length === 0) {
        finish();
        return;
    }
    preference.addEventListener('change', skip);
    void Promise.allSettled(animations.map(animation => animation.finished)).then(finish);
}

export function showPopup(element) {
    animatePopup(element, 'enter');
}

export function removePopup(element) {
    if (element.inert) return;
    element.inert = true;
    element.setAttribute('aria-hidden', 'true');
    if (element.contains(document.activeElement)) document.activeElement.blur();
    animatePopup(element, 'exit');
}
