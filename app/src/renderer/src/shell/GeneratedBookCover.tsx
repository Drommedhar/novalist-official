/** A stable visual identity for an unfinished cover. Project IDs choose the
 * palette; book IDs choose the artwork. Titles remain content, never the seed. */
function coverHash(value: string): number {
  let hash = 2166136261
  for (let i = 0; i < value.length; i++) hash = Math.imul(hash ^ value.charCodeAt(i), 16777619)
  return hash >>> 0
}

const palettes = ['blue', 'rust', 'green', 'cream', 'purple', 'ochre', 'slate', 'rose'] as const
const motifs = ['moon', 'mountains', 'arch', 'waves', 'orbit', 'leaves'] as const

function CoverArtwork({ motif }: { motif: typeof motifs[number] }): React.JSX.Element {
  switch (motif) {
    case 'moon': return <>
      <circle cx="118" cy="172" r="42" fill="currentColor" fillOpacity="0.2" />
      <circle cx="106" cy="159" r="32" />
      <path d="M0 290 48 206 81 241 124 185 200 290M34 267l20-31 27 20 42-45 46 57" />
    </>
    case 'mountains': return <>
      <circle cx="150" cy="150" r="24" />
      <path d="m-10 300 83-160 61 112 24-51 62 99Z" fill="currentColor" fillOpacity="0.12" />
      <path d="m49 186 24-46 32 59-25-12-7-20-14 30" />
    </>
    case 'arch': return <>
      <path d="M42 300V180a58 58 0 0 1 116 0v120M54 300V183a46 46 0 0 1 92 0v117M66 300V186a34 34 0 0 1 68 0v114" />
      <path d="M20 284h160M30 275h140M42 266h116" />
    </>
    case 'waves': return <>
      <circle cx="143" cy="166" r="25" fill="currentColor" fillOpacity="0.2" />
      {Array.from({ length: 9 }, (_, i) => <path key={i} d={`M-20 ${203 + i * 9}q30-18 60 0t60 0 60 0 60 0`} />)}
    </>
    case 'orbit': return <>
      <circle cx="100" cy="196" r="47" />
      <ellipse cx="100" cy="196" rx="79" ry="24" transform="rotate(-35 100 196)" />
      <ellipse cx="100" cy="196" rx="68" ry="20" transform="rotate(35 100 196)" />
      <circle cx="100" cy="196" r="7" fill="currentColor" />
      <circle cx="157" cy="156" r="4" fill="currentColor" />
    </>
    case 'leaves': return <>
      <path d="M30 300c-17-72-4-148 13-207m130 207c23-68 9-139-8-198" />
      <path d="M28 257c-29-10-21-40-21-40 25 4 29 22 21 40Zm-2-51c-27-20-13-45-13-45 23 10 23 26 13 45Zm7-38c-23-31-6-51-6-51 20 15 16 34 6 51Zm-3 73c36-8 42-40 42-40-31 0-38 21-42 40Zm146 19c29-13 16-45 16-45-26 8-24 31-16 45Zm0-59c-33-2-38-33-38-33 30-4 39 18 38 33Z" fill="currentColor" fillOpacity="0.15" />
    </>
  }
}

export function GeneratedBookCover({ projectSeed, bookSeed, title, kind }: {
  projectSeed: string
  bookSeed: string
  title: string
  kind: string
}): React.JSX.Element {
  const palette = palettes[coverHash(projectSeed) % palettes.length]
  const motif = motifs[coverHash(`${projectSeed}:${bookSeed}`) % motifs.length]
  return <div className={`volume-generated-cover cover-palette-${palette} cover-motif-${motif}`}>
    <svg className="volume-cover-art" viewBox="0 0 200 300" preserveAspectRatio="xMidYMid slice" aria-hidden="true" focusable="false">
      <CoverArtwork motif={motif} />
    </svg>
    <div className="volume-cover-lettering">
      <span className="volume-title">{title}</span>
      <span className="volume-imprint">{kind}</span>
    </div>
  </div>
}
