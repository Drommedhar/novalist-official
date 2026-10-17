'use strict';

globalThis.NovalistQuoteContext = (() => {
    const wordCharacter = /[\p{L}\p{M}\p{N}]/u;
    const openingBoundary = /[\s([{\u2013\u2014:]/u;
    const closingBoundary = /[\s)\]},.!?;:\u2013\u2014]/u;

    function count(text, token) {
        if (!token) return 0;
        let total = 0;
        for (let at = text.indexOf(token); at !== -1; at = text.indexOf(token, at + token.length)) {
            const before = Array.from(text.slice(0, at)).at(-1) || '';
            const after = Array.from(text.slice(at + token.length))[0] || '';
            if (!wordCharacter.test(before) || !wordCharacter.test(after)) total++;
        }
        return total;
    }

    function replace(pair, before, typed, after) {
        const line = before + typed;
        if (!pair.start || pair.start !== pair.end || pair.startReplace === pair.endReplace ||
            !line.endsWith(pair.start) || pair.start.length < typed.length) return null;

        const prefix = line.slice(0, -pair.start.length);
        const previous = Array.from(prefix).at(-1) || '';
        const next = Array.from(after)[0] || '';
        const open = count(prefix, pair.startReplace) > count(prefix, pair.endReplace);
        const backtrack = pair.start.length - typed.length;

        if (pair.start !== "'" && pair.start !== '"') {
            return { replacement: open ? pair.endReplace : pair.startReplace, backtrack };
        }
        if (wordCharacter.test(previous) && wordCharacter.test(next)) {
            return { replacement: typed, backtrack: 0 };
        }
        if ((!previous || openingBoundary.test(previous)) && (!next || !/\s/u.test(next))) {
            return { replacement: pair.startReplace, backtrack };
        }
        if (open && previous && !/\s/u.test(previous) && (!next || closingBoundary.test(next))) {
            return {
                replacement: pair.endReplace,
                backtrack,
                // The next typed letter can reveal an apostrophe inside this quotation.
                provisional: pair.start === "'" && wordCharacter.test(previous) && !next
            };
        }
        return { replacement: typed, backtrack: 0 };
    }

    function continueWord(pending, before, typed) {
        if (!pending || before !== pending.before || !wordCharacter.test(Array.from(typed)[0] || '')) return null;
        return { replacement: "'" + typed, backtrack: pending.length };
    }

    return { replace, continueWord };
})();
