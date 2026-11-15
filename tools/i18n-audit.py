#!/usr/bin/env python3
r"""i18n audit for the Linux/Avalonia tree (repo root: src/TipCL.App + src/TipCL.Core).

Why this exists
---------------
The repo ships `--i18n-check` (windos/src/TipCL/I18nCheck.cs), but it is Windows-only
and its source-tree candidates are hard-coded to `<TIPCL_HOME>/src/TipCL` or
`<TIPCL_HOME>/wpf/TipCL` (I18nCheck.cs:26-28). It therefore cannot measure the Linux
port at all, and with an isolated TIPCL_HOME it exits 2 with `没有源码树: ...\wpf\TipCL`.
This script applies the same criterion to the Linux tree instead of trusting a
paraphrased list (DECISIONS.md D-33).

Criterion (deliberately identical to I18nCheck.HasCjk / IsWrapped, adapted wrappers)
------------------------------------------------------------------------------------
CJK  = any char in U+4E00..U+9FFF, U+3400..U+4DBF, U+3000..U+303F, U+FF00..U+FFEF
       (exactly I18nCheck.HasCjk, I18nCheck.cs:145-155).
Wrapped = the string literal is an argument of a call whose callee name is in WRAPPERS
       below. On the Linux/Avalonia side there is exactly ONE real word-table lookup:
       `I18nCore.Tr` (`I18nCore.cs:135`, plus the `Tr` alias at `Ai/Preview.cs:602`).
       I18nCheck.IsWrapped only accepts `L` / `*.L`, which is the Windows tree's
       convention and does NOT transfer here - see the WRAPPERS comment for the
       verified counter-examples.
Ignored = the line contains `i18n:ignore`, or one of the file's first 10 lines contains
       `i18n:ignore-file` (I18nCheck.cs:157-173, :195-198).

DENOMINATOR / DIRECTION (read this before quoting any number - see R30-WP3b 8)
------------------------------------------------------------------------------
  cjk_literals = the DENOMINATOR: every CJK string literal occurrence in the scanned
                 tree (occurrences, not distinct values; distinct is reported separately).
  wrapped      = literals that GO THROUGH the word table  -> changing the language CAN
                 change them.
  unwrapped    = literals that do NOT                       -> changing the language
                 CANNOT change them (this is the backlog).
  coverage     = wrapped / cjk_literals.

  => `coverage` is the share that IS wired. It is NOT "how much of the UI is
     translated" and it must NOT be read backwards: a LOW coverage means MOST strings
     are unwired. Measured on this tree (2026-11-15, R30-WP3d):
       cjk_literals 4607 | wrapped 122 (2.65%) | unwrapped 4485 (97.35%)
     i.e. 97.35% of the CJK literals do not consult the word table at all, so the
     interface keeps showing Chinese even with language=en.

Reported numbers
----------------
  cjk_literals        every CJK string literal occurrence in the scanned tree
  wrapped             CJK literals that go through a wrapper (wired, 2.65%)
  unwrapped           CJK literals that do not (the localisation backlog, 97.35%)
  coverage            wrapped / cjk_literals  <-- the SHARE THAT IS WIRED
  wrapped_keys        distinct wrapper argument values (the D-33 "key" universe)
  missing_en/_zh      wrapped_keys absent from that table  <-- the D-33 actionable 差集

Usage:
  python tools/i18n-audit.py                 # human summary
  python tools/i18n-audit.py --help          # criterion + denominator/口径
  python tools/i18n-audit.py --json          # machine-readable
  python tools/i18n-audit.py --list-unwrapped --top-files 25
"""

import json
import os
import re
import sys
from collections import Counter

# Callee names that count as a localisation wrapper on the Linux/Avalonia side.
#
# CORRECTED in R30-WP3d. The original set was {"Tr", "L", "Gt", "T", } which
# over-counted: it produced 139 wrapped (3.02%) instead of the true 122 (2.65%),
# because it matched four callee names that are NOT word-table lookups at all.
# Verified against the tree (R30-WP3b 8.2):
#
#   Tr   -> I18nCore.cs:135  Tr(key, lang, dflt)          = REAL word-table lookup
#           Ai/Preview.cs:602 `Tr(key) => I18nCore.Tr(key)` = alias of the above
#   L    -> Py.cs:91         `L(params object[])`          = python-style LIST constructor
#           Manifest.cs:360  `L(object)`                   = list coercion helper
#           Conflict.cs:337  `L(object)`                   = string coercion helper
#           Layout/PageLayoutIO.cs:12 `LayoutText.L(key)`  = IDENTITY STUB: body is
#                                                            `return key;` - it never
#                                                            consults the word table
#   Gt   -> Pages/Settings/SettingsPage.cs:453 `Gt(t) => UiClass.GroupTitle(t)`
#                                                          = group-title widget builder
#           Launcher/Mods.cs:1305 / Modpack.cs:1136 `Gt(o, k)` = dict getter
#   T    -> no definition and no call site anywhere in the Linux tree
#
# So the strict, verified set is exactly {"Tr"}. Anything added here must be a
# *proven* word-table lookup (read the callee's body, do not guess from the name);
# a wrong entry makes this tool report a nicer coverage than reality, which is worse
# than having no tool at all - the next person uses this to plan the P4 wiring work.
WRAPPERS = {"Tr"}

HELP = r"""i18n audit (Linux/Avalonia tree)

Criterion
  CJK literal  = a string literal containing a char in U+4E00..U+9FFF, U+3400..U+4DBF,
                 U+3000..U+303F or U+FF00..U+FFEF (same ranges as I18nCheck.HasCjk).
  wrapped      = the literal is an argument of a call whose callee is in WRAPPERS.
                 WRAPPERS is {"Tr"} = I18nCore.Tr (I18nCore.cs:135) only, because that
                 is the only *verified* word-table lookup in this tree. Do not add a
                 name from the Windows tree without reading its body first: `L` here is
                 mostly Py.L / coercion helpers and LayoutText.L is `return key;`,
                 `Gt` is a group-title builder or a dict getter.

Denominator / direction - READ THIS BEFORE QUOTING A NUMBER
  cjk_literals = DENOMINATOR: every CJK literal occurrence in src/TipCL.App + src/TipCL.Core.
  wrapped      = literals that DO consult the word table  -> language switching can affect them.
  unwrapped    = literals that do NOT                      -> language switching cannot affect them.
  coverage     = wrapped / cjk_literals = the share that IS wired.

  coverage is NOT "how much of the UI is translated", and it must not be read backwards.
  Last measured 2026-11-15 (R30-WP3d):
      cjk_literals 4607 | wrapped 122 (2.65%) | unwrapped 4485 (97.35%)
  => 97.35% of the CJK literals never consult the word table, which is why the UI stays
     Chinese even with language=en. See progress/R30-WP3b.md 8 for the reconciliation.

Options
  --json             machine-readable output
  --out PATH         write the report to PATH instead of stdout
  --list-unwrapped   also list every unwrapped literal with file:line
  --top-files N      how many files to list in the "unwrapped by file" table (default 20)
  --help, -h         this text
"""

CJK_RANGES = (
    (0x4E00, 0x9FFF),
    (0x3400, 0x4DBF),
    (0x3000, 0x303F),
    (0xFF00, 0xFFEF),
)


def has_cjk(s: str) -> bool:
    for ch in s:
        o = ord(ch)
        for lo, hi in CJK_RANGES:
            if lo <= o <= hi:
                return True
    return False


ESCAPES = {"n": "\n", "r": "\r", "t": "\t", "0": "\0", "a": "\a",
           "b": "\b", "f": "\f", "v": "\v", "\\": "\\", '"': '"', "'": "'"}


class Lexer:
    """String-literal scanner following I18nCheck.ScanCode / ScanString."""

    def __init__(self, text: str):
        self.s = text
        self.n = len(text)
        self.lits = []  # (start_offset, value)

    def run(self):
        self._code(0, self.n)
        return self.lits

    def _code(self, i, end):
        s = self.s
        while i < end:
            c = s[i]
            if c == "/" and i + 1 < end and s[i + 1] == "/":
                while i < end and s[i] != "\n":
                    i += 1
                continue
            if c == "/" and i + 1 < end and s[i + 1] == "*":
                j = s.find("*/", i + 2)
                i = end if j < 0 else j + 2
                continue
            if c == "'":
                i += 1
                while i < end and s[i] != "'":
                    if s[i] == "\\":
                        i += 1
                    i += 1
                i += 1
                continue
            if c == '"' or (c == "@" and i + 1 < end and s[i + 1] == '"') \
                    or (c == "$" and i + 1 < end and (s[i + 1] == '"'
                        or (s[i + 1] == "@" and i + 2 < end and s[i + 2] == '"'))) \
                    or (c == "@" and i + 1 < end and s[i + 1] == "$" and i + 2 < end
                        and s[i + 2] == '"'):
                i = self._string(i, end)
                continue
            i += 1

    def _string(self, start, end):
        s = self.s
        i = start
        verbatim = interpolated = False
        while i < end and (s[i] == "@" or s[i] == "$"):
            if s[i] == "@":
                verbatim = True
            else:
                interpolated = True
            i += 1
        # raw string literal (C# 11): """ ... """
        if i + 2 < end and s[i] == '"' and s[i + 1] == '"' and s[i + 2] == '"':
            j = i
            while j < end and s[j] == '"':
                j += 1
            fence = '"' * (j - i)
            close = s.find(fence, j)
            if close < 0:
                close = end
            value = s[j:close]
            if interpolated:
                value = value.replace("{{", "{").replace("}}", "}")
            self.lits.append((start, value.strip("\r\n ")))
            return min(end, close + len(fence))
        i += 1
        out = []
        while i < end:
            c = s[i]
            if c == '"':
                if not verbatim or i + 1 >= end or s[i + 1] != '"':
                    i += 1
                    break
                out.append('"')
                i += 2
                continue
            if not verbatim and c == "\\" and i + 1 < end:
                nxt = s[i + 1]
                if nxt == "u" and i + 6 <= self.n:
                    try:
                        out.append(chr(int(s[i + 2:i + 6], 16)))
                        i += 6
                        continue
                    except ValueError:
                        pass
                elif nxt == "U" and i + 10 <= self.n:
                    try:
                        out.append(chr(int(s[i + 2:i + 10], 16)))
                        i += 10
                        continue
                    except ValueError:
                        pass
                elif nxt == "x":
                    m = re.match(r"x[0-9A-Fa-f]{1,4}", s[i + 1:])
                    if m:
                        out.append(chr(int(m.group(0)[1:], 16)))
                        i += 1 + len(m.group(0)) - 1
                        continue
                out.append(ESCAPES.get(nxt, nxt))
                i += 2
                continue
            if interpolated and c == "{":
                if i + 1 < end and s[i + 1] == "{":
                    out.append("{")
                    i += 2
                    continue
                # skip the interpolation hole; remember it as {}
                depth = 0
                j = i + 1
                while j < end:
                    d = s[j]
                    if d == "{":
                        depth += 1
                    elif d == "}":
                        if depth == 0:
                            break
                        depth -= 1
                    elif d == '"':
                        j = self._skip_nested_string(j, end)
                        continue
                    j += 1
                out.append("{}")
                i = min(end, j + 1)
                continue
            if interpolated and c == "}" and i + 1 < end and s[i + 1] == "}":
                out.append("}")
                i += 2
                continue
            out.append(c)
            i += 1
        self.lits.append((start, "".join(out)))
        return i

    def _skip_nested_string(self, j, end):
        # scan a nested string inside an interpolation hole; returns index of closing quote
        s = self.s
        j += 1
        if j + 1 < end and s[j] == "{":
            return j
        while j < end and s[j] != '"':
            if s[j] == "\\":
                j += 1
            j += 1
        return j


CALLEE_RE = re.compile(r"[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")


def callee_before(text: str, start: int):
    """Mirror I18nCheck.IsWrapped: walk back over ws, expect '(', read dotted ident."""
    i = start - 1
    while i >= 0 and text[i] in " \t\r\n":
        i -= 1
    if i < 0 or text[i] != "(":
        return None
    i -= 1
    while i >= 0 and text[i] in " \t\r\n":
        i -= 1
    if i < 0:
        return None
    j = i
    while j >= 0 and (text[j].isalnum() or text[j] in "_."):
        j -= 1
    name = text[j + 1:i + 1]
    return name or None


def iter_cs_files(root):
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in ("obj", "bin", ".git")]
        for fn in sorted(filenames):
            if fn.endswith(".cs"):
                yield os.path.join(dirpath, fn)


def is_file_ignored(text: str) -> bool:
    for line in text.split("\n")[:10]:
        if "i18n:ignore-file" in line:
            return True
    return False


def line_of(text: str, offset: int) -> int:
    return text.count("\n", 0, offset) + 1


def scan_file(path, root):
    with open(path, encoding="utf-8", errors="replace") as fh:
        text = fh.read()
    rel = os.path.relpath(path, root).replace("\\", "/")
    if is_file_ignored(text):
        return rel, [], True
    lines = text.split("\n")
    rows = []
    for start, value in Lexer(text).run():
        if not has_cjk(value):
            continue
        ln = line_of(text, start)
        ignored = "i18n:ignore" in lines[ln - 1] if 0 < ln <= len(lines) else False
        callee = callee_before(text, start)
        wrapped = callee is not None and callee.split(".")[-1] in WRAPPERS
        rows.append({
            "file": rel, "line": ln, "text": value,
            "callee": callee, "wrapped": wrapped, "ignored": ignored,
        })
    return rel, rows, False


def main(argv):
    if "--help" in argv or "-h" in argv:
        print(HELP)
        return 0

    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.abspath(os.path.join(here, ".."))
    src_dirs = [os.path.join(root, "src", "TipCL.App"),
                os.path.join(root, "src", "TipCL.Core")]
    locales_dir = os.path.join(root, "src", "TipCL.Core", "Resources", "locales")

    as_json = "--json" in argv
    show_unwrapped = "--list-unwrapped" in argv
    out_path = None
    if "--out" in argv:
        out_path = argv[argv.index("--out") + 1]
    top_files = 20
    if "--top-files" in argv:
        top_files = int(argv[argv.index("--top-files") + 1])

    tables, raw = {}, {}
    for lang in ("en", "zh_CN"):
        p = os.path.join(locales_dir, lang + ".json")
        with open(p, encoding="utf-8") as fh:
            raw[lang] = fh.read()
        tables[lang] = json.loads(raw[lang])

    all_rows, ignored_files, files_scanned = [], [], 0
    for d in src_dirs:
        if not os.path.isdir(d):
            continue
        for f in iter_cs_files(d):
            rel, rows, ign = scan_file(f, root)
            if ign:
                ignored_files.append(rel)
                continue
            files_scanned += 1
            all_rows.extend(rows)

    wrapped = [r for r in all_rows if r["wrapped"]]
    unwrapped = [r for r in all_rows if not r["wrapped"] and not r["ignored"]]
    ignored_lines = [r for r in all_rows if r["ignored"] and not r["wrapped"]]

    wrapped_keys = sorted({r["text"] for r in wrapped})
    missing_en = [k for k in wrapped_keys if k not in tables["en"]]
    missing_zh = [k for k in wrapped_keys if k not in tables["zh_CN"]]

    kept = {
        "en": [k for k in tables["en"] if k in set(wrapped_keys)],
        "zh_CN": [k for k in tables["zh_CN"] if k in set(wrapped_keys)],
    }

    # Of the unwrapped literals, how many already HAVE a translation in the table?
    # Those are "translation available but the call site is not wired", which is a
    # different (and much cheaper) problem than "no translation exists".
    unwrapped_distinct = sorted({r["text"] for r in unwrapped})
    unwrapped_have_en = [k for k in unwrapped_distinct if k in tables["en"]]
    unwrapped_have_zh = [k for k in unwrapped_distinct if k in tables["zh_CN"]]

    unwrapped_files = Counter(r["file"] for r in unwrapped)
    total = len(all_rows)
    result = {
        "repo_root": root,
        "src_dirs": [os.path.relpath(d, root).replace("\\", "/") for d in src_dirs],
        "locales_dir": os.path.relpath(locales_dir, root).replace("\\", "/"),
        "files_scanned": files_scanned,
        "ignored_files": ignored_files,
        "table_sizes": {k: len(v) for k, v in tables.items()},
        "table_key_sets_equal": set(tables["en"]) == set(tables["zh_CN"]),
        "table_key_set_diff": sorted(set(tables["en"]) ^ set(tables["zh_CN"])),
        "cjk_literals": total,
        "wrapped": len(wrapped),
        "unwrapped": len(unwrapped),
        "ignored_lines": len(ignored_lines),
        "coverage": round(len(wrapped) / total, 4) if total else 0.0,
        "wrapped_keys": len(wrapped_keys),
        "missing_en": missing_en,
        "missing_en_count": len(missing_en),
        "missing_zh": missing_zh,
        "missing_zh_count": len(missing_zh),
        "unwrapped_by_file": dict(unwrapped_files.most_common(top_files)),
        "unwrapped_files_total": len(unwrapped_files),
        "unwrapped_distinct": len(unwrapped_distinct),
        "unwrapped_distinct_with_en_key": len(unwrapped_have_en),
        "unwrapped_distinct_with_zh_key": len(unwrapped_have_zh),
        "unwrapped_with_en_key": unwrapped_have_en,
        "callee_histogram": dict(Counter(
            (r["callee"] or "(none)") for r in all_rows).most_common(40)),
    }

    if as_json:
        text = json.dumps(result, ensure_ascii=False, indent=2)
        if out_path:
            with open(out_path, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(text + "\n")
        else:
            print(text)
        return 0

    L = []
    L.append("== i18n audit (Linux/Avalonia tree) ==")
    L.append("  src        : " + ", ".join(result["src_dirs"]))
    L.append("  locales    : " + result["locales_dir"])
    L.append("  files      : %d .cs (ignored %d)" % (files_scanned, len(ignored_files)))
    L.append("  tables     : en=%d zh_CN=%d  key sets equal: %s"
             % (len(tables["en"]), len(tables["zh_CN"]), result["table_key_sets_equal"]))
    L.append("  CJK literals: %d" % total)
    L.append("  wrapped     : %d   (%.2f%% - these DO consult the word table)"
             % (len(wrapped), 100.0 * result["coverage"]))
    L.append("  unwrapped   : %d   (%.2f%% - these do NOT, language switching cannot"
             " affect them; distinct %d, across %d files)"
             % (len(unwrapped), 100.0 * len(unwrapped) / total if total else 0.0,
                len(unwrapped_distinct), len(unwrapped_files)))
    L.append("  of the distinct unwrapped values, %d already have an en key and %d a zh_CN key"
             % (len(unwrapped_have_en), len(unwrapped_have_zh)))
    L.append("  => translation-available-but-unwired share of unwrapped: en %.2f%%"
             % (100.0 * len(unwrapped_have_en) / len(unwrapped_distinct) if unwrapped_distinct else 0.0))
    L.append("  ignored lines: %d" % len(ignored_lines))
    L.append("  wrapped keys : %d" % len(wrapped_keys))
    L.append("  missing en   : %d" % len(missing_en))
    L.append("  missing zh   : %d" % len(missing_zh))
    for k in missing_en:
        L.append("      en- %s" % k[:120])
    for k in missing_zh:
        L.append("      zh- %s" % k[:120])
    L.append("  unwrapped by file (top %d of %d):" % (top_files, len(unwrapped_files)))
    for f, n in unwrapped_files.most_common(top_files):
        L.append("      %5d  %s" % (n, f))
    if show_unwrapped:
        L.append("  unwrapped literals:")
        for r in sorted(unwrapped, key=lambda r: (r["file"], r["line"])):
            L.append("      %s:%d  %s" % (r["file"], r["line"], r["text"][:120]))
    L.append("  callee histogram (all CJK literals):")
    for c, n in Counter((r["callee"] or "(none)") for r in all_rows).most_common(20):
        L.append("      %5d  %s" % (n, c))

    if out_path:
        with open(out_path, "w", encoding="utf-8", newline="\n") as fh:
            fh.write("\n".join(L) + "\n")
        print("wrote " + out_path)
    else:
        # Console codepages mangle CJK on Windows; prefer --out for anything readable.
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        print("\n".join(L))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
