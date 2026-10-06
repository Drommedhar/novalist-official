"""Resolve local classic scripts actually loaded by a checked HTML entry point."""

from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit


class _ClassicScripts(HTMLParser):
    def __init__(self) -> None:
        super().__init__()
        self.sources: list[str] = []
        self.inline: list[str] = []
        self._body: list[str] | None = None

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag != "script":
            return
        attributes = dict(attrs)
        if attributes.get("type", "").lower() not in (
            "",
            "text/javascript",
            "application/javascript",
        ):
            return
        source = attributes.get("src")
        if source:
            self.sources.append(source)
        else:
            self._body = []

    def handle_data(self, data: str) -> None:
        if self._body is not None:
            self._body.append(data)

    def handle_endtag(self, tag: str) -> None:
        if tag == "script" and self._body is not None:
            self.inline.append("".join(self._body))
            self._body = None


def classic_script_paths(entry: Path) -> list[Path]:
    """Include inline code in the entry, then its local classic scripts.

    Missing references raise during the caller's read, so extracting code does
    not let broken script paths silently disappear from the source checks.
    Module scripts cannot implement the editor's global bridge functions.
    """
    parser = _ClassicScripts()
    parser.feed(entry.read_text(encoding="utf-8"))
    result = [entry]
    for source in parser.sources:
        url = urlsplit(source)
        if not url.scheme and not url.netloc:
            path = (entry.parent / unquote(url.path)).resolve()
            if path not in result:
                result.append(path)
    return result


def extracted_script_lines(previous: str, entry: Path) -> int:
    """Credit only verbatim moves from inline scripts to referenced files.

    This lets the truncation check recognize extraction without exempting an
    HTML file or counting unrelated libraries toward its retained contents.
    """
    parser = _ClassicScripts()
    parser.feed(previous)
    referenced = set()
    for path in classic_script_paths(entry)[1:]:
        if path.is_file():
            referenced.add(path.read_text(encoding="utf-8").strip())
    retained = 0
    for body in parser.inline:
        if body.strip() and body.strip() in referenced:
            retained += body.count("\n")
            referenced.remove(body.strip())
    return retained
