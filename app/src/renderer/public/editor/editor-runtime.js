'use strict';

// Live bindings connect classic scripts without copying editor state.
Object.defineProperty(window, 'NovalistEditorState', { value: Object.create(null) });
