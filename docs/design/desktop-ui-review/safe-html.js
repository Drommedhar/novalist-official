'use strict';

window.reviewFragment = function (html) {
    return window.DOMPurify.sanitize(html, {
        RETURN_DOM_FRAGMENT: true,
        ADD_ATTR: ['target', 'contenteditable']
    });
};
