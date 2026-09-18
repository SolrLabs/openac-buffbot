#!/usr/bin/env python3
"""Check C# comments against this repository's limits.

A comment explains the code, not the circumstances that produced it. Planning notes, dated
narratives and issue references belong in a commit message or an issue, not in a public
repository's source.

    scripts/check-comments.py              check src/ and tests/
    scripts/check-comments.py --report     print the numbers and exit 0
    scripts/check-comments.py path ...     check only these paths

Limits live in scripts/comment-limits.json so they can ratchet downwards as the code is tidied;
they may never be raised without saying why in the commit message.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
LIMITS_PATH = ROOT / "scripts" / "comment-limits.json"
DEFAULT_PATHS = ("src", "tests")
SKIP_DIRS = {"bin", "obj", ".git", "artifacts"}

# Each pattern is (name, regex, why). Keep them concrete: a checker that guesses at tone
# annoys more than it helps.
BANNED = [
    ("dated narrative", re.compile(r"\b\d{4}-\d{2}-\d{2}\b"),
     "a date in a comment records when something happened, not what the code does"),
    ("decision or trap id", re.compile(r"\b[BTWO]-\d{1,3}\b"),
     "internal record ids mean nothing to a reader of this repository"),
    ("planning vocabulary", re.compile(
        # "plan" alone is domain vocabulary here: a cast plan is a real thing.
        r"\b(brief|playtest|docket|research[- ]pass|handoff|unit \d|per shane|shane|erik)\b",
        re.IGNORECASE),
     "planning and review history belongs in the commit message or an issue"),
    ("live-run narrative", re.compile(
        r"\b(live run|watched run|in a live|we tried|turned out|used to be|before this existed)\b",
        re.IGNORECASE),
     "a comment states the current fact, not the history of getting there"),
]


STOPWORDS = {
    "a", "an", "and", "are", "as", "at", "back", "be", "by", "for", "from", "has", "have",
    "in", "is", "it", "its", "of", "on", "one", "or", "own", "since", "that", "the", "their",
    "then", "this", "to", "until", "up", "was", "when", "which", "while", "with",
}
DECLARATION = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)?(?:public|internal|private|protected|static|readonly|sealed|"
    r"abstract|virtual|override|partial|async|const|record|class|struct|enum|interface|"
    r"bool|int|uint|long|double|float|string|void|var)\b.*?"
    r"(?P<name>[A-Z][A-Za-z0-9]+)\s*(?:[({=;<]|=>)")
ENUM_MEMBER = re.compile(r"^\s*(?P<name>[A-Z][A-Za-z0-9]+)\s*(?:=\s*[^,]+)?,?\s*$")


def words_of(identifier: str) -> set[str]:
    """Split PascalCase into lowercase words: TellsAnswered -> {tells, answered}."""
    return {w.lower() for w in re.findall(r"[A-Z]?[a-z]+|[A-Z]+(?![a-z])", identifier)}


def restates_the_name(comment_lines: list[str], declaration: str) -> bool:
    """True when a summary says nothing the declaration's own name does not."""
    match = DECLARATION.match(declaration) or ENUM_MEMBER.match(declaration)
    if not match:
        return False

    if any("----" in line for line in comment_lines):
        return False  # a section banner is a divider, not a description

    prose = " ".join(comment_lines)
    prose = re.sub(r"<[^>]+>", " ", prose)          # strip XML tags and cref targets
    prose = re.sub(r"[^A-Za-z ]", " ", prose)
    spoken = {w.lower() for w in prose.split() if w.lower() not in STOPWORDS and len(w) > 2}
    spoken -= {"summary", "param", "returns", "true", "false", "null"}
    if not spoken or len(spoken) > 8:
        return False

    named = words_of(match.group("name"))
    # Also count words from the parameter list, so a record whose fields are listed back counts.
    named |= {w for ident in re.findall(r"[A-Z][A-Za-z0-9]+", declaration) for w in words_of(ident)}
    echoed = len(spoken & named)
    return echoed >= 2 and echoed / len(spoken) >= 0.6


def load_limits() -> dict:
    if LIMITS_PATH.exists():
        return json.loads(LIMITS_PATH.read_text())
    return {"maxCommentShare": 0.10, "maxBlockLines": 4, "maxFileCommentShare": 0.30}


def iter_files(paths: list[str]):
    for raw in paths:
        p = (ROOT / raw) if not pathlib.Path(raw).is_absolute() else pathlib.Path(raw)
        if p.is_file() and p.suffix == ".cs":
            yield p
            continue
        for f in p.rglob("*.cs"):
            if not SKIP_DIRS.intersection(f.parts):
                yield f


def scan(path: pathlib.Path):
    """Return (non_blank, comment_lines, blocks, findings) for one file."""
    non_blank = comments = 0
    blocks: list[tuple[int, int]] = []  # (start line, length)
    findings: list[tuple[int, str, str, str]] = []
    run_start = 0
    run_len = 0
    for number, raw in enumerate(path.read_text(errors="replace").splitlines(), start=1):
        line = raw.strip()
        if not line:
            # A blank line ends a block: two paragraphs are two comments, not one long one.
            if run_len:
                blocks.append((run_start, run_len))
                run_len = 0
            continue
        non_blank += 1
        if line.startswith("//"):
            comments += 1
            if run_len == 0:
                run_start = number
            run_len += 1
            body = line.lstrip("/").strip()
            for name, pattern, why in BANNED:
                if pattern.search(body):
                    findings.append((number, name, why, line))
        else:
            # A comment trailing code counts for the banned patterns, not for the share.
            trailing = re.search(r"//(?!\w).*$", line)
            if trailing:
                body = trailing.group(0).lstrip("/").strip()
                for name, pattern, why in BANNED:
                    if pattern.search(body):
                        findings.append((number, name, why, line.strip()))
            if run_len:
                blocks.append((run_start, run_len))
                run_len = 0
    if run_len:
        blocks.append((run_start, run_len))

    lines = path.read_text(errors="replace").splitlines()
    for start, length in blocks:
        after = start + length - 1
        while after < len(lines) and not lines[after].strip():
            after += 1
        if after >= len(lines):
            continue
        block = [lines[i].strip() for i in range(start - 1, start - 1 + length)]
        if restates_the_name(block, lines[after]):
            findings.append((
                start, "restates the name",
                "the declaration already says this; delete the comment",
                lines[start - 1].strip()))

    return non_blank, comments, blocks, findings


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", nargs="*", default=list(DEFAULT_PATHS))
    parser.add_argument("--report", action="store_true", help="print the numbers, never fail")
    args = parser.parse_args()

    limits = load_limits()
    total_lines = total_comments = 0
    all_blocks: list[int] = []
    failures: list[str] = []

    for path in sorted(iter_files(args.paths or list(DEFAULT_PATHS))):
        rel = path.relative_to(ROOT)
        non_blank, comments, blocks, findings = scan(path)
        total_lines += non_blank
        total_comments += comments
        all_blocks.extend(length for _, length in blocks)

        for number, name, why, text in findings:
            failures.append(f"{rel}:{number}: {name} — {why}\n    {text}")
        for start, length in blocks:
            if length > limits["maxBlockLines"]:
                failures.append(
                    f"{rel}:{start}: comment block of {length} lines "
                    f"(limit {limits['maxBlockLines']})")
        if non_blank and comments / non_blank > limits["maxFileCommentShare"]:
            failures.append(
                f"{rel}: {100 * comments / non_blank:.1f}% of lines are comments "
                f"(file limit {100 * limits['maxFileCommentShare']:.0f}%)")

    share = total_comments / total_lines if total_lines else 0
    median = sorted(all_blocks)[len(all_blocks) // 2] if all_blocks else 0
    print(f"{total_comments} comment lines in {total_lines} non-blank ({100 * share:.1f}%), "
          f"{len(all_blocks)} blocks, median {median} line(s)")

    if args.report:
        return 0

    if share > limits["maxCommentShare"]:
        failures.append(
            f"overall comment share {100 * share:.1f}% exceeds "
            f"{100 * limits['maxCommentShare']:.0f}%")

    if failures:
        print(f"\n{len(failures)} problem(s):\n", file=sys.stderr)
        for failure in failures:
            print(failure, file=sys.stderr)
        print("\nA comment explains the code, not the circumstances that produced it.",
              file=sys.stderr)
        return 1

    print("comments are within limits")
    return 0


if __name__ == "__main__":
    sys.exit(main())
