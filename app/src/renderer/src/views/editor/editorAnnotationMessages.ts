import { useShellStore } from '../../stores/shellStore'
import type { EditorHandlerContext, EditorMessageHandlers } from './editorMessageContext'

export function createEditorAnnotationMessages(context: EditorHandlerContext): EditorMessageHandlers {
  const { editorRef, annotationsRef, persistAnnotations, pushAnnotations, applyFootnoteOrder, loadAnnotations } = context
  return {
    requestAddComment: () => {
      editorRef.current?.addCommentToSelection(crypto.randomUUID())
    },
    commentAdded: (message) => {
      annotationsRef.current.comments.push({
        id: String(message.commentId),
        anchorText: String(message.anchorText ?? ''),
        text: '',
        resolved: false
      })
      void persistAnnotations()
      if (editorRef.current) pushAnnotations(editorRef.current)
    },
    commentTextChanged: (message) => {
      const comment = annotationsRef.current.comments.find(
        (c) => c.id === String(message.commentId)
      )
      if (comment) {
        comment.text = String(message.text ?? '')
        void persistAnnotations()
      }
    },
    commentDeleted: (message) => {
      annotationsRef.current.comments = annotationsRef.current.comments.filter(
        (c) => c.id !== String(message.commentId)
      )
      editorRef.current?.removeCommentById(String(message.commentId))
      void persistAnnotations()
    },
    commentClicked: (message) => {
      editorRef.current?.scrollToCommentById(String(message.commentId ?? ''))
    },
    requestAddFootnote: () => {
      editorRef.current?.insertFootnoteAtSelection(crypto.randomUUID())
    },
    footnoteInserted: (message) => {
      const footnoteId = String(message.footnoteId)
      if (!annotationsRef.current.footnotes.some((f) => f.id === footnoteId)) {
        annotationsRef.current.footnotes.push({ id: footnoteId, number: 0, text: '' })
      }
      // The whole order, because a note put in ahead of another renumbers
      // it. The message carries it; the count is only a fallback for an
      // editor build that predates that.
      applyFootnoteOrder(
        Array.isArray(message.ids)
          ? (message.ids as unknown[]).map(String)
          : annotationsRef.current.footnotes.map((f) => f.id)
      )
      // The note is worth writing at the moment it is made, so the box that
      // holds it takes the caret rather than waiting to be found.
      useShellStore.getState().requestFootnoteText(footnoteId)
    },
    footnotesRenumbered: (message) => {
      const ids = Array.isArray(message.ids) ? (message.ids as unknown[]).map(String) : []
      // A marker was taken out of the prose, and the panel that asked for
      // that has already written its own list. Reading it back first is what
      // stops this pane's older copy from being written over the top of it.
      void loadAnnotations(editorRef.current).then((loaded) => {
        if (loaded) applyFootnoteOrder(ids)
      })
    }
  }
}
