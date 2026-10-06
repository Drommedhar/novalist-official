// aislop-ignore-file code-quality/duplicate-block -- Fictional chapter fixtures intentionally share a schema; titles, prose, and scene metadata are distinct data.
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
