"""Detect transliterated text and damaged locale encodings."""

import re

# -- Spelling: a language written in the wrong letters -------------------------
#
# Two ways a translated string stops being that language, both of which have
# shipped:
#
# 1. Transliteration. German umlauts typed as digraphs - "Oeffnen", "wofuer",
#    "ausgewaehlt". It reads as a spelling mistake to the person the string is
#    for, and it spreads: the next person to add a string copies the file's
#    apparent convention from whatever line they happened to read first.
# 2. Mojibake. UTF-8 read back as Latin-1 ("A¤", "A¼") or a character lost to a
#    replacement glyph, from a tool in the middle that was not told the encoding.
#
# The transliteration list is stems that no correctly-spelled German word
# contains, rather than a bare "ae|oe|ue" search - "neue", "Quelle", "Trauer",
# "aktuell" and "Feuerwache" are all fine and all contain one of those pairs.
TRANSLITERATED_STEMS = (
    "ueber",
    "ueck",
    "uess",
    "uehr",
    "fuer",
    "oeffn",
    "loesch",
    "waehl",
    "aender",
    "moecht",
    "koenn",
    "muess",
    "groess",
    "schliess",
    "hinzufueg",
    "buech",
    "woert",
    "naechst",
    "spaet",
    "frueh",
    "schaetz",
    "haeng",
    "gewoehnl",
    "luecke",
    "kuest",
    "laesst",
    "geloescht",
    "aufraeum",
    "zurueck",
    "haett",
    "koerper",
    "hoehe",
    "fuell",
    "pruef",
    "erklaer",
    "waehr",
    "verfuegb",
    "zufaell",
    "aehnlich",
)
TRANSLITERATED_RE = re.compile(
    r"[A-Za-z]*(?:" + "|".join(TRANSLITERATED_STEMS) + r")[A-Za-z]*", re.IGNORECASE
)

# Latin-1-read-as-UTF-8 wreckage, plus the replacement character itself.
MOJIBAKE_RE = re.compile("[ÃÂ][-¿]|�")

# Locales whose text is expected to carry Latin diacritics. The mojibake half of
# the check applies to every locale, English included - a curly quote or an em
# dash is mangled by exactly the same mistake.
DIACRITIC_LOCALES = {"de"}


def spelling_faults(en: dict, others: dict) -> list:
    """Strings written in the wrong letters for their language."""
    faults: list = []
    for lang, loc in [("en", en)] + sorted(others.items()):
        for key, value in loc.items():
            if not isinstance(value, str):
                continue
            broken = MOJIBAKE_RE.findall(value)
            if broken:
                faults.append(f"{lang}::{key}  mangled encoding: {broken[:3]}")
            if lang in DIACRITIC_LOCALES:
                for word in TRANSLITERATED_RE.findall(value):
                    faults.append(
                        f"{lang}::{key}  transliterated: {word!r} - write the real letters"
                    )
    return faults
