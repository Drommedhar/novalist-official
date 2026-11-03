import { test } from 'node:test'
import assert from 'node:assert/strict'
import * as checklist from '../src/renderer/src/shell/sceneChecklist.ts'

test('converts bullets without losing wording, nesting, checked state or CRLF', () => {
  const input = 'Keep this reminder.\r\n\r\n• First beat\r\n  - Detail\r\n  * [X] Already done\r\n\r\n+ Another beat\r\n';
  const expected = 'Keep this reminder.\r\n\r\n• [ ] First beat\r\n  - [ ] Detail\r\n  * [X] Already done\r\n\r\n+ [ ] Another beat\r\n';
  assert.equal(checklist.normalizeChecklist(input), expected)
  assert.equal(checklist.normalizeChecklist(expected), expected)
})

test('code fences, rules and ordinary prose are not converted into tasks', () => {
  const input = 'POV Liam.\n---\n* * *\n```md\n- example\n```\n~~~\n• example\n~~~\n- Real task';
  assert.equal(checklist.normalizeChecklist(input), input.replace('- Real task', '- [ ] Real task'))
})

test('keeps root titles, nested items and continuation prose together', () => {
  const blocks = checklist.parseChecklist('Reminder\n\n• [ ] Repair the bag\n  Speak calmly.\n\n  - [x] Rocket joke\n    Explain the nickname.\n  - [ ] Astronaut\n\n• [ ] Next beat')
  assert.equal(blocks[0].kind, 'text')
  assert.equal(blocks[0].text.trim(), 'Reminder')
  const root = blocks[1]
  assert.equal(root.title, 'Repair the bag')
  assert.equal(root.done, false)
  assert.equal(root.children[0].text.trim(), 'Speak calmly.')
  const children = root.children.filter((block) => block.kind === 'item')
  assert.deepEqual(children.map(({ title, done }) => ({ title, done })), [
    { title: 'Rocket joke', done: true }, { title: 'Astronaut', done: false }
  ])
  assert.equal(children[0].children[0].text.trim(), 'Explain the nickname.')
  assert.equal(blocks[2].title, 'Next beat')
})

test('checking a child changes only its marker, keeping parent independent', () => {
  const text = '• [ ] Parent\r\n  - [ ] Child\r\n  - [x] Sibling\r\n';
  assert.equal(checklist.setChecklistDone(text, 1, true), '• [ ] Parent\r\n  - [x] Child\r\n  - [x] Sibling\r\n')
  assert.equal(checklist.setChecklistDone(text, 9, true), text)
})

test('long flat bullets retain their complete text and tab-indented details', () => {
  const text = '• A long beat; retain every word, punctuation and reference.\n\t- Child\n\t\tExtra detail.'
  const [root] = checklist.parseChecklist(checklist.normalizeChecklist(text))
  assert.equal(root.title, 'A long beat; retain every word, punctuation and reference.')
  assert.equal(root.children[0].title, 'Child')
  assert.equal(root.children[0].children[0].text.trim(), 'Extra detail.')
})

test('fenced details are opaque to heading and indentation parsing', () => {
  const [parent, ...rest] = checklist.parseChecklist('- [ ] Parent\n  ```md\n# example\n  ```\n  - [ ] Child')
  assert.equal(rest.length, 0)
  assert.equal(parent.children[0].text, '  ```md\n# example\n  ```\n')
  assert.equal(parent.children[1].title, 'Child')
})

test('editing through a text editor preserves the existing CRLF convention', () => {
  assert.equal(checklist.preserveChecklistLineEndings('• [ ] Before\r\n  Details\r\n', '• [ ] After\n  Details\n'), '• [ ] After\r\n  Details\r\n')
  assert.equal(checklist.preserveChecklistLineEndings('- Old\n', '- New\n'), '- New\n')
})
