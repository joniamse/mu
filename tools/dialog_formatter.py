#!/usr/bin/env python3
"""Formatea los diálogos de NPC del cliente: ortografía limpia y tono de fantasía oscura.

Lee el archivo de diálogos del cliente MuMain (src/Localization/Dialog.<idioma>.resx, del que
se generan los textos I18N::Dialog), y para las ciudades elegidas (Lorencia, Noria y Devias por
defecto):

1. Reemplaza los textos por las reescrituras del catálogo (tools/dialog_catalog.<idioma>.json),
   escritas con un tono inmersivo de fantasía oscura y de longitud parecida al original.
2. Pasa una limpieza ortográfica a esos textos (espacios, puntuación, tildes mal puestas,
   restos de traducción como "Y?Semeden") y, con --clean-all, a todo el archivo.
3. Agrega las claves del catálogo que falten en el archivo (p. ej. Text_0 en español).
4. Valida que no se pierdan marcadores (%d, %s, {0}...) y avisa de signos ¿? ¡! sin pareja y de
   textos demasiado largos para la ventana de diálogo.

Por defecto solo muestra las diferencias; con --write guarda el archivo (y un .bak del original).
Después hay que recompilar el cliente: los textos se compilan dentro de Main.exe.

Uso:
    python tools/dialog_formatter.py                     # vista previa (diff)
    python tools/dialog_formatter.py --write             # aplica los cambios
    python tools/dialog_formatter.py --cities devias --report cambios.md
    python tools/dialog_formatter.py --clean-all --no-rewrite --write   # solo ortografía, todo el archivo

Solo usa la biblioteca estándar de Python.
"""

from __future__ import annotations

import argparse
import difflib
import json
import re
import shutil
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path
from xml.sax.saxutils import escape

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_RESX = REPO_ROOT / "client-src" / "MuMain" / "src" / "Localization" / "Dialog.{lang}.resx"
DEFAULT_CATALOG = Path(__file__).resolve().parent / "dialog_catalog.{lang}.json"
DEFAULT_CITIES = ("lorencia", "noria", "devias")

# Por encima de esto el texto puede no entrar en la ventana de diálogo del cliente.
MAX_DIALOG_LENGTH = 320
MAX_ANSWER_LENGTH = 60

PLACEHOLDER = re.compile(r"%l?[dsuxXfc]|%\d*\.?\d*[dfs]|\{\d+\}|\\n")

# Where the diff breaks a text into lines: between sentences.
SENTENCE_BREAK = re.compile(r"(?<=[.!?])\s+(?=[¿¡'A-ZÁÉÍÓÚÑ])")

# Errores de ortografía y de traducción vistos en los textos del cliente (español).
TYPOS_ES = {
    r"\bpiedrá\b": "piedra",
    r"\bvió\b": "vio",
    r"\bdió\b": "dio",
    r"\bfué\b": "fue",
    r"\bfuí\b": "fui",
    r"\bguión\b": "guion",
    r"\bAtlance\b": "Atlans",
    r"\bAquél que\b": "Aquel que",
    r"\baquél que\b": "aquel que",
    r"\bjunto con el\.": "junto con él.",
}


@dataclass
class Change:
    key: str
    before: str | None
    after: str
    reason: str


def clean_text(text: str, lang: str) -> str:
    """Spelling and punctuation cleanup, conservative: it never changes the wording."""
    cleaned = re.sub(r"\s+", " ", text).strip()
    # Translation leftovers: a "?" glued between two words ("Y?Semeden").
    cleaned = re.sub(r"(?<=\w)\?(?=[A-ZÁÉÍÓÚÑ])", " ", cleaned)
    # No space before punctuation, one space after it.
    cleaned = re.sub(r"\s+([,.;:!?])", r"\1", cleaned)
    # Only before a word, so "http://" and "10:30" stay as they are.
    cleaned = re.sub(r"([,;:])(?=[A-Za-zÁÉÍÓÚÑáéíóúñ¿¡'])", r"\1 ", cleaned)
    # "!." / "?." at the end of a sentence, and runs of dots that aren't an ellipsis.
    cleaned = re.sub(r"([!?])\.(?!\.)", r"\1", cleaned)
    cleaned = re.sub(r"(?<!\.)\.\.(?!\.)", ".", cleaned)
    cleaned = re.sub(r"\.{4,}", "...", cleaned)
    if lang == "es":
        for pattern, replacement in TYPOS_ES.items():
            cleaned = re.sub(pattern, replacement, cleaned)
        # Laughter written the Spanish way.
        cleaned = re.sub(r"\bJa ja ja\b", "Je, je, je", cleaned)
    return cleaned


def check_text(key: str, text: str, original: str | None, lang: str) -> list[str]:
    """Warnings that need a human: placeholders, unpaired marks, length."""
    warnings: list[str] = []
    if original is not None and sorted(PLACEHOLDER.findall(original)) != sorted(PLACEHOLDER.findall(text)):
        warnings.append(f"{key}: cambian los marcadores {PLACEHOLDER.findall(original)} -> {PLACEHOLDER.findall(text)}")
    if lang == "es":
        if text.count("¿") != text.count("?"):
            warnings.append(f"{key}: signos de interrogación sin pareja (¿ {text.count('¿')}, ? {text.count('?')})")
        if text.count("¡") != text.count("!"):
            warnings.append(f"{key}: signos de exclamación sin pareja (¡ {text.count('¡')}, ! {text.count('!')})")
    limit = MAX_ANSWER_LENGTH if key.startswith("Answer_") else MAX_DIALOG_LENGTH
    if len(text) > limit:
        warnings.append(f"{key}: {len(text)} caracteres, puede no entrar en la ventana (máximo sugerido {limit})")
    return warnings


def read_values(path: Path) -> dict[str, str]:
    root = ET.parse(path).getroot()
    return {data.get("name"): data.findtext("value") or "" for data in root.findall("data")}


def key_order(key: str) -> tuple[int, int, str]:
    numbers = [int(n) for n in re.findall(r"\d+", key)]
    return (numbers[0] if numbers else -1, numbers[1] if len(numbers) > 1 else -1, key)


def read_comments(path: Path) -> dict[str, str]:
    if not path.is_file():
        return {}
    root = ET.parse(path).getroot()
    return {data.get("name"): data.findtext("comment") or "" for data in root.findall("data")}


def apply_changes(content: str, changes: list[Change], newline: str, comments: dict[str, str]) -> str:
    """Replaces the values in the resx text itself, so the rest of the file stays byte for byte.

    A new key gets the comment of the reference language (e.g. ``legacy_id=0``), which the
    client's resource generator uses to map the old numeric dialog ids.
    """
    for change in changes:
        value = escape(change.after)
        pattern = re.compile(r'(<data name="' + re.escape(change.key) + r'"[^>]*>\s*<value>)(.*?)(</value>)', re.DOTALL)
        if change.before is not None:
            content, count = pattern.subn(lambda m: m.group(1) + value + m.group(3), content, count=1)
            if count != 1:
                raise RuntimeError(f"No se encontró {change.key} en el archivo")
            continue

        # A missing key goes in front of the first data element that sorts after it.
        element = (
            f'  <data name="{change.key}" xml:space="preserve">{newline}'
            f"    <value>{value}</value>{newline}"
            f"    <comment>{escape(comments.get(change.key) or 'agregado por tools/dialog_formatter.py')}</comment>{newline}"
            f"  </data>{newline}"
        )
        names = re.findall(r'<data name="([^"]+)"', content)
        following = next((n for n in names if n.startswith(("Text_", "Answer_")) and key_order(n) > key_order(change.key)), None)
        if following is None:
            content = content.replace("</root>", element + "</root>", 1)
        else:
            index = content.index(f'  <data name="{following}"')
            content = content[:index] + element + content[index:]
    return content


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--lang", default="es", help="idioma del archivo (por defecto es)")
    parser.add_argument("--resx", type=Path, help="archivo Dialog.<idioma>.resx (por defecto el del cliente MuMain)")
    parser.add_argument("--catalog", type=Path, help="catálogo de reescrituras (por defecto tools/dialog_catalog.<idioma>.json)")
    parser.add_argument("--cities", default=",".join(DEFAULT_CITIES), help="ciudades a reescribir, separadas por coma")
    parser.add_argument("--no-rewrite", action="store_true", help="no aplica el catálogo, solo limpia la ortografía")
    parser.add_argument("--clean-all", action="store_true", help="limpia la ortografía de todos los textos del archivo")
    parser.add_argument("--write", action="store_true", help="guarda los cambios (con copia .bak del original)")
    parser.add_argument("--report", type=Path, help="guarda un informe Markdown con antes y después")
    parser.add_argument("--quiet", action="store_true", help="no muestra el diff")
    args = parser.parse_args(argv)

    resx = args.resx or Path(str(DEFAULT_RESX).format(lang=args.lang))
    if not resx.is_file():
        print(f"No existe {resx}", file=sys.stderr)
        return 2

    values = read_values(resx)
    planned: dict[str, tuple[str, str]] = {}

    if not args.no_rewrite:
        catalog_path = args.catalog or Path(str(DEFAULT_CATALOG).format(lang=args.lang))
        if not catalog_path.is_file():
            print(f"No existe el catálogo {catalog_path}", file=sys.stderr)
            return 2
        catalog = json.loads(catalog_path.read_text(encoding="utf-8"))["cities"]
        for city in (c.strip().lower() for c in args.cities.split(",") if c.strip()):
            if city not in catalog:
                print(f"La ciudad '{city}' no está en el catálogo ({', '.join(catalog)})", file=sys.stderr)
                return 2
            for key, text in catalog[city]["entries"].items():
                planned[key] = (text, f"reescritura ({catalog[city]['name']})")

    if args.clean_all:
        for key, text in values.items():
            if key.startswith(("Text_", "Answer_")):
                planned.setdefault(key, (text, "ortografía"))

    changes: list[Change] = []
    warnings: list[str] = []
    for key in sorted(planned, key=key_order):
        text, reason = planned[key]
        before = values.get(key)
        after = clean_text(text, args.lang)
        warnings.extend(check_text(key, after, before, args.lang))
        if before is None:
            changes.append(Change(key, None, after, reason + ", clave nueva"))
        elif after != before:
            changes.append(Change(key, before, after, reason))

    if not args.quiet:
        for change in changes:
            print(f"--- {change.key} [{change.reason}]")
            before = SENTENCE_BREAK.split(change.before or "")
            after = SENTENCE_BREAK.split(change.after)
            for line in difflib.unified_diff(before, after, lineterm="", n=0):
                if not line.startswith(("---", "+++", "@@")):
                    print("   " + line)

    for warning in warnings:
        print(f"AVISO {warning}")

    if args.report:
        lines = [f"# Diálogos de NPC: {resx.name}", "", f"{len(changes)} cambios.", ""]
        for change in changes:
            lines += [f"## {change.key} ({change.reason})", "", f"- Antes: {change.before or '(no existía)'}", f"- Después: {change.after}", ""]
        if warnings:
            lines += ["## Avisos", ""] + [f"- {w}" for w in warnings] + [""]
        args.report.write_text("\n".join(lines), encoding="utf-8")
        print(f"Informe: {args.report}")

    print(f"\n{len(changes)} cambios, {len(warnings)} avisos en {resx}")
    if not args.write:
        if changes:
            print("Vista previa: agrega --write para guardar.")
        return 0

    if changes:
        raw = resx.read_bytes()
        bom = raw.startswith(b"\xef\xbb\xbf")
        content = raw.decode("utf-8-sig")
        newline = "\r\n" if "\r\n" in content else "\n"
        reference = resx.with_name(re.sub(r"\.[^.]+\.resx$", ".en.resx", resx.name))
        updated = apply_changes(content, changes, newline, read_comments(reference))
        ET.fromstring(updated.encode("utf-8"))  # still well-formed
        backup = resx.with_suffix(resx.suffix + ".bak")
        if not backup.exists():
            shutil.copy2(resx, backup)
        resx.write_bytes((b"\xef\xbb\xbf" if bom else b"") + updated.encode("utf-8"))
        print(f"Guardado. Original en {backup}. Recompila el cliente para ver los textos nuevos.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
