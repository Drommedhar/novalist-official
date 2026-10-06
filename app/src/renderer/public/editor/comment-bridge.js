'use strict';

window.NovalistEditorState.editor.addEventListener('click', function (e) {
    var target = e.target;
    while (target && target !== window.NovalistEditorState.editor && !target.classList?.contains('nv-comment')) {
        target = target.parentNode;
    }
    if (target && target !== window.NovalistEditorState.editor && target.classList?.contains('nv-comment')) {
        var id = target.getAttribute('data-comment-id');
        if (id) window.sendMessage({ type: 'commentClicked', commentId: id });
    }
});

// Host bridge: addComment(id) wraps current selection. Returns anchor via message.
window.addCommentToSelection = function (commentId) {
    try {
        var anchor = window.wrapSelectionAsComment(commentId);
        window.sendMessage({
            type: 'commentAdded',
            commentId: commentId,
            anchorText: anchor || ''
        });
    } catch {
        window.sendMessage({ type: 'commentAdded', commentId: commentId, anchorText: '' });
    }
};

window.removeCommentById = window.removeCommentSpan;

window.scrollToCommentById = window.scrollToComment;
