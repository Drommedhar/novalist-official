/**
 * Demo project content for App Store and manual screenshots.
 *
 * Entirely fictional. Nothing here comes from a real user project — this file is
 * the single source of truth for what the marketing and documentation captures
 * show, so a re-shoot always produces the same populated views.
 */

export const PROJECT_NAME = 'The Cartographer’s Daughter'
export const BOOK_NAME = 'The Cartographer’s Daughter'

const p = (...paras) => paras.map((t) => `<p>${t}</p>`).join('\n')

export const CHAPTERS = [
  {
    title: 'I. The Drowned Chart',
    act: 'Act I',
    status: 'Final',
    scenes: [
      {
        title: 'A Letter from Bellhaven',
        pov: 'Mira Aldencourt',
        synopsis:
          'A letter in her father’s hand arrives eleven months after his ship was declared lost. Mira leaves for the coast that night.',
        notes:
          'Establish the salt-ink motif early — the reader should not know yet that it is a code, only that the letter smells of the sea.',
        html: p(
          'The letter came on a Thursday, which Mira would afterwards think was the cruelty of it. Thursdays were for the ledger. Thursdays were for reconciling what the shop had sold against what the shop had promised, and for discovering, as she did every week, that the two had never once agreed.',
          'She knew the hand before she knew the seal. Eleven months of probate and condolence and the slow administrative business of being made an orphan, and still her body recognised her father’s writing the way it recognised a stair in the dark — before thought, and faster than it.',
          'The paper was cheap. That was the first wrong thing. Silas Aldencourt had held opinions about paper the way other men held opinions about God, and none of them would have permitted this grey, fibrous, ill-sized sheet. The second wrong thing was the smell. She lifted it to the lamp and breathed in and there it was, unmistakable beneath the tallow and the road: salt, and the particular green rot of a harbour at low tide.',
          '<em>Mira,</em> it said. <em>Do not believe the chart. Do not sell the chart. Do not let Crane know the chart exists.</em>',
          'There was no signature. There was, instead, a small figure inked in the lower corner — a compass rose with its north arm broken — and Mira sat for a long time with her thumb over it, in the shop that was no longer her father’s, listening to the gulls that had no business being this far inland.',
          'She was on the eastbound mail coach before the lamps were lit.'
        )
      },
      {
        title: 'The Auction on Quay Street',
        pov: 'Mira Aldencourt',
        synopsis:
          'Mira reaches the auction rooms an hour before the Aldencourt estate goes under the hammer, and meets the man her father warned her about.',
        notes: 'Crane should be charming here. The reader should like him.',
        html: p(
          'Bellhaven in November was a town holding its breath. The rain came sideways off the water and the whole of Quay Street had the look of something recently salvaged — the shopfronts warped, the paint gone chalky, every door swollen half an inch too large for its frame.',
          'The auction rooms were warm, at least. Mira stood at the back with her coat steaming and counted her father’s life laid out in lots. Lot fourteen: a case of drafting instruments, brass, some wear. Lot fifteen: forty-one charts of the northern approaches, various states. Lot sixteen: one sextant, maker unknown, sold as seen.',
          '‘You’re his girl.’',
          'The man beside her had appeared the way weather appears. He was perhaps fifty, dressed better than the room, and he did not look at her when he spoke — only at the lots, with the mild proprietary interest of someone reading a menu.',
          '‘I’m his daughter,’ Mira said.',
          '‘Yes.’ He smiled at the correction as though she had passed something. ‘Halvard Crane. I bought from your father for nineteen years and I never once got the better of him, which I want you to know I say with affection.’',
          'She had rehearsed a great many responses on the coach. Not one of them survived contact with the fact that she liked his voice.',
          '‘Lot fifteen,’ she said. ‘The northern approaches. I want to see them before they sell.’',
          'Crane’s smile did not move at all, and that was how she knew.'
        )
      },
      {
        title: 'What the Ink Concealed',
        pov: 'Mira Aldencourt',
        synopsis:
          'Held over a flame, the forty-first chart gives up a coastline that appears on no other map. Mira understands the letter.',
        notes: 'Reveal. Keep it physical — heat, paper, smell. No exposition dump.',
        html: p(
          'She took the chart back to the room above the chandler’s and did not sleep.',
          'It was, by every measure she had been raised to apply, an unremarkable sheet. The northern approaches at four leagues to the inch, drawn in her father’s tight and unbeautiful hand, showing the Sable Shoals and the run up to Cormorant Light and the ninety miles of grey nothing between. She had traced its like a hundred times as a child, for practice, for punishment, for the pleasure of watching a shoreline arrive under her own pen.',
          'It was only when the candle guttered and she moved it close — too close, close enough that she smelled the paper start to think about burning — that the nothing began to fill in.',
          'It came up brown and slow, the way a bruise comes up. A coastline where there was no coastline. A bay, sounded and marked. A channel through the Shoals that no pilot in Bellhaven would have sworn to, running north-northeast for eleven miles and ending at an anchorage her father had labelled in letters so small she had to hold the glass to them.',
          '<em>Ashgrave.</em>',
          'Mira sat back. The heat faded from the paper and the island went with it, dissolving out of the chart as politely as it had arrived, until there was nothing again but the Shoals and the light and the ninety miles of grey.',
          '<em>Do not believe the chart,</em> her father had written.',
          'He had not meant this one. He had meant every other chart in the world.'
        )
      }
    ]
  },
  {
    title: 'II. Salt and Sextant',
    act: 'Act II',
    status: 'Revised',
    scenes: [
      {
        title: 'Passage Aboard the Meridian',
        pov: 'Mira Aldencourt',
        synopsis:
          'No captain in Bellhaven will take a fare to the Shoals. One will take a cartographer.',
        notes: 'First Roake scene. She should be unimpressed by Mira and interested in her instruments.',
        html: p(
          'She asked eleven captains and was refused eleven times, which she came to understand was not superstition but arithmetic: the Sable Shoals had taken nine hulls in twelve years and insurance men can count.',
          'The twelfth was careening at the far end of the yard, a hundred-and-forty-ton brig with her copper showing and a name painted so recently it had not yet learned to look weathered. <em>Meridian.</em> The woman under her, up to the elbows in tallow, did not look up.',
          '‘I want passage to the Shoals,’ Mira said.',
          '‘No you don’t.’',
          '‘I’ll pay eighty.’',
          '‘You’ll pay eighty to drown, and I’ll be the one explaining it.’ Captain Yewen Roake straightened, wiped her hands, and looked at Mira properly for the first time — not at her face but at the case under her arm, the long flat one with the brass corners. ‘What’s in that?’',
          '‘A sextant.’',
          '‘Whose?’',
          '‘Mine.’ A beat. ‘My father’s.’',
          'Roake held out her hand, and something in the gesture made Mira open the case without arguing. The captain lifted the instrument the way you lift something asleep, turned it once to the light, and read the arc.',
          '‘This is a working sextant,’ she said, in the tone of a woman revising an estimate. ‘Not a gentleman’s.’',
          '‘He was not a gentleman.’',
          '‘No.’ Roake closed the case and handed it back. ‘Forty, and you work the glass. I don’t carry passengers and I don’t carry liars, and you’ve got until we clear the point to decide which one you’re going to stop being.’'
        )
      },
      {
        title: 'The Island That Isn’t',
        pov: 'Mira Aldencourt',
        synopsis:
          'Eleven miles into a channel that officially does not exist, the Meridian finds bottom exactly where the drowned chart said she would.',
        notes: 'Payoff for the sounding detail in ch.1. Vale should be watching Mira, not the water.',
        html: p(
          'They took the channel at first light with the leadsman calling and Roake at the rail saying nothing at all, which Mira had learned in six days was the loudest thing the captain did.',
          '‘By the deep, nine.’',
          'Mira had the chart flat on the skylight with a stone at each corner. She had warmed it that morning over the galley stove and the island had come up obediently, brown and patient, and she had copied every sounding onto a clean sheet in her own hand before it faded, because she did not trust a map that could change its mind.',
          '‘And a half, seven.’',
          'Nine. Seven and a half. The numbers walked down the page exactly as her father had written them eleven months before he was declared lost, in a channel that four separate admiralty surveys agreed was forty feet of standing rock.',
          '‘By the mark, five.’',
          'Roake turned her head very slightly. ‘Miss Aldencourt.’',
          '‘Five,’ Mira said. ‘Then four and a half for a cable, then it opens to eleven and holds.’',
          '‘And if it doesn’t?’',
          '‘Then my father was wrong,’ Mira said, ‘and I would very much like to find that out.’',
          'Behind them, in the companionway where he had no reason to be, Doctor Perrin Vale set down his cup without drinking from it, and did not take his eyes off the chart.'
        )
      },
      {
        title: 'Mutiny at Third Bell',
        pov: 'Tamsin Okonkwo',
        synopsis:
          'Vale makes his move for the chart. Tamsin has to choose between her captain and eleven years of wages.',
        notes: 'Switch POV here — first time. Tamsin is the reader’s way of seeing Roake from outside.',
        html: p(
          'Tamsin Okonkwo had sailed with Yewen Roake for eleven years and in that time had formed exactly one opinion about mutiny, which was that it never began with shouting.',
          'It began, as it did now, with a man being helpful.',
          'Vale had been helpful all afternoon. He had helped with the boats. He had helped the cook, which no one had ever done. And at the turn of the second watch he had helped himself down the companionway with a lamp he did not need, into a cabin that was not his, and had come up eleven minutes later with his coat buttoned over something flat.',
          'Tamsin watched him from the shadow of the mainmast and did the arithmetic she had been putting off for six days.',
          'Eleven years of wages. A captain who had never once left her behind, and had also never once explained herself. A passenger with a chart that made rock into water. Four men forward who had stopped meeting Tamsin’s eye on Tuesday.',
          'She thought about her sister in Bellhaven and the room they had not been able to keep.',
          'Then she crossed the deck, took Vale by the elbow with something that from any distance looked like courtesy, and said, very quietly, ‘The captain will want to see what you’re carrying.’',
          'The bell went for the third watch. Forward, in the dark, somebody put down a coil of rope very carefully, so that it would not make a sound.'
        )
      },
      {
        title: 'The Keeper of the Light',
        pov: 'Mira Aldencourt',
        synopsis:
          'Cormorant Light has been kept by the same woman for thirty years. She remembers Silas Aldencourt. She remembers when he came back.',
        notes: 'The hinge of the book. Nan gives Mira the date, and the date is after the shipwreck.',
        html: p(
          'The light stood on ninety feet of black rock and had been kept, without relief and by her own insistence, by Nan Ellery for thirty years.',
          'She fed them without asking who they were, which Mira understood was the courtesy of a place where the alternative to feeding people was burying them. Only when the plates were cleared did she sit down across from Mira and say, ‘You have his chin.’',
          'The room went very quiet.',
          '‘You knew my father.’',
          '‘I knew Silas Aldencourt thirty-one years and I fed him at this table more times than I fed my own brother.’ Nan poured. Her hands were steady in the way of someone who has decided to be. ‘Last time was the ninth of March.’',
          'Mira set down her cup. ‘That’s not possible.’',
          '‘It’s the ninth of March in the book, and I keep the book, and I have never once been wrong in it.’',
          '‘The <em>Corvid</em> went down in October,’ Mira said. Her voice came out level and she was distantly proud of it. ‘There was an inquiry. There were nine witnesses. He was declared lost in October and the estate was settled in April and I have signed my name to it forty times.’',
          'Nan Ellery looked at her for a long moment with great and terrible kindness.',
          '‘Then somebody,’ she said, ‘has been signing a different name than the one they thought.’'
        )
      }
    ]
  },
  {
    title: 'III. True North',
    act: 'Act III',
    status: 'Outline',
    scenes: [
      {
        title: 'The Verdigris Key',
        pov: 'Mira Aldencourt',
        synopsis:
          'What Silas hid on Ashgrave was never the island. It was the record of who paid to have it unmapped.',
        notes: 'Crane’s conspiracy lands here. Keep the Guild off-page — it is scarier as paperwork.',
        html: p(
          'The anchorage opened at eleven fathoms and held, exactly as the chart had promised, and the island that four admiralty surveys agreed was standing rock rose out of the morning with a stone jetty on it.',
          'Not a ruin. A jetty, maintained, with the weed cut back to the waterline within the month.',
          '‘Somebody,’ Roake said, ‘is paying a man to do that.’',
          'The house at the head of the path was low and slate-roofed and had a door of oak banded in iron, and the lock was green with thirty years of sea air. Mira took her father’s key out of her coat — the one that had hung on the shop wall her whole childhood, labelled nothing, opening nothing she had ever found — and it went in as though the two had been apart a week.',
          'Inside there were no charts at all.',
          'There were ledgers. Nineteen of them, shelved by year, in the flat institutional hand of men who are paid to be careful. Sums paid. Surveys withdrawn. A column headed <em>Corrections</em> and, beside it, a column headed <em>Consideration</em>, and running down the second of these for thirty years, in amounts that made Mira sit down on the floor of her father’s house, the same name.',
          'She read it four times before she let herself believe the arithmetic.',
          'Then she closed the ledger, and went out into the light, and thought about a man in a warm room on Quay Street saying <em>I never once got the better of him</em>, with affection.'
        )
      },
      {
        title: 'The Chart Completed',
        pov: 'Mira Aldencourt',
        synopsis:
          'Mira finishes the survey her father started. Roake gives her the choice of what to do with it.',
        notes: 'Quiet scene. The decision, not the confrontation.',
        html: p(
          'She worked for nine days and Roake let her, which was its own kind of statement.',
          'Sun sights at noon and stars when the sky allowed it. The bay sounded twice over, once by boat and once at low water on foot with her boots in her hand. The channel run and re-run until she could have drawn it blind. Every figure entered twice, in ink, in a hand that had stopped being an imitation of her father’s somewhere around the fourth day and had become, without her noticing, hers.',
          'On the ninth evening she carried the finished sheet up on deck and laid it on the skylight and did not put stones on the corners, because it did not need them any more. It was a chart. It stayed what it was in any light you cared to bring.',
          'Roake looked at it for a long time.',
          '‘You know what that’s worth,’ she said at last.',
          '‘I know what it was worth to keep it off the books for thirty years,’ Mira said. ‘Which is not the same number.’',
          '‘No.’ Roake put her hands on the rail. ‘Admiralty will bury it. Crane will buy it. And there’s a third thing you could do that neither of them has thought of, because neither of them has ever had to.’',
          '‘Which is?’',
          '‘Publish it,’ said the captain, ‘and let the whole rotten trade find out at once.’'
        )
      },
      {
        title: 'Landfall',
        pov: 'Mira Aldencourt',
        synopsis: 'Bellhaven, in spring. The Aldencourt shop reopens under a different sign.',
        notes: 'Ending. Do not reunite her with Silas on the page — the letter is enough.',
        html: p(
          'The shop on Quay Street reopened in April under a sign that said ALDENCOURT & CO., SURVEYORS, which was one word longer than it had ever been and, Mira thought, considerably more honest.',
          'The northern approaches went out in June, engraved in Bellhaven, four leagues to the inch, showing the Sable Shoals and the run up to Cormorant Light and — eleven miles north-northeast through a channel that held at eleven fathoms — an island named for the first time in print.',
          'It sold six hundred copies in a fortnight. It was cited at two inquiries. Halvard Crane left the country in October and the ledgers went to the Admiralty in nineteen crates, and Mira was told by a man in a grey room that she had been very foolish and had also, he conceded, been entirely correct.',
          'In the spring after that, a letter came on a Thursday.',
          'Cheap paper. No signature. In the lower corner, a compass rose with its north arm broken — and beneath it, in a hand she had known before she knew her own, four words.',
          '<em>The chart is good.</em>',
          'Mira put it in the case with the sextant, closed the shop, and walked down to the water to watch the tide come in over ground that was now, at last, correctly drawn.'
        )
      }
    ]
  }
]

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

export const PLOTLINES = [
  'Mira’s Search',
  'The Guild Conspiracy',
  'Roake & the Meridian',
  'The Chart’s Secret'
]

/** scene title -> plotlines active in it */
export const PLOT_CELLS = {
  'A Letter from Bellhaven': ['Mira’s Search', 'The Chart’s Secret'],
  'The Auction on Quay Street': ['Mira’s Search', 'The Guild Conspiracy'],
  'What the Ink Concealed': ['The Chart’s Secret', 'Mira’s Search'],
  'Passage Aboard the Meridian': ['Roake & the Meridian', 'Mira’s Search'],
  'The Island That Isn’t': ['The Chart’s Secret', 'Roake & the Meridian', 'The Guild Conspiracy'],
  'Mutiny at Third Bell': ['The Guild Conspiracy', 'Roake & the Meridian'],
  'The Keeper of the Light': ['Mira’s Search', 'The Chart’s Secret'],
  'The Verdigris Key': ['The Guild Conspiracy', 'The Chart’s Secret'],
  'The Chart Completed': ['Mira’s Search', 'Roake & the Meridian'],
  Landfall: ['Mira’s Search', 'The Guild Conspiracy', 'The Chart’s Secret']
}

// categoryId must be one of the built-in timeline categories: plot, character, world.
export const TIMELINE_EVENTS = [
  { title: 'Silas takes the Corvid north', date: '1847-10-02', description: 'The last survey voyage entered in the Guild register.', category: 'world' },
  { title: 'The Corvid declared lost', date: '1847-10-19', description: 'Nine witnesses. An inquiry that lasts four days.', category: 'world' },
  { title: 'Silas at Cormorant Light', date: '1848-03-09', description: 'Nan Ellery feeds him at her table and enters it in the keeper’s book.', category: 'character' },
  { title: 'The estate is settled', date: '1848-04-11', description: 'Mira signs her name forty times.', category: 'character' },
  { title: 'The letter arrives', date: '1848-11-02', description: 'Cheap paper, no signature, a compass rose with a broken north arm.', category: 'plot' },
  { title: 'The Quay Street auction', date: '1848-11-06', description: 'Forty-one lots. Mira meets Halvard Crane.', category: 'plot' },
  { title: 'The ink is read', date: '1848-11-07', description: 'Ashgrave appears under the candle and fades again.', category: 'plot' },
  { title: 'The Meridian sails', date: '1848-11-14', description: 'Forty pounds and the glass.', category: 'character' },
  { title: 'The channel is run', date: '1848-11-20', description: 'Nine, seven and a half, five — exactly as written.', category: 'plot' },
  { title: 'Vale takes the chart', date: '1848-11-22', description: 'Third bell. Tamsin chooses.', category: 'plot' },
  { title: 'Landfall at Ashgrave', date: '1848-11-26', description: 'Nineteen ledgers, shelved by year.', category: 'world' },
  { title: 'The northern approaches published', date: '1849-06-01', description: 'Six hundred copies in a fortnight. Two inquiries.', category: 'world' }
]

export const GOALS = { daily: 1200, project: 90000 }

// Story dates for the Calendar view, matching the timeline events above. The
// Calendar plots scenes by resolved story date, so without these it renders an
// empty grid.
export const SCENE_DATES = {
  'A Letter from Bellhaven': '1848-11-02',
  'The Auction on Quay Street': '1848-11-06',
  'What the Ink Concealed': '1848-11-07',
  'Passage Aboard the Meridian': '1848-11-14',
  'The Island That Isn’t': '1848-11-20',
  'Mutiny at Third Bell': '1848-11-22',
  'The Keeper of the Light': '1848-11-24',
  'The Verdigris Key': '1848-11-26',
  'The Chart Completed': '1848-11-28',
  Landfall: '1848-11-30'
}

/** Month the Calendar opens on, so the dated scenes are actually in view. */
export const CALENDAR_ANCHOR = '1848-11-01'
