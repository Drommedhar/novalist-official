'use strict';

// ── Entity Detection (Focus Peek) ───────────────────────────────

function setEntityNames(namesJson) {
    try {
        const parsed = JSON.parse(namesJson);
        window.NovalistEditorState.entityIndex = new Map();
        window.NovalistEditorState.entityMatchRules = new Map();

        if (parsed.length === 0) {
            window.NovalistEditorState.entityNames = [];
            window.NovalistEditorState.entityRegex = null;
            return;
        }

        // Accept both legacy ["Name", ...] and richer [{name, entityId, entityType, isAlias}, ...]
        if (typeof parsed[0] === 'string') {
            window.NovalistEditorState.entityNames = parsed;
        } else {
            window.NovalistEditorState.entityNames = parsed.map(p => p.name);
            for (const rec of parsed) {
                if (!rec || !rec.name) continue;
                const key = String(rec.name).toLowerCase();
                if (!window.NovalistEditorState.entityIndex.has(key)) {
                    window.NovalistEditorState.entityIndex.set(key, {
                        entityId: rec.entityId || '',
                        entityType: rec.entityType || '',
                        isAlias: !!rec.isAlias
                    });
                }
                // Only entries that actually customised something get a rule, so
                // the common path stays a single map miss.
                if (rec.caseSensitive || (rec.exclusions && rec.exclusions.length > 0)) {
                    window.NovalistEditorState.entityMatchRules.set(key, {
                        caseSensitive: !!rec.caseSensitive,
                        exact: String(rec.name),
                        exclusions: (rec.exclusions || []).map(e => String(e).toLowerCase()).filter(e => e.length > 0)
                    });
                }
            }
        }

        const escaped = window.NovalistEditorState.entityNames
            .slice()
            .sort((a, b) => b.length - a.length)
            .map(n => n.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
        window.NovalistEditorState.entityRegex = new RegExp('(?<![\\p{L}\\p{N}])(' + escaped.join('|') + ')(?![\\p{L}\\p{N}])', 'iu');
    } catch {
        window.NovalistEditorState.entityRegex = null;
    }
}

// The regex always searches case-insensitively so a hit can be found at all;
// these are the per-entry rules that decide whether the hit counts. Anything
// without a rule is allowed, which is exactly the old behaviour.
function entityHitAllowed(matched, contextText) {
    const rule = window.NovalistEditorState.entityMatchRules.get(String(matched).toLowerCase());
    if (!rule) return true;
    if (rule.caseSensitive && matched !== rule.exact) return false;
    if (rule.exclusions.length > 0 && contextText) {
        const haystack = contextText.toLowerCase();
        for (const phrase of rule.exclusions) {
            if (haystack.indexOf(phrase) !== -1) return false;
        }
    }
    return true;
}

Object.assign(window, {
    setEntityNames,
    entityHitAllowed
});
