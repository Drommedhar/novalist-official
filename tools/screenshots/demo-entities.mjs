// aislop-ignore-file code-quality/duplicate-block -- Fictional entity fixtures intentionally share a schema; identities, traits, and prose are distinct data.
export const CHARACTERS = [
  {
    name: 'Mira', surname: 'Aldencourt',
    fields: {
      role: 'Protagonist', group: 'Aldencourt & Co.', gender: 'Female', age: '26',
      eyeColor: 'Grey', hairColor: 'Dark brown', hairLength: 'Shoulder, tied back',
      height: '5 ft 8', build: 'Wiry', skinTone: 'Pale, weathered at the hands',
      distinguishingFeatures: 'Ink permanently under the nails of the right hand'
    },
    aliases: ['The cartographer’s daughter'],
    sections: [
      { title: 'Want', content: 'To know what happened to her father — and, beneath that, to be told she was right to keep asking when everyone else had stopped.' },
      { title: 'Wound', content: 'Signed the probate papers herself. Forty signatures declaring a man dead who, it turns out, was not.' },
      { title: 'Arc', content: 'From authenticating her father’s hand to trusting her own. The final chart is the first thing she draws that is not an imitation.' },
      { title: 'Voice', content: 'Precise, literal, funnier than she intends. Answers the question actually asked.' }
    ],
    relationships: [
      { role: 'Father', target: 'Silas Aldencourt' },
      { role: 'Captain', target: 'Yewen Roake' },
      { role: 'Adversary', target: 'Halvard Crane' }
    ]
  },
  {
    name: 'Silas', surname: 'Aldencourt',
    fields: {
      role: 'The missing man', group: 'Aldencourt & Co.', gender: 'Male', age: '58',
      eyeColor: 'Grey', hairColor: 'White', height: '5 ft 10',
      distinguishingFeatures: 'Two fingers of the left hand lost to a winch, 19 years before'
    },
    aliases: ['S.A.', 'The Bellhaven surveyor'],
    sections: [
      { title: 'Role in the story', content: 'Declared lost with the Corvid in October. Fed at Cormorant Light the following March. Never appears on the page after chapter one.' },
      { title: 'The salt-ink', content: 'A survey trick from his Guild apprenticeship: a second chart drawn in brine, invisible until warmed. He used it to keep an honest copy of every survey he was paid to falsify.' }
    ],
    relationships: [{ role: 'Daughter', target: 'Mira Aldencourt' }, { role: 'Blackmailed by', target: 'Halvard Crane' }]
  },
  {
    name: 'Yewen', surname: 'Roake',
    fields: {
      role: 'Captain of the Meridian', group: 'Meridian', gender: 'Female', age: '44',
      eyeColor: 'Brown', hairColor: 'Black, greying', hairLength: 'Cropped',
      height: '5 ft 6', build: 'Solid', distinguishingFeatures: 'Burn scar across the left forearm; never explained'
    },
    aliases: ['Captain Roake'],
    sections: [
      { title: 'Want', content: 'To keep the Meridian, which she owns three-fifths of and has mortgaged the rest of.' },
      { title: 'Method', content: 'Says one sentence where other captains say ten, and expects to be understood.' },
      { title: 'Turn', content: 'Chapter nine — tells Mira to publish, knowing it ends her own quiet arrangement with the Admiralty pilots.' }
    ],
    relationships: [
      { role: 'Navigator', target: 'Tamsin Okonkwo' },
      { role: 'Passenger', target: 'Mira Aldencourt' },
      { role: 'Ship’s surgeon', target: 'Perrin Vale' }
    ]
  },
  {
    name: 'Tamsin', surname: 'Okonkwo',
    fields: {
      role: 'Navigator', group: 'Meridian', gender: 'Female', age: '38',
      eyeColor: 'Dark brown', hairColor: 'Black', height: '5 ft 4',
      distinguishingFeatures: 'Left-handed; keeps her own private log in a shorthand nobody else reads'
    },
    sections: [
      { title: 'Want', content: 'A room in Bellhaven her sister cannot be turned out of.' },
      { title: 'POV', content: 'Carries chapter seven. The only outside view of Roake the reader ever gets.' }
    ],
    relationships: [{ role: 'Sails under', target: 'Yewen Roake' }, { role: 'Sister', target: 'Josua Fen' }]
  },
  {
    name: 'Perrin', surname: 'Vale',
    fields: {
      role: 'Ship’s surgeon / Guild agent', group: 'Cartographers’ Guild', gender: 'Male', age: '41',
      eyeColor: 'Pale blue', hairColor: 'Sandy', height: '6 ft', build: 'Narrow',
      distinguishingFeatures: 'Immaculate cuffs at all times, in all weather'
    },
    aliases: ['Doctor Vale'],
    sections: [
      { title: 'Cover', content: 'Signed aboard at Bellhaven three days after Mira booked her passage. His papers are genuine; his reason is not.' },
      { title: 'Want', content: 'The chart, intact, and Mira unable to say where she got it.' }
    ],
    relationships: [{ role: 'Reports to', target: 'Halvard Crane' }, { role: 'Exposed by', target: 'Tamsin Okonkwo' }]
  },
  {
    name: 'Halvard', surname: 'Crane',
    fields: {
      role: 'Antagonist', group: 'Cartographers’ Guild', gender: 'Male', age: '52',
      eyeColor: 'Green', hairColor: 'Grey', height: '5 ft 11',
      distinguishingFeatures: 'A voice people describe as kind before they describe anything else'
    },
    sections: [
      { title: 'Want', content: 'That the Unnamed Waters stay unnamed for one more generation, by which time the leases will have run.' },
      { title: 'Why he works', content: 'He is not lying when he says he admired Silas Aldencourt. He paid him for thirty years and thought of it as patronage.' }
    ],
    relationships: [{ role: 'Paid', target: 'Silas Aldencourt' }, { role: 'Agent', target: 'Perrin Vale' }]
  },
  {
    name: 'Nan', surname: 'Ellery',
    fields: {
      role: 'Keeper of Cormorant Light', gender: 'Female', age: '67',
      eyeColor: 'Blue', hairColor: 'White', height: '5 ft 2',
      distinguishingFeatures: 'Thirty years of keeper’s books, none of them ever wrong'
    },
    sections: [
      { title: 'Function', content: 'Gives Mira the date. Everything after chapter eight follows from one line in a ledger.' }
    ],
    relationships: [{ role: 'Fed', target: 'Silas Aldencourt' }]
  },
  {
    name: 'Josua', surname: 'Fen',
    fields: {
      role: 'Ship’s boy', group: 'Meridian', gender: 'Male', age: '15',
      eyeColor: 'Brown', hairColor: 'Red', height: '5 ft 1'
    },
    sections: [{ title: 'Function', content: 'Heard the four men forward talking on Tuesday and told nobody, which is the whole of his guilt and most of his arc.' }],
    relationships: [{ role: 'Sister', target: 'Tamsin Okonkwo' }]
  }
]

export const LOCATIONS = [
  {
    name: 'Bellhaven',
    fields: { type: 'Port town', description: 'A working harbour of nine thousand souls, built on the chart trade and quietly dying of it. Rain comes sideways from October to March.' },
    sections: [{ title: 'Feel', content: 'Everything warped half an inch too large for its frame.' }],
    relationships: [{ role: 'Contains', target: 'Quay Street Auction Rooms' }]
  },
  {
    name: 'Quay Street Auction Rooms',
    fields: { type: 'Building', parent: 'Bellhaven', description: 'Where the Aldencourt estate is broken into forty-one lots. Warm, panelled, and the only dry room in chapter two.' },
    relationships: [{ role: 'In', target: 'Bellhaven' }]
  },
  {
    name: 'The Sable Shoals',
    fields: { type: 'Waters', description: 'Ninety miles of standing rock on every admiralty survey since the withdrawal. Nine hulls in twelve years. One channel, eleven fathoms, that officially is not there.' },
    sections: [{ title: 'The lie', content: 'Not a navigational error. A thirty-year commercial arrangement, minuted and paid for.' }]
  },
  {
    name: 'Cormorant Light',
    fields: { type: 'Lighthouse', description: 'Ninety feet of black rock at the head of the Shoals, kept without relief by Nan Ellery for thirty years.' },
    relationships: [{ role: 'Kept by', target: 'Nan Ellery' }]
  },
  {
    name: 'Ashgrave Isle',
    fields: { type: 'Island', description: 'Eleven miles north-northeast through the channel. A stone jetty with the weed cut back inside the month, and a slate-roofed house holding nineteen years of ledgers.' },
    sections: [{ title: 'Reveal', content: 'The island was never the secret. The bookkeeping was.' }]
  },
  {
    name: 'The Meridian',
    fields: { type: 'Ship', description: 'A hundred-and-forty-ton brig, copper-bottomed, three-fifths owned by her captain and wholly mortgaged.' },
    relationships: [{ role: 'Captain', target: 'Yewen Roake' }]
  }
]

export const ITEMS = [
  {
    name: 'The Drowned Chart',
    fields: { type: 'Document', description: 'Lot fifteen, sheet forty-one. The northern approaches at four leagues to the inch, with a second survey drawn in brine beneath it that appears only under heat.' },
    sections: [{ title: 'Rule', content: 'It fades as it cools. Copy it or lose it — Mira never trusts a map that can change its mind.' }]
  },
  {
    name: 'Silas’s Sextant',
    fields: { type: 'Instrument', description: 'Maker unknown, sold as seen. A working instrument, not a gentleman’s — which is how Roake decides to take her aboard.' }
  },
  {
    name: 'The Verdigris Key',
    fields: { type: 'Key', description: 'Hung on the shop wall for Mira’s whole childhood, labelled nothing, opening nothing she had ever found. Green with thirty years of sea air.' }
  },
  {
    name: 'The Keeper’s Book',
    fields: { type: 'Ledger', description: 'Thirty years of arrivals at Cormorant Light in Nan Ellery’s hand. Records Silas Aldencourt on the ninth of March — five months after he was declared lost.' }
  }
]

export const LORE = [
  {
    name: 'The Cartographers’ Guild',
    fields: { description: 'Charters every surveyor on the coast and, through the Corrections Office, decides which surveys are entered and which are withdrawn. Its power is entirely administrative, which is why nobody fears it until they are inside it.' },
    sections: [{ title: 'In practice', content: 'A column headed Corrections and a column headed Consideration, running side by side for thirty years.' }]
  },
  {
    name: 'Salt-ink',
    fields: { description: 'A Guild apprentice’s trick: a chart drawn in brine on ordinary paper, invisible until warmed, gone again as it cools. Taught as a curiosity. Used by exactly one man as a conscience.' }
  },
  {
    name: 'The Doctrine of Unnamed Waters',
    fields: { description: 'Ground not entered on an admiralty chart cannot be claimed, leased, or insured. Thirty years of unnamed water is thirty years of leases nobody has to renew.' },
    sections: [{ title: 'Stakes', content: 'Publishing the chart does not expose a crime. It ends a business.' }]
  }
]
