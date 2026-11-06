import { JSDOM } from 'jsdom'
import createDOMPurify from 'dompurify'
import { createSceneSanitizer } from '../../shared/sceneSanitizer'
import { ProseDocumentHost as SharedDocuments } from '../../shared/ProseDocumentHost'

export class ProseDocumentHost extends SharedDocuments {
  private readonly dom: JSDOM

  constructor() {
    const dom = new JSDOM('<!doctype html>')
    super(dom.window.document, createSceneSanitizer(dom.window, createDOMPurify(dom.window)))
    this.dom = dom
  }

  override dispose(): void {
    super.dispose()
    this.dom.window.close()
  }
}
