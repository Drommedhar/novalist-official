import { useEffect } from 'react'
import { useEditorBridge } from '../../stores/editorBridgeStore'
import { editorPane, useProjectStore } from '../../stores/projectStore'
import { useShellStore } from '../../stores/shellStore'
import { registerPendingWrite } from '../../stores/pendingWrites'
import { type EditorFrameModel } from './useEditorFrameModel'

export function useEditorPersistence({ editorRef, pane, openSceneId }: Pick<EditorFrameModel, 'editorRef' | 'pane' | 'openSceneId'>): void {
// The iframe normally reports an edit after 50 ms and the project store
  // writes it after two seconds. An update may already be cached, so capture
  // the live DOM and drain both delays before the installer is allowed to run.
  useEffect(() => {
    const capture = (): void => {
      const live = editorRef.current
      const store = useProjectStore.getState()
      const current = editorPane(store, pane)
      if (live && current.sceneId) {
        const html = live.getContent()
        const plainText = live.getPlainText()
        if (html !== current.html || plainText !== current.plainText) store.onEditorContentChanged(pane, html, plainText)
      }
    }
    return registerPendingWrite(async () => {
      capture()
      await useProjectStore.getState().flushPane(pane)
    }, capture)
  }, [pane, openSceneId])
}

export function useEditorScene({ editorRef, sceneHtml, lastReportedHtmlRef, loadingRef, loadAnnotations, applyFootnoteOrder, openSceneId, annotationsRevision, ownAnnotationsRef, pendingSuggestion, isActiveEditor, pushEntityNames }: Pick<EditorFrameModel, 'editorRef' | 'sceneHtml' | 'lastReportedHtmlRef' | 'loadingRef' | 'loadAnnotations' | 'applyFootnoteOrder' | 'openSceneId' | 'annotationsRevision' | 'ownAnnotationsRef' | 'pendingSuggestion' | 'isActiveEditor' | 'pushEntityNames'>): void {
// Push content on scene switch and on genuine external changes (snapshot
  // restore, live disk edits), but NOT when sceneHtml is merely the echo of the
  // edit the editor just reported - that would reset the caret and kill undo.
  useEffect(() => {
    const editor = editorRef.current
    if (!editor || sceneHtml === null) return
    if (sceneHtml === lastReportedHtmlRef.current) return
    loadingRef.current = true
    editor.setContent(sceneHtml)
    loadingRef.current = false
    lastReportedHtmlRef.current = sceneHtml
    void loadAnnotations(editor).then((loaded) => {
      if (!loaded) return
      // The prose is on screen now, so it can say what the numbers are. A scene
      // whose numbers were written by the old rule is repaired here, the first
      // time it is opened.
      const ids = editor.footnoteOrder()
      if (ids.length > 0) applyFootnoteOrder(ids)
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [openSceneId, sceneHtml])

// Somebody else wrote the scene's annotations - the Footnotes panel editing a
  // note or deleting one - so the copy this pane holds is out of date and the
  // next thing it saves would put the old text back.
  useEffect(() => {
    if (annotationsRevision === ownAnnotationsRef.current) return
    ownAnnotationsRef.current = annotationsRevision
    void loadAnnotations(editorRef.current)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [annotationsRevision])

// Following "2 waiting in this scene" has to end at one of them. The scene is
  // opened first and its prose arrives a moment later, so this waits for the
  // content rather than firing on the click.
  useEffect(() => {
    if (!pendingSuggestion || !isActiveEditor) return
    if (pendingSuggestion.sceneId !== openSceneId) return
    const editor = editorRef.current
    if (!editor) return
    useShellStore.getState().clearPendingSuggestion()
    editor.scrollToSuggestionById(pendingSuggestion.changeId)
  }, [pendingSuggestion, isActiveEditor, openSceneId, sceneHtml])

// Detection is scene-scoped: an entry silenced in one scene must come back in
  // the next, so the name list is rebuilt whenever the open scene changes.
  useEffect(() => {
    const editor = editorRef.current
    if (!editor || !openSceneId) return
    void pushEntityNames(editor)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [openSceneId])
}

export function useEditorControls({ editorRef, isActiveEditor, openSceneId, suggestionMode, reviewerName, sceneHtml, setLinkPrompt, toggleReadAloud, speaking, iframeRef, pane }: Pick<EditorFrameModel, 'editorRef' | 'isActiveEditor' | 'openSceneId' | 'suggestionMode' | 'reviewerName' | 'sceneHtml' | 'setLinkPrompt' | 'toggleReadAloud' | 'speaking' | 'iframeRef' | 'pane'>): void {
// Chromium reports the misspelling under the pointer as the menu opens, and
  // it is the only place those suggestions exist - there is no API to ask for
  // them. Handed to the frame so they land in the menu already on screen.
  useEffect(() => {
    window.novalist.onSpellingContext((word, suggestions) => {
      editorRef.current?.setSpellingSuggestions(word, suggestions)
    })
  }, [])

// Panels outside this pane - the footnotes and comments lists - need a way
  // to reach the prose. Registered by the pane the writer is in alone: another
  // editor is showing a different scene, and answering for it would strip a
  // marker out of the wrong one.
  useEffect(() => {
    if (!isActiveEditor) return
    useEditorBridge.getState().register(editorRef.current, openSceneId)
    return () => useEditorBridge.getState().register(null, null)
  }, [isActiveEditor, openSceneId])

// Suggestion mode belongs to the writing view rather than to the button that
  // used to hold it in local state, so the editor is told whenever the shell's
  // flag moves - whoever moved it, a toolbar button or the palette.
  useEffect(() => {
    editorRef.current?.setSuggestionMode(suggestionMode, reviewerName)
  }, [suggestionMode, reviewerName, sceneHtml])

// Two commands the frame owns rather than the editor: asking for a link
  // address, and driving the system speech engine. Published so the command
  // registry can offer them by name - a command only one button knows how to
  // run is a command the palette cannot reach.
  useEffect(() => {
    if (!isActiveEditor) return
    useEditorBridge.getState().setFrameCommands({
      requestLink: () => setLinkPrompt(true),
      toggleReadAloud
    })
    return () =>
      useEditorBridge.getState().setFrameCommands({ requestLink: null, toggleReadAloud: null })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isActiveEditor, speaking])

// Opening a scene puts the caret in it. Without this the writer clicks a
  // scene in the binder, starts typing, and the keystrokes go to the binder -
  // which is why the editor grew a focusEditor that nothing ever called.
  useEffect(() => {
    if (!isActiveEditor || !openSceneId) return
    const at = window.setTimeout(() => {
      // A footnote or another text field may have taken the caret while the
      // scene loaded. Its later editing gesture owns focus now.
      const active = document.activeElement
      if (active instanceof HTMLElement &&
        (active.isContentEditable || active.matches('input, textarea, select'))) return
      // The iframe first. Focusing an element inside a frame does nothing for
      // the keyboard while the frame itself is not focused - the caret appears
      // and the typing still goes to whatever the host had focused, which is
      // why calling focusEditor on its own looked like it did nothing.
      iframeRef.current?.focus()
      editorRef.current?.focusEditor()
    }, 60)
    return () => window.clearTimeout(at)
  }, [pane, openSceneId])
}
