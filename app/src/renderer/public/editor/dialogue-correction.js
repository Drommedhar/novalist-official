function correctGermanDialogue(text, oq, cq, verbs) {
    let changed = false;
    let result = text;

    const verbPattern = verbs.map(v => escapeRegex(v)).join('|');
    const cqE = escapeRegex(cq);
    const oqE = escapeRegex(oq);

    // Pass 1: Period inside closing quote before verb → remove period, comma outside
    // „Text." sagte er → „Text", sagte er
    const p1 = new RegExp(
        oqE + '([^' + cqE + ']*?)\\.' + cqE + '\\s*,?\\s*(' + verbPattern + ')\\b',
        'giu'
    );
    result = result.replace(p1, (m, inner, verb) => {
        changed = true;
        return oq + inner + cq + ', ' + verb.toLowerCase();
    });

    // Pass 2: Wrong period after closing quote → replace with comma
    // „Text?". sagte er → „Text?", sagte er
    const p2 = new RegExp(
        cqE + '\\.\\s*,?\\s*(' + verbPattern + ')\\b',
        'giu'
    );
    result = result.replace(p2, (m, verb) => {
        changed = true;
        return cq + ', ' + verb.toLowerCase();
    });

    // Pass 3: Missing comma after closing quote before verb
    // „Text" sagte er → „Text", sagte er
    // „Text?" sagte er → „Text?", sagte er
    // But NOT if comma already present: „Text", sagte → skip
    const p3 = new RegExp(
        cqE + '(?!,)\\s+(' + verbPattern + ')\\b',
        'giu'
    );
    result = result.replace(p3, (m, verb) => {
        changed = true;
        return cq + ', ' + verb.toLowerCase();
    });

    // Pass 4: Uppercase verb after comma
    // „Text", Sagte → „Text", sagte
    const p4 = new RegExp(
        cqE + ',\\s+(' + verbPattern + ')\\b',
        'giu'
    );
    result = result.replace(p4, (m, verb) => {
        const lower = verb.charAt(0).toLowerCase() + verb.slice(1);
        if (lower !== verb) {
            changed = true;
            return cq + ', ' + lower;
        }
        return m;
    });

    // Pass 5: Interrupted speech — add comma before continuation opening quote
    // sagte Noah „und... → sagte Noah, „und...
    // But NOT if period precedes opening quote (new sentence: sagte Liam. „Wir...)
    const p5 = new RegExp(
        '(' + verbPattern + ')(\\s+[^' + oqE + cqE + ']*?)\\s+(' + oqE + ')',
        'giu'
    );
    result = result.replace(p5, (m, verb, middle, nextOq) => {
        const trimmed = middle.trimEnd();
        if (trimmed.endsWith('.') || trimmed.endsWith(',')) return m;
        changed = true;
        return verb + trimmed + ', ' + nextOq;
    });

    return changed ? result : null;
}

function correctEnglishDialogue(text, oq, cq, verbs) {
    const verbPattern = verbs.map(escapeRegex).join('|');
    const close = escapeRegex(cq);
    const open = escapeRegex(oq);
    const rules = [
        // A sentence-ending period before a speech tag becomes a comma.
        [open + '([^' + close + ']*?)\\.' + close + '\\s*(' + verbPattern + ')\\b',
            (_match, inner, verb) => oq + inner + ',' + cq + ' ' + verb.toLowerCase()],
        // A comma belongs inside the quotes, except after a question or exclamation.
        [open + '([^' + close + ']*?)' + close + ',\\s*(' + verbPattern + ')\\b',
            (match, inner, verb) => {
                if (inner.endsWith(',')) return match;
                return oq + inner + (/[?!]$/.test(inner) ? '' : ',') + cq + ' ' + verb.toLowerCase();
            }],
        [open + '([^' + close + ']*?)' + close + '\\s+(' + verbPattern + ')\\b',
            (match, inner, verb) => /[,?!]$/.test(inner)
                ? match : oq + inner + ',' + cq + ' ' + verb.toLowerCase()],
        ['([?!])' + close + ',\\s*(' + verbPattern + ')\\b',
            (_match, punctuation, verb) => punctuation + cq + ' ' + verb.toLowerCase()],
        [close + '\\s+(' + verbPattern + ')\\b',
            (match, verb) => {
                const lower = verb.charAt(0).toLowerCase() + verb.slice(1);
                return lower === verb ? match : cq + ' ' + lower;
            }],
        // Interrupted speech needs a comma before its continuation.
        ['(' + verbPattern + ')(\\s+[^' + open + close + ']*?)\\s+(' + open + ')',
            (match, verb, middle, nextQuote) => {
                const trimmed = middle.trimEnd();
                return /[.,]$/.test(trimmed) ? match : verb + trimmed + ', ' + nextQuote;
            }]
    ];
    const corrected = rules.reduce((value, [pattern, replace]) => value.replace(new RegExp(pattern, 'giu'), replace), text);
    return corrected === text ? null : corrected;
}

function escapeRegex(str) {
    return str.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

window.NovalistDialogue = { correctGermanDialogue, correctEnglishDialogue };
