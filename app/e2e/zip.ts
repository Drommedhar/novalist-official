import { inflateRawSync } from 'node:zlib'
import { readFileSync } from 'node:fs'

/**
 * Just enough ZIP to look inside an export.
 *
 * EPUB and DOCX are both zips, and asserting on what a format actually
 * contains is the only way to tell a real EPUB cover from an image that
 * happens to be in the archive. Doing it without a dependency, and without
 * shelling out: the `tar` first on PATH here is GNU tar, which cannot read zip
 * at all and reads `C:\...` as a remote host.
 *
 * Sizes and offsets come from the central directory rather than the local
 * headers, because a local header is allowed to carry zeroes and defer the
 * real sizes to a trailing data descriptor.
 */

const EOCD = 0x06054b50
const CENTRAL = 0x02014b50

export type ZipEntry = { name: string; data: Buffer }

export function readZip(path: string): Map<string, Buffer> {
  const buf = readFileSync(path)

  // The end-of-central-directory record is last, after a comment of unknown
  // length, so it is found by scanning back for its signature.
  let eocd = -1
  for (let i = buf.length - 22; i >= 0 && i > buf.length - 22 - 0xffff; i--) {
    if (buf.readUInt32LE(i) === EOCD) {
      eocd = i
      break
    }
  }
  if (eocd < 0) throw new Error(`not a zip: ${path}`)

  const count = buf.readUInt16LE(eocd + 10)
  let at = buf.readUInt32LE(eocd + 16)
  const out = new Map<string, Buffer>()

  for (let n = 0; n < count; n++) {
    if (buf.readUInt32LE(at) !== CENTRAL) throw new Error(`bad central directory in ${path}`)
    const method = buf.readUInt16LE(at + 10)
    const compressed = buf.readUInt32LE(at + 20)
    const nameLen = buf.readUInt16LE(at + 28)
    const extraLen = buf.readUInt16LE(at + 30)
    const commentLen = buf.readUInt16LE(at + 32)
    const localAt = buf.readUInt32LE(at + 42)
    const name = buf.toString('utf8', at + 46, at + 46 + nameLen)

    // The local header repeats the name and extra fields, at its own lengths.
    const localNameLen = buf.readUInt16LE(localAt + 26)
    const localExtraLen = buf.readUInt16LE(localAt + 28)
    const from = localAt + 30 + localNameLen + localExtraLen
    const raw = buf.subarray(from, from + compressed)

    if (!name.endsWith('/')) {
      out.set(name, method === 0 ? Buffer.from(raw) : inflateRawSync(raw))
    }
    at += 46 + nameLen + extraLen + commentLen
  }

  return out
}

/** Every text-ish member of the archive, concatenated and labelled. */
export function readZipText(path: string): string {
  let all = ''
  for (const [name, data] of readZip(path)) {
    if (/\.(x?html?|opf|xml|ncx|txt|md|css)$/i.test(name)) {
      all += `\n<<<${name}>>>\n${data.toString('utf8')}`
    }
  }
  return all
}
