#!/usr/bin/env python3
r"""i18n audit for the Linux/Avalonia tree (repo root: src/TipCL.App + src/TipCL.Core).

Why this exists
---------------
The repo ships `--i18n-check` (windos/src/TipCL/I18nCheck.cs), but it is Windows-only
and its source-tree candidates are hard-coded to `<TIPCL_HOME>/src/TipCL` or
`<TIPCL_HOME>/wpf/TipCL` (I18nCheck.cs:26-28). It therefore cannot measure the Linux
port at all, and with an isolated TIPCL_HOME it exits 2 with `没有源码树: ...\wpf\TipCL`.
This script applies the same criterion to the Linux tree instead of trusting a
paraphrased list (docs/DECISIONS.md D-33).

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
     are unwired. Measured on this tree (2026-11-15, R30-WP6b = after the R-WP5-01
     lexer fix below; the pre-fix numbers 4607/4485/97.35% were under-counted by 152):
       cjk_literals 4759 | wrapped 122 (2.56%) | unwrapped 4637 (97.44%)
     i.e. 97.44% of the CJK literals do not consult the word table at all, so the
     interface keeps showing Chinese even with language=en.

Lexer fix (R-WP5-01, 2026-11-15)
--------------------------------
`Lexer._string()` used to miss the `}` of an interpolation hole when the hole itself
contained a nested string: the nested-string skip returned the index of the closing
quote and the loop then `continue`d *without* advancing, so the hole scan hunted for
the *next* quote instead of resuming after this one, never found `}` at depth 0 and ran
to EOF. The literal then swallowed the rest of the file and every later literal in that
file became invisible. Trigger (src/TipCL.App/Pages/MultiplayerPage.cs:222):

    _nodes.Text = nodes.Count == 0 ? "" : $"会合节点 {nodes.Count} 个:{string.Join("  ", nodes.Take(3))}";

=> 6 files had a swallowing literal; 5 of them lost 152 CJK literals in total
   (MultiplayerPage 76, SmokeRunner 55, ModsDialog 8, PluginsPage.Settings 7,
   VersionManagePage 6) and the 6th (Ui/UiHelpers.cs) kept its Chinese in comments
   only, so it never showed up in the counts. Detection is structural now, not
   count-based: `python tools/i18n-audit.py --selfcheck` asserts that no literal runs to
   EOF, that every interpolation hole is closed, and that no `"` is left unscanned
   behind the last literal of a file.

Reported numbers
----------------
  cjk_literals        every CJK string literal occurrence in the scanned tree
  wrapped             CJK literals that go through a wrapper (wired, 2.56%)
  unwrapped           CJK literals that do not (the localisation backlog, 97.44%)
  coverage            wrapped / cjk_literals  <-- the SHARE THAT IS WIRED
  wrapped_keys        distinct wrapper argument values (the D-33 "key" universe)
  missing_en/_zh      wrapped_keys absent from that table  <-- the D-33 actionable 差集

Usage:
  python tools/i18n-audit.py                 # human summary
  python tools/i18n-audit.py --help          # criterion + denominator/口径
  python tools/i18n-audit.py --json          # machine-readable
  python tools/i18n-audit.py --list-unwrapped --top-files 25
  python tools/i18n-audit.py --baseline progress/I18N-BASELINE.md   # 基线快照(带 I18N-GUARD 机读块)
"""

import json
import os
import re
import sys
import datetime
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
                 Also wrapped: literals on lines marked `i18n:runtime-tr` (or inside an
                 `i18n:runtime-tr-begin` .. `-end` block) = table keys that are passed
                 through I18nCore.Tr when read (only in RUNTIME_TR_FILES, test-guarded).

Denominator / direction - READ THIS BEFORE QUOTING A NUMBER
  cjk_literals = DENOMINATOR: every CJK literal occurrence in src/TipCL.App + src/TipCL.Core.
  wrapped      = literals that DO consult the word table  -> language switching can affect them.
  unwrapped    = literals that do NOT                      -> language switching cannot affect them.
  coverage     = wrapped / cjk_literals = the share that IS wired.

  coverage is NOT "how much of the UI is translated", and it must not be read backwards.
  Last measured 2026-11-15 (R30-WP6b, after the R-WP5-01 lexer fix; the pre-fix numbers
  4607/4485/97.35% under-counted 152 literals in 5 files):
      cjk_literals 4759 | wrapped 122 (2.56%) | unwrapped 4637 (97.44%)
  => 97.44% of the CJK literals never consult the word table, which is why the UI stays
     Chinese even with language=en. See progress/R30-WP3b.md 8 for the reconciliation
     and progress/R30-WP6b.md for the 152-literal lexer defect.

Options
  --json             machine-readable output
  --out PATH         write the report to PATH instead of stdout
  --list-unwrapped   also list every unwrapped literal with file:line
  --top-files N      how many files to list in the "unwrapped by file" table (default 20)
  --baseline PATH    write a Markdown baseline snapshot to PATH: per-file unwired list + a
                     machine-readable I18N-GUARD block (unwrapped_total / per_file).
                     Guard test tests/TipCL.Tests/Repo/I18nBaselineTests.cs reads that block
                     and fails if the unwired count grows.
  --selfcheck        assert the lexer invariants over every scanned file (exit 1 on
                     failure). Run this after touching Lexer: a scanner that silently
                     stops half-way through a file is exactly what R-WP5-01 was.
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
    """String-literal scanner following I18nCheck.ScanCode / ScanString.

    Structural invariants (asserted by `run_selfcheck`, i.e. `--selfcheck`):
      1. every literal ends *on* a closing quote: the returned offset is > start and
         `s[ret - 1] == '"'`. A literal must never run to EOF;
      2. every `{}` interpolation hole is closed by a `}` found at depth 0;
      3. no line break inside a non-verbatim, non-raw literal (a plain `"..."` cannot
         span lines in C#, so a span that does means the scanner has lost sync with the
         quotes - the desync that follows shows up as wrong values, not as missing
         counts);
      4. no `"` is left unscanned behind the last literal of a file (quotes inside
         comments and char literals do not count).
    These are invariants, not counters, on purpose: R-WP5-01 was silent (the totals just
    came out 152 too low), and the buggy literal satisfied "the scan reached EOF", so a
    count- or EOF-based check would have stayed green. `self.anomalies` is empty for a
    healthy tree.
    """

    def __init__(self, text: str):
        self.s = text
        self.n = len(text)
        self.lits = []       # (start_offset, value)
        self.spans = []      # (start_offset, offset_after_literal, terminated, may_span_lines)
        self.anomalies = []  # (offset, kind, detail)
        self.comments = []   # (start_offset, offset_after_comment) - for the tail check
        self.chars = []      # (start_offset, offset_after_char_literal)

    def run(self):
        self._code(0, self.n)
        return self.lits

    def _literal_start(self, i, end):
        """True if a string literal starts at i (the prefixes C# allows)."""
        s = self.s
        if i >= end:
            return False
        c = s[i]
        if c == '"':
            return True
        if c == "$":
            return (i + 1 < end and s[i + 1] == '"') or \
                   (i + 2 < end and s[i + 1] == "@" and s[i + 2] == '"')
        if c == "@":
            return (i + 1 < end and s[i + 1] == '"') or \
                   (i + 2 < end and s[i + 1] == "$" and s[i + 2] == '"')
        return False

    def _code(self, i, end):
        s = self.s
        while i < end:
            c = s[i]
            if c == "/" and i + 1 < end and s[i + 1] == "/":
                start = i
                while i < end and s[i] != "\n":
                    i += 1
                self.comments.append((start, i))
                continue
            if c == "/" and i + 1 < end and s[i + 1] == "*":
                start = i
                j = s.find("*/", i + 2)
                i = end if j < 0 else j + 2
                self.comments.append((start, i))
                continue
            if c == "'":
                start = i
                i += 1
                while i < end and s[i] != "'":
                    if s[i] == "\\":
                        i += 1
                    i += 1
                i += 1
                self.chars.append((start, min(end, i)))
                continue
            if self._literal_start(i, end):
                i = self._string(i, end)
                continue
            i += 1

    def _string(self, start, end):
        """Record one literal (scanned by `_literal`) and return the offset after it."""
        ret, value, terminated, may_span_lines = self._literal(start, end)
        self.lits.append((start, value))
        self.spans.append((start, ret, terminated, may_span_lines))
        if not terminated:
            self.anomalies.append((start, "unterminated-literal",
                                   "string literal opened at %d (line offset %d) is never closed"
                                   % (start, start)))
        return ret

    def _literal(self, start, end):
        """Scan one string literal without recording it.

        Returns (offset_after_literal, value, terminated, may_span_lines).
        """
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
            terminated = close >= 0
            if not terminated:
                close = end
            value = s[j:close]
            if interpolated:
                value = value.replace("{{", "{").replace("}}", "}")
            ret = min(end, close + len(fence))
            if not terminated:
                self.anomalies.append((start, "unterminated-raw-string",
                                       "raw string fence %r has no closing fence" % fence))
            return ret, value.strip("\r\n "), terminated, True
        i += 1
        out = []
        terminated = False
        while i < end:
            c = s[i]
            if c == '"':
                if not verbatim or i + 1 >= end or s[i + 1] != '"':
                    i += 1
                    terminated = True
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
                hole = i
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
                    elif self._literal_start(j, end):
                        # R-WP5-01: resume *after* the nested literal. Keeping j on its
                        # closing quote (the old `continue`) made the hole scan jump from
                        # quote to quote, skip the hole's `}` and run to EOF, so the
                        # literal swallowed the rest of the file.
                        #
                        # Scanning the nested literal with `_literal` (not with the old
                        # quote-to-quote shortcut) also fixes the *nested interpolated*
                        # case, e.g. SmokeRunner.cs:459
                        #   $"...({(acc.Total == 0 ? "无数据" : $"{acc.Ok}/{acc.Total}")})"
                        # whose inner `{acc.Ok}` used to be mistaken for the hole's `}`,
                        # leaving the scanner one quote out of sync for the rest of the
                        # file (wrong values, same totals - which is why the desync check
                        # below matters).
                        nret, _v, _term, _sl = self._literal(j, end)
                        j = min(end, nret)
                        continue
                    j += 1
                out.append("{}")
                i = min(end, j + 1)
                if j >= end or s[j] != "}":
                    self.anomalies.append(
                        (hole, "unclosed-interpolation-hole",
                         "interpolation hole opened at %d has no closing brace "
                         "(hole scan stopped at %d, file length %d)" % (hole, j, self.n)))
                continue
            if interpolated and c == "}" and i + 1 < end and s[i + 1] == "}":
                out.append("}")
                i += 2
                continue
            out.append(c)
            i += 1
        return i, "".join(out), terminated, (verbatim or False)


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


# AI3-i18n: "wired at runtime" marker.
#
# Some CJK literals are word-table KEYS that are looked up later rather than at the
# literal itself, e.g. the old tool-label dictionaries in Ai/Tools.cs:
#     private static readonly Dictionary<string, string> Labels = new() { { "ask_user", "请你选一下" }, ... };
#     ... return Labels.TryGetValue(key, out string? label) ? I18nCore.Tr(label) : key;
# Switching the language DOES change what the user sees, but the literal is not an argument
# of Tr(...), so the plain criterion above counts it as unwired (a false positive).
#
# Such literals can be marked, and then count as wrapped (callee "(runtime-tr)"):
#   * a line containing `i18n:runtime-tr`                 -> that line;
#   * `i18n:runtime-tr-begin` ... `i18n:runtime-tr-end`   -> every line in between (inclusive).
# The marker is a claim that must be backed by a test: every file that uses it is listed in
# RUNTIME_TR_FILES, and tests/TipCL.Tests/Repo/I18nRuntimeTrMarkerTests.cs fails when a new
# file starts using it (or a listed one stops), so the list cannot silently grow. Each listed
# file has a behaviour test proving the values really come out English under language=en
# (Tools.cs: AiI18nUxLabelTests; AiUiText.cs: AiI18nResidueCoreTests). Marked literals also
# join `wrapped_keys`, so a missing en translation shows up in `missing_en` like any Tr key.
RUNTIME_TR_MARK = "i18n:runtime-tr"
RUNTIME_TR_CALLEE = "(runtime-tr)"
RUNTIME_TR_FILES = (
    "src/TipCL.Core/Launcher/Ai/Tools.cs",
    "src/TipCL.Core/Launcher/Ai/AiUiText.cs",
)


def runtime_tr_lines(lines):
    """1-based line numbers covered by an i18n:runtime-tr marker (line or begin/end block)."""
    covered = set()
    inside = False
    for idx, line in enumerate(lines, 1):
        if RUNTIME_TR_MARK + "-begin" in line:
            inside = True
        if inside or RUNTIME_TR_MARK in line:
            covered.add(idx)
        if RUNTIME_TR_MARK + "-end" in line:
            inside = False
    return covered


def line_of(text: str, offset: int) -> int:
    return text.count("\n", 0, offset) + 1


def scan_file(path, root):
    with open(path, encoding="utf-8", errors="replace") as fh:
        text = fh.read()
    rel = os.path.relpath(path, root).replace("\\", "/")
    if is_file_ignored(text):
        return rel, [], True
    lines = text.split("\n")
    runtime = runtime_tr_lines(lines)
    rows = []
    for start, value in Lexer(text).run():
        if not has_cjk(value):
            continue
        ln = line_of(text, start)
        ignored = "i18n:ignore" in lines[ln - 1] if 0 < ln <= len(lines) else False
        callee = callee_before(text, start)
        wrapped = callee is not None and callee.split(".")[-1] in WRAPPERS
        if not wrapped and ln in runtime:
            # AI3-i18n: 这条字面量本身不是 Tr(...) 的实参,但它所在的表在**运行时**取出后一定过
            # I18nCore.Tr(见 RUNTIME_TR_MARK 的说明)—— 切语言能改变它,按已接线计。
            wrapped = True
            callee = RUNTIME_TR_CALLEE
        rows.append({
            "file": rel, "line": ln, "text": value,
            "callee": callee, "wrapped": wrapped, "ignored": ignored,
        })
    return rel, rows, False


def selfcheck_tree(root, src_dirs, show=10):
    """Assert the Lexer invariants over the whole scanned tree.

    Returns (lines, failures). See Lexer's docstring for what is asserted. The point is
    to fail loudly when a scanner change (or a new C# construct) makes a file stop being
    lexed half-way: that is invisible in the totals, which is how R-WP5-01 survived.
    """
    out = []
    checked = literals = 0
    failures = []
    for d in src_dirs:
        if not os.path.isdir(d):
            continue
        for f in iter_cs_files(d):
            with open(f, encoding="utf-8", errors="replace") as fh:
                text = fh.read()
            rel = os.path.relpath(f, root).replace("\\", "/")
            if is_file_ignored(text):
                continue
            checked += 1
            lex = Lexer(text)
            lex.run()
            literals += len(lex.lits)
            probs = list(lex.anomalies)
            for start, ret, terminated, may_span_lines in lex.spans:
                if not terminated:
                    probs.append((start, "literal-not-closed",
                                  "literal at %d (line %d) is not closed by a quote"
                                  % (start, line_of(text, start))))
                elif ret <= start or ret > len(text) or text[ret - 1] != '"':
                    probs.append((start, "literal-does-not-end-on-a-quote",
                                  "literal at %d (line %d) returned end offset %d, but "
                                  "text[%d] is %r - the scanner did not stop on a closing quote"
                                  % (start, line_of(text, start), ret, ret - 1,
                                     text[ret - 1] if 0 < ret <= len(text) else None)))
                elif not may_span_lines and "\n" in text[start:ret]:
                    probs.append((start, "literal-crosses-a-line-break",
                                  "a plain (non-verbatim, non-raw) literal at %d (line %d) "
                                  "spans %d line breaks, which C# does not allow; the scanner "
                                  "is out of sync with the quotes from here on (span %d..%d)"
                                  % (start, line_of(text, start),
                                     text[start:ret].count("\n"), start, ret)))
            # Every '"' in code position must have been looked at: nothing may hide behind
            # the end of the last literal. Quotes inside comments / char literals / the
            # literals themselves do not count as hitting this invariant.
            masked = sorted([(sp[0], sp[1]) for sp in lex.spans] + lex.comments + lex.chars)
            last_q = text.rfind('"')
            while last_q >= 0 and any(a <= last_q < b for a, b in masked):
                last_q = text.rfind('"', 0, last_q)
            reached = max([sp[1] for sp in lex.spans], default=0)
            if last_q >= 0 and reached < last_q + 1:
                probs.append((last_q, "tail-not-reached",
                              "the file's last quote in code position is at %d (line %d) but "
                              "lexing stopped at %d: %d bytes never scanned"
                              % (last_q, line_of(text, last_q), reached, len(text) - reached)))
            for off, kind, detail in probs:
                if len(failures) < 200:
                    failures.append("%s:%d  [%s] %s"
                                    % (rel, line_of(text, off), kind, detail))
    out.append("== i18n-audit --selfcheck (Lexer invariants) ==")
    out.append("  files checked   : %d" % checked)
    out.append("  literals lexed  : %d" % literals)
    out.append("  scope           : %s"
               % ", ".join(os.path.relpath(d, root).replace("\\", "/") for d in src_dirs))
    out.append("  invariants      : (1) every literal ends on a closing quote; (2) every {..}"
               " interpolation hole is closed at depth 0; (3) no line break inside a plain"
               " literal (scanner desync); (4) no quote in code position is left behind the"
               " last literal")
    if failures:
        out.append("  FAIL: %d problem(s) in the tree:" % len(failures))
        for f in failures[:show]:
            out.append("      " + f)
        if len(failures) > show:
            out.append("      ... and %d more" % (len(failures) - show))
        out.append("  => a file is not being lexed correctly; the counts above cannot be"
                   " trusted. See R-WP5-01 / R-WP5-06 (Lexer string scanning).")
        return out, failures
    out.append("  PASS: all %d files lexed to their end, no literal swallowed a file tail,"
               " no desync." % checked)
    return out, []

def write_baseline(path, root, result, unwrapped, tables, detail_files=25, detail_per_file=40):
    """把「未接线的用户可见 CJK 字面量」落成一份基线快照(progress/I18N-BASELINE.md)。

    为什么要这份文件:未接线数量只靠"这次比上次少"是管不住的 —— 没人记得上次是多少。
    这里同时产出**人看的按文件清单**和**机读的 I18N-GUARD 块**;守卫测试
    (tests/TipCL.Tests/Repo/I18nBaselineTests.cs) 读机读块,断言未接线总数不增加。

    口径与 --json 一致:一条"未接线字面量"= 一次出现(不是去重后的值);文件路径相对仓库根。
    """
    en = tables["en"]
    zh = tables["zh_CN"]
    per_file = Counter(r["file"] for r in unwrapped)
    per_file_with_en = Counter(r["file"] for r in unwrapped if r["text"] in en)
    have_en_total = sum(1 for r in unwrapped if r["text"] in en)

    L = []
    L.append("# i18n 接线基线(未接线的用户可见 CJK 字面量)")
    L.append("")
    L.append("> 自动生成,**不要手改**:`python tools/i18n-audit.py --baseline progress/I18N-BASELINE.md`")
    L.append("> 生成时间:%s" % datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"))
    L.append(">")
    L.append("> 判据(与 `tools/i18n-audit.py --help` 顶部同一份口径):")
    L.append(">   * **已接线(wrapped)** = 字面量是 `I18nCore.Tr(...)` 的实参 —— 切语言**能**改变它;")
    L.append(">   * **未接线(unwrapped)** = 其余全部 —— 切语言**改不了**它(用户看到的\"英文界面里的中文\")。")
    L.append(">   * 覆盖率 = 已接线 / 全部 CJK 字面量。**它不是\"界面翻译了多少\"**,别倒着读。")
    L.append("")
    L.append("## 总览")
    L.append("")
    L.append("| 指标 | 数值 |")
    L.append("| --- | --- |")
    L.append("| CJK 字面量(分母) | %d |" % result["cjk_literals"])
    L.append("| 已接线(wrapped) | %d(%.2f%%) |" % (result["wrapped"], 100.0 * result["coverage"]))
    L.append("| 未接线(unwrapped) | %d |" % result["unwrapped"])
    L.append("| 其中**词表里已有 en 译文**(只差接线) | %d |" % have_en_total)
    L.append("| 涉及的 .cs 文件 | %d |" % len(per_file))
    L.append("| 词表条目(en / zh_CN) | %d / %d |" % (len(en), len(zh)))
    L.append("")
    L.append("## 按文件聚合(未接线条数降序)")
    L.append("")
    L.append("| 未接线 | 其中已有 en 译文 | 文件 |")
    L.append("| --- | --- | --- |")
    for f, n in per_file.most_common():
        L.append("| %d | %d | `%s` |" % (n, per_file_with_en.get(f, 0), f))
    L.append("")
    L.append("## 明细(未接线条数最多的 %d 个文件,每个最多 %d 条)" % (detail_files, detail_per_file))
    L.append("")
    L.append("`[已有译文]` = 这条文案在 `locales/en.json` 里已经是一个 key,接线(套 `Tr(...)`)就能立刻生效;")
    L.append("没有这个标记的,要先补词表。")
    L.append("")
    by_file = {}
    for r in unwrapped:
        by_file.setdefault(r["file"], []).append(r)
    for f, _ in per_file.most_common(detail_files):
        rows = sorted(by_file.get(f, []), key=lambda r: r["line"])
        L.append("### `%s`(%d 条)" % (f, len(rows)))
        L.append("")
        for r in rows[:detail_per_file]:
            mark = " `[已有译文]`" if r["text"] in en else ""
            L.append("- `%s:%d` %s%s" % (f, r["line"], r["text"].replace("|", "\\|")[:160], mark))
        if len(rows) > detail_per_file:
            L.append("- …… 另有 %d 条(见 `--list-unwrapped` 全量输出)" % (len(rows) - detail_per_file))
        L.append("")
    L.append("## 机读守卫块(守卫测试读这里,勿改格式)")
    L.append("")
    L.append("<!-- I18N-GUARD-BEGIN -->")
    guard = {
        "generated": datetime.datetime.now().strftime("%Y-%m-%dT%H:%M:%S"),
        "cjk_literals": result["cjk_literals"],
        "wrapped": result["wrapped"],
        "unwrapped_total": result["unwrapped"],
        "unwrapped_with_en_key": have_en_total,
        "files": len(per_file),
        "per_file": dict(per_file.most_common()),
    }
    L.append("```json")
    L.append(json.dumps(guard, ensure_ascii=False, sort_keys=True))
    L.append("```")
    L.append("<!-- I18N-GUARD-END -->")
    L.append("")

    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(L))
    print("wrote " + path)
    print("  unwrapped_total=%d (含已有译文 %d)  wrapped=%d/%d"
          % (result["unwrapped"], have_en_total, result["wrapped"], result["cjk_literals"]))


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
    baseline_path = None
    if "--baseline" in argv:
        baseline_path = argv[argv.index("--baseline") + 1]
    out_path = None
    if "--out" in argv:
        out_path = argv[argv.index("--out") + 1]
    top_files = 20
    if "--top-files" in argv:
        top_files = int(argv[argv.index("--top-files") + 1])

    if "--selfcheck" in argv:
        lines, failures = selfcheck_tree(root, src_dirs)
        text = "\n".join(lines) + "\n"
        if out_path:
            with open(out_path, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(text)
            print("wrote " + out_path)
        else:
            sys.stdout.reconfigure(encoding="utf-8", errors="replace")
            print(text, end="")
        return 1 if failures else 0

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
        "runtime_tr_wrapped": sum(1 for r in wrapped if r["callee"] == RUNTIME_TR_CALLEE),
        "runtime_tr_files": sorted({r["file"] for r in wrapped if r["callee"] == RUNTIME_TR_CALLEE}),
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

    if baseline_path:
        write_baseline(baseline_path, root, result, unwrapped, tables)
        return 0

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
    L.append("    of which  : %d marked i18n:runtime-tr (looked up via Tr at runtime) in %s"
             % (result["runtime_tr_wrapped"], ", ".join(result["runtime_tr_files"]) or "-"))
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
