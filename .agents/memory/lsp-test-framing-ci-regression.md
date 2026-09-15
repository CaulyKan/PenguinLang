# LSP test framing CI regression — MATCH mode for variable Content-Length (2026-09-15/16)

CI (GitHub Actions, commit 713f365) had 4 test failures, all `LspTest` on the
`Prebuilt` backend: `MagellanicConfig`, `MagellanicConfigPriority`,
`ProjectDiscovery`, `SelfHostProjectModeLibChain`.

## Root cause

The Run LSP `Stdin` and ESCAPE `ExpectedStdout` goldens embed
`file://${PENGUIN_ROOT}/...` uris with HAND-WRITTEN `Content-Length:` counts.
The runner expands `${VAR}` textually but never re-lengths the frames, so the
counts were only valid for one root path length (stale even locally:
MagellanicConfig's didOpen declared 298 vs 302 actual bytes).
On CI `PENGUIN_ROOT = /home/runner/work/PenguinLang/PenguinLang` (42 chars):
every frame whose body embeds the root is 9 bytes longer than declared → the
LSP framer desyncs → the server silently drops every request after the first
size-mismatched frame (only the root-free `shutdown` reply survived).

Tests using `file:///tmp/...` (no `${PENGUIN_ROOT}`) were unaffected.

## Fix (Tests/Program.cs + LSP goldens — no compiler change)

Two halves; the split matters:

1. **Assertion side — `MATCH` expectation mode (replaces golden rewriting).**
   First attempt rewrote the expected goldens with `LspFraming.FixContentLengths`
   in `Evaluate` — rejected as ugly (double bookkeeping: the .md numbers became
   lies). Final design: `ExpectedStdout: MATCH \`pattern\`` — operand is
   C-unescaped, ${VAR}-expanded, then matched as a MOSTLY-LITERAL regex against
   the whole stream (`\A(?:...)\z`, Singleline): every char verbatim except
   hole fragments `\d` `\s` `\w` (optional `+`/`*`) and `.*`/`.+`
   (`Expectation.ToMatchPattern` splices holes into `Regex.Escape`d literals).
   LSP goldens write `Content-Length: \d+` at count positions; JSON bodies
   stay byte-exact without metachar escaping. Malformed patterns are caught
   (`RegexParseException`), match timeout 10 s.
2. **Stdin side — runner recomputes counts (irreplaceable).** The server
   consumes real BYTES, not patterns: a misframed didOpen desyncs the framer
   and every later request is dropped regardless of how the golden matches.
   `LspFraming.FixContentLengths` runs on `Stdin` after `EnvHelper.Expand`,
   gated on `IsRunLsp`; it rewrites each `Content-Length: N` to the real UTF-8
   body length, handles CRLF CRLF + lenient LF LF, and leaves a stream with a
   never-terminated (deliberately partial) frame untouched.

`FramerSplitFrames.md` (penguin-side `lsp.Framer` unit test, Pass3) is
unaffected — its Content-Length strings live in penguin source.

## Conversion notes (gotchas hit)

- All 19 LSP goldens converted `ESCAPE` → `MATCH` with counts → `\d+`.
  Two files (`Hover.md`, `MagellanicConfigPriority.md`) have RAW BACKTICKS
  inside the operand (markdown fences in hover values) — a `[^`]*` conversion
  regex corrupts them; anchor on the FIRST and LAST backtick of the line
  instead (`^prefix ESCAPE (`)(.*)(`)$` with greedy .*).
- The runner filter needs the category: `LspTest/MagellanicConfig*` works,
  `MagellanicConfig*` matches nothing.

## Verification

- `--compilers prebuilt --filter 'LspTest/*'`: 19/19 PASS.
- Negative: corrupting one golden body char (`cfglib.penguinX`) → FAIL with
  "stream does not match MATCH pattern"; restored after.
- Algorithm mirror checked framing consistency under 33-char local AND 42-char
  CI root lengths.
- BabyPenguin fast suite 368/368 (Evaluate changed → rerun to be safe).
