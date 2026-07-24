#!/usr/bin/env python3
"""
Memory derived-fact checker.

Scans memory/*.md files for claims that can be re-derived from source code
(file counts, line counts, test counts, directory listings, coverage stats)
and flags them as potential maintenance burden.

A "derived fact" is any claim that can be reproduced in under 30 seconds
with a deterministic shell command. These facts silently rot when the code
changes — unlike code, stale derived facts produce no compiler error.

Usage:
    # Default: list all derived facts grouped by category
    python3 scripts/check-derived-facts.py

    # Brief: one-line summary
    python3 scripts/check-derived-facts.py --brief

    # Machine-readable JSON
    python3 scripts/check-derived-facts.py --json

    # CI gate: exit non-zero if more than N derived facts found
    python3 scripts/check-derived-facts.py --fail-under 20

    # Only high-confidence findings
    python3 scripts/check-derived-facts.py --confidence high

    # Verify file-count claims against the actual repo
    python3 scripts/check-derived-facts.py --verify
"""

import argparse
import json
import os
import re
import subprocess
import sys
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional

# ── Patterns ──────────────────────────────────────────────────────────

# High confidence: file counts — "51 文件", "8 文件"
FILE_COUNT_RE = re.compile(r'(\d+)\s*文件')

# High confidence: line counts — "8,898 行", "~5,600 行", "1,222 行"
# Matches optional tilde, digits with optional comma-separated thousands
LINE_COUNT_RE = re.compile(r'(~?\d[\d,]*)\s*行')

# High confidence: test counts — "649 测试", "175 测试"
TEST_COUNT_RE = re.compile(r'(\d+)\s*测试')

# High confidence: coverage percentage — "95.7% 行覆盖率"
COVERAGE_PCT_RE = re.compile(r'(\d+\.\d+)%\s*(?:行覆盖率|lines)')

# High confidence: coverage fraction — "(10677/11161)" or "(10677/11161 lines)"
COVERAGE_FRAC_RE = re.compile(r'\((\d+)/(\d+)\)')

# High confidence: tree lines with file counts — box-drawing + "文件"
TREE_LINE_WITH_COUNT_RE = re.compile(r'[├└│]\s*.*?(\d+)\s*文件')
BOX_DRAWING_RE = re.compile(r'[├└│]')

# Medium confidence: tree blocks — 3+ consecutive box-drawing lines
# that contain path-like segments (ending with /) and no design-diagram markers

# Medium confidence: class name lists — 3+ comma-separated PascalCase names
CLASS_LIST_RE = re.compile(r'([A-Z][A-Za-z0-9]{2,}(?:\s*,\s*[A-Z][A-Za-z0-9]{2,}){2,})')

# Narrative prose exclusion: "覆盖率从 X 降至 Y" — comparative, not a stat
NARRATIVE_COVERAGE_RE = re.compile(r'从\s*\d+.*?(?:降至|到|至)\s*\d+')

# Design diagram markers (lines containing these are NOT directory trees)
DESIGN_DIAGRAM_KEYWORDS = {'bits', 'CompType', 'bits', 'Generation',
                           'Version', 'Magic', 'Flags', 'Reserved'}

# Path-like directory names in the project (for tree block detection)
KNOWN_DIR_NAMES = {
    'Core', 'Compiler', 'Blob', 'Cache', 'Format', 'Lexer', 'Literal',
    'WAL', 'SourceGenerator', 'Runtime', 'Editor', 'Tests', 'Extensions',
    'packages', 'fluxformula', 'fluxformula.core', 'fluxformula.burst',
    'fluxformula.addressables', 'fluxformula.addressables.unitask',
    'docs', 'examples', 'scripts',
}

# Arrows in text (indicate flow diagrams, not directory trees)
ARROW_CHARS = {'→', '←', '↑', '↓', '↔'}


@dataclass
class Finding:
    """A single derived fact found in a memory file."""
    file_name: str
    line_number: int
    category: str         # file_count, line_count, test_count, coverage_stat,
                          # tree_file_block, directory_tree, class_list
    confidence: str       # 'high' or 'medium'
    matched_text: str
    suggestion: str

    @property
    def location(self) -> str:
        return f'{self.file_name}:{self.line_number}'


# ── Memory file discovery ─────────────────────────────────────────────

def _normalize_path_for_match(s: str) -> str:
    """Normalize a path segment for fuzzy matching (dashes/underscores/spaces)."""
    return s.lower().replace('-', '_').replace(' ', '_')


def _try_projects_dir(projects_dir: str, prefer_suffix: str = '') -> Optional[str]:
    """Search a .claude/projects/ directory for a matching memory/ subdirectory."""
    if not os.path.isdir(projects_dir):
        return None

    matches = []
    for entry in os.listdir(projects_dir):
        mem_dir = os.path.join(projects_dir, entry, 'memory')
        if os.path.isdir(mem_dir):
            matches.append((entry, mem_dir))

    if not matches:
        return None

    # Prefer the entry whose name fuzzy-matches the prefer_suffix
    if prefer_suffix:
        norm_prefer = _normalize_path_for_match(prefer_suffix)
        for entry, mem_dir in matches:
            if _normalize_path_for_match(entry) == norm_prefer:
                return mem_dir
        # Lenient: check if prefer_suffix is a substring of entry or vice versa
        for entry, mem_dir in matches:
            norm_entry = _normalize_path_for_match(entry)
            if norm_prefer in norm_entry or norm_entry in norm_prefer:
                return mem_dir

    # Fallback: return the first match
    return matches[0][1]


def find_memory_dir() -> Optional[str]:
    """Find the memory directory via CLAUDE_PROJECT_DIR or common paths."""
    # 1. CLAUDE_PROJECT_DIR env var
    env_dir = os.environ.get('CLAUDE_PROJECT_DIR')
    if env_dir:
        candidate = os.path.join(env_dir, 'memory')
        if os.path.isdir(candidate):
            return candidate

    cwd = os.getcwd()

    # 2. Walk up from cwd to find .claude/projects/
    search = cwd
    for _ in range(5):
        claude_dir = os.path.join(search, '.claude')
        if os.path.isdir(claude_dir):
            result = _try_projects_dir(os.path.join(claude_dir, 'projects'))
            if result:
                return result
        parent = os.path.dirname(search)
        if parent == search:
            break
        search = parent

    # 3. Check ~/.claude/projects/ (user home, common on all platforms)
    home = os.environ.get('HOME') or os.environ.get('USERPROFILE')
    if home:
        result = _try_projects_dir(
            os.path.join(home, '.claude', 'projects'),
            prefer_suffix=os.path.basename(cwd),
        )
        if result:
            return result

    return None


def find_memory_files(memory_dir: str) -> list[Path]:
    """Recursively find all .md files in the memory directory, sorted."""
    md_files = sorted(Path(memory_dir).rglob('*.md'))
    return [f for f in md_files if f.name != 'MEMORY.md' or True]
    # Include MEMORY.md itself — it may contain derived facts too


# ── Frontmatter parsing ───────────────────────────────────────────────

def parse_frontmatter(lines: list[str]) -> tuple[int, list[tuple[int, str]]]:
    """
    Parse YAML frontmatter boundaries.

    Returns (frontmatter_end_index, [(original_line_no, body_line), ...]).
    frontmatter_end_index is the 0-based index of the closing --- line,
    or -1 if no frontmatter found.
    Body lines carry their original 1-based line numbers.
    """
    if not lines:
        return -1, []

    if lines[0].strip() != '---':
        return -1, [(i + 1, line) for i, line in enumerate(lines)]

    for i in range(1, len(lines)):
        if lines[i].strip() == '---':
            body = [(j + 1, lines[j]) for j in range(i + 1, len(lines))]
            return i, body

    # Malformed: no closing ---, treat all as body
    return -1, [(i + 1, line) for i, line in enumerate(lines)]


# ── Detectors ─────────────────────────────────────────────────────────

def detect_file_counts(file_path: str, body_lines: list[tuple[int, str]]) -> list[Finding]:
    """Detect 'N 文件' patterns — file count claims."""
    findings = []
    file_dir = os.path.basename(os.path.dirname(file_path))
    file_name = os.path.basename(file_path)
    # Include parent dir for disambiguation
    rel_name = f'{file_dir}/{file_name}' if file_dir and file_dir != 'memory' else file_name

    for line_no, line in body_lines:
        for m in FILE_COUNT_RE.finditer(line):
            matched = m.group(0).strip()
            findings.append(Finding(
                file_name=rel_name,
                line_number=line_no,
                category='file_count',
                confidence='high',
                matched_text=matched,
                suggestion='find <path> -name "*.cs" | wc -l',
            ))
    return findings


def detect_line_counts(file_path: str, body_lines: list[tuple[int, str]]) -> list[Finding]:
    """Detect 'N 行' patterns — line count claims. Excludes rubric scores."""
    findings = []
    file_dir = os.path.basename(os.path.dirname(file_path))
    file_name = os.path.basename(file_path)
    rel_name = f'{file_dir}/{file_name}' if file_dir and file_dir != 'memory' else file_name

    for line_no, line in body_lines:
        # Skip rubric table rows (subjective scores like "| 9.0 |")
        if re.search(r'\|\s*[\d.]+\s*\|', line):
            continue

        for m in LINE_COUNT_RE.finditer(line):
            matched = m.group(0).strip()
            # Skip very small numbers that are likely not line counts
            num_str = m.group(1).replace(',', '').replace('~', '')
            try:
                if int(num_str) < 10:
                    continue
            except ValueError:
                continue

            findings.append(Finding(
                file_name=rel_name,
                line_number=line_no,
                category='line_count',
                confidence='high',
                matched_text=matched,
                suggestion='find <path> -name "*.cs" | xargs wc -l',
            ))
    return findings


def detect_test_counts(file_path: str, body_lines: list[tuple[int, str]]) -> list[Finding]:
    """Detect 'N 测试' patterns — test count claims."""
    findings = []
    file_dir = os.path.basename(os.path.dirname(file_path))
    file_name = os.path.basename(file_path)
    rel_name = f'{file_dir}/{file_name}' if file_dir and file_dir != 'memory' else file_name

    for line_no, line in body_lines:
        for m in TEST_COUNT_RE.finditer(line):
            matched = m.group(0).strip()
            num = int(m.group(1))
            if num < 1:
                continue
            findings.append(Finding(
                file_name=rel_name,
                line_number=line_no,
                category='test_count',
                confidence='high',
                matched_text=matched,
                suggestion='dotnet test --list-tests | wc -l',
            ))
    return findings


def detect_coverage_stats(file_path: str, body_lines: list[tuple[int, str]]) -> list[Finding]:
    """Detect coverage percentage and fraction claims. Excludes narrative comparisons."""
    findings = []
    file_dir = os.path.basename(os.path.dirname(file_path))
    file_name = os.path.basename(file_path)
    rel_name = f'{file_dir}/{file_name}' if file_dir and file_dir != 'memory' else file_name

    for line_no, line in body_lines:
        # Skip narrative comparisons: "覆盖率从 97.5% 降至 95.7%"
        if NARRATIVE_COVERAGE_RE.search(line):
            continue

        has_pct = COVERAGE_PCT_RE.search(line)
        has_frac = COVERAGE_FRAC_RE.search(line)

        # Validate that any fraction looks like a coverage stat:
        # both numbers should be > 100 (coverage lines are in thousands)
        frac_is_coverage = False
        if has_frac:
            try:
                a, b = int(has_frac.group(1)), int(has_frac.group(2))
                frac_is_coverage = a > 100 and b > 100
            except ValueError:
                pass

        if has_pct:
            matched = has_pct.group(0).strip()
            # If there's also a coverage-like fraction on the same line, include it
            if has_frac and frac_is_coverage:
                matched = f'{matched} ({has_frac.group(1)}/{has_frac.group(2)})'

            findings.append(Finding(
                file_name=rel_name,
                line_number=line_no,
                category='coverage_stat',
                confidence='high',
                matched_text=matched,
                suggestion='python3 scripts/coverage-report.py --brief',
            ))
        elif has_frac and frac_is_coverage:
            # Fraction-only lines: only flag if both numbers are coverage-scale
            matched = f'({has_frac.group(1)}/{has_frac.group(2)})'
            findings.append(Finding(
                file_name=rel_name,
                line_number=line_no,
                category='coverage_stat',
                confidence='high',
                matched_text=matched,
                suggestion='python3 scripts/coverage-report.py --brief',
            ))

    return findings


def detect_tree_file_counts(file_path: str, body_lines: list[tuple[int, str]]) -> list[Finding]:
    """Detect box-drawing lines that include file counts — '├── Core/ 18 文件'."""
    findings = []
    file_dir = os.path.basename(os.path.dirname(file_path))
    file_name = os.path.basename(file_path)
    rel_name = f'{file_dir}/{file_name}' if file_dir and file_dir != 'memory' else file_name

    for line_no, line in body_lines:
        if not BOX_DRAWING_RE.search(line):
            continue

        m = TREE_LINE_WITH_COUNT_RE.search(line)
        if m:
            matched = line.strip()[:60]
            findings.append(Finding(
                file_name=rel_name,
                line_number=line_no,
                category='tree_file_block',
                confidence='high',
                matched_text=matched,
                suggestion='find <path> -name "*.cs" | wc -l',
            ))

    return findings


def _is_design_diagram(lines_in_run: list[str]) -> bool:
    """Check if a run of box-drawing lines is a design diagram (not a directory tree)."""
    combined = ' '.join(lines_in_run).lower()

    # Design diagrams have technical labels, not file/path content
    has_design_markers = any(
        kw.lower() in combined for kw in DESIGN_DIAGRAM_KEYWORDS
    )

    # Directory trees have path-like segments ending with /
    has_path_segments = any('/' in line for line in lines_in_run)
    # Or known directory names followed by spaces/file counts
    has_known_dirs = any(
        re.search(rf'\b{re.escape(d)}\b', line)
        for line in lines_in_run
        for d in KNOWN_DIR_NAMES
    )

    # Arrows indicate flow diagrams
    has_arrows = any(ch in combined for ch in ARROW_CHARS)

    # If it has design markers AND no path segments, it's a design diagram
    if has_design_markers and not has_path_segments:
        return True
    # If it has arrows, it's a flow diagram
    if has_arrows:
        return True
    # If it has neither known dirs nor path segments, and has no 文件,
    # it's likely a design diagram
    if not has_known_dirs and not has_path_segments:
        has_file_count = any('文件' in line for line in lines_in_run)
        if not has_file_count:
            return True

    return False


def detect_directory_tree_blocks(file_path: str, body_lines: list[tuple[int, str]]) -> list[Finding]:
    """Detect multi-line directory tree blocks (3+ consecutive box-drawing lines)."""
    findings = []
    file_dir = os.path.basename(os.path.dirname(file_path))
    file_name = os.path.basename(file_path)
    rel_name = f'{file_dir}/{file_name}' if file_dir and file_dir != 'memory' else file_name

    # Build runs of consecutive box-drawing lines
    runs: list[list[tuple[int, str]]] = []
    current_run: list[tuple[int, str]] = []

    for line_no, line in body_lines:
        if BOX_DRAWING_RE.search(line):
            current_run.append((line_no, line))
        else:
            if len(current_run) >= 3:
                runs.append(current_run)
            current_run = []

    # Don't forget the last run
    if len(current_run) >= 3:
        runs.append(current_run)

    for run in runs:
        run_lines = [line for _, line in run]
        if _is_design_diagram(run_lines):
            continue

        start_line = run[0][0]
        end_line = run[-1][0]
        preview = run_lines[0].strip()[:60]

        findings.append(Finding(
            file_name=rel_name,
            line_number=start_line,
            category='directory_tree',
            confidence='medium',
            matched_text=f'lines {start_line}-{end_line}: {preview}',
            suggestion='find <path> -type d | sort  (or `tree`)',
        ))

    return findings


def detect_class_lists(file_path: str, body_lines: list[tuple[int, str]]) -> list[Finding]:
    """Detect comma-separated PascalCase class name lists (3+ names)."""
    findings = []
    file_dir = os.path.basename(os.path.dirname(file_path))
    file_name = os.path.basename(file_path)
    rel_name = f'{file_dir}/{file_name}' if file_dir and file_dir != 'memory' else file_name

    for line_no, line in body_lines:
        # Skip tree block lines — they're handled separately
        if BOX_DRAWING_RE.search(line):
            continue
        # Skip lines that are clearly not class lists (markdown headers, tables, etc.)
        if line.strip().startswith('#') or line.strip().startswith('|'):
            continue

        m = CLASS_LIST_RE.search(line)
        if m:
            matched = m.group(1).strip()
            # Avoid matching non-class text (e.g., long prose with commas)
            names = [n.strip() for n in matched.split(',')]
            # All names must be PascalCase
            if all(re.match(r'^[A-Z][A-Za-z0-9]+$', n) for n in names):
                findings.append(Finding(
                    file_name=rel_name,
                    line_number=line_no,
                    category='class_list',
                    confidence='medium',
                    matched_text=matched[:60],
                    suggestion='ls <path>/*.cs  (filenames are derivable from directory)',
                ))

    return findings


# ── Orchestrator ──────────────────────────────────────────────────────

def detect_derived_facts(file_path: str, confidence_filter: str = 'all',
                         category_filter: str = 'all') -> list[Finding]:
    """Run all detectors against a single memory file."""
    with open(file_path, encoding='utf-8') as f:
        raw_lines = f.readlines()

    _, body_lines = parse_frontmatter(raw_lines)
    if not body_lines:
        return []

    detectors = [
        detect_file_counts,
        detect_line_counts,
        detect_test_counts,
        detect_coverage_stats,
        detect_tree_file_counts,
        detect_directory_tree_blocks,
        detect_class_lists,
    ]

    all_findings = []
    for detector in detectors:
        found = detector(file_path, body_lines)
        if confidence_filter != 'all':
            found = [f for f in found if f.confidence == confidence_filter]
        if category_filter != 'all':
            found = [f for f in found if f.category == category_filter]
        all_findings.extend(found)

    return all_findings


# ── Verify mode ───────────────────────────────────────────────────────

KNOWN_DIR_MAP = {
    'Core': 'packages/fluxformula.core/Runtime/Core',
    'Compiler': 'packages/fluxformula.core/Runtime/Compiler',
    'Blob': 'packages/fluxformula.core/Runtime/Blob',
    'Cache': 'packages/fluxformula.core/Runtime/Cache',
    'Format': 'packages/fluxformula.core/Runtime/Format',
    'Lexer': 'packages/fluxformula.core/Runtime/Lexer',
    'Literal': 'packages/fluxformula.core/Runtime/Literal',
    'WAL': 'packages/fluxformula.core/Runtime/WAL',
    'SourceGenerator': 'packages/fluxformula.core/SourceGenerator',
}


def count_cs_files(repo_root: str, rel_path: str) -> int:
    """Count .cs files in a directory using find."""
    full_path = os.path.join(repo_root, rel_path)
    if not os.path.isdir(full_path):
        return -1

    result = subprocess.run(
        ['find', full_path, '-name', '*.cs', '-type', 'f'],
        capture_output=True, text=True,
    )
    if result.returncode != 0:
        return -1

    files = [f for f in result.stdout.split('\n') if f]
    return len(files)


def verify_tree_structure(memory_dir: str, repo_root: str) -> list[dict]:
    """
    Verify file-count claims in monorepo-architecture.md against actual repo.

    Returns a list of discrepancy dicts: {directory, claimed, actual}.
    """
    arch_file = os.path.join(memory_dir, 'architecture', 'monorepo-architecture.md')
    if not os.path.exists(arch_file):
        return []

    with open(arch_file, encoding='utf-8') as f:
        content = f.read()

    # Extract claims: "├── Core/       18 文件 —" or "└── WAL/         9 文件 —"
    claims = {}
    for m in re.finditer(r'[├└]──\s+(\w+)/\s+(\d+)\s*文件', content):
        dir_name = m.group(1)
        count = int(m.group(2))
        if dir_name not in claims:
            claims[dir_name] = count

    discrepancies = []
    for dir_name, claimed in sorted(claims.items()):
        if dir_name not in KNOWN_DIR_MAP:
            continue

        actual = count_cs_files(repo_root, KNOWN_DIR_MAP[dir_name])
        if actual < 0:
            continue  # Directory not found — skip

        if claimed != actual:
            discrepancies.append({
                'directory': dir_name,
                'path': KNOWN_DIR_MAP[dir_name],
                'claimed': claimed,
                'actual': actual,
            })

    return discrepancies


# ── Output formatters ─────────────────────────────────────────────────

CATEGORY_LABELS = {
    'file_count': 'file_count',
    'line_count': 'line_count',
    'test_count': 'test_count',
    'coverage_stat': 'coverage ',
    'tree_file_block': 'tree+file',
    'directory_tree': 'dir_tree ',
    'class_list': 'classlist',
}

CATEGORY_ORDER = [
    'file_count', 'line_count', 'test_count', 'coverage_stat',
    'tree_file_block', 'directory_tree', 'class_list',
]


def format_default(findings: list[Finding]) -> str:
    """Format findings as a grouped table."""
    if not findings:
        return 'No derived facts found in memory files.'

    lines = ['Derived facts in memory files',
             '=' * 78]

    grouped = defaultdict(list)
    for f in findings:
        grouped[f.category].append(f)

    for cat in CATEGORY_ORDER:
        cat_findings = grouped.get(cat, [])
        if not cat_findings:
            continue
        for f in sorted(cat_findings, key=lambda x: (x.file_name, x.line_number)):
            label = CATEGORY_LABELS.get(cat, cat[:10])
            preview = f.matched_text[:45].replace('\n', ' ')
            lines.append(f'{label:>10}  {f.location:<30}  "{preview}"')

    lines.append('─' * 78)
    high_count = sum(1 for f in findings if f.confidence == 'high')
    med_count = sum(1 for f in findings if f.confidence == 'medium')
    file_set = {f.file_name for f in findings}
    lines.append(
        f'{len(findings)} derived facts across {len(file_set)} files '
        f'(high: {high_count}, medium: {med_count})'
    )
    return '\n'.join(lines)


def format_brief(findings: list[Finding]) -> str:
    """One-line summary."""
    high_count = sum(1 for f in findings if f.confidence == 'high')
    med_count = sum(1 for f in findings if f.confidence == 'medium')
    file_set = {f.file_name for f in findings}
    return (f'{len(findings)} derived facts across {len(file_set)} files '
            f'(high: {high_count}, medium: {med_count})')


def format_json(findings: list[Finding]) -> str:
    """JSON output."""
    by_category = defaultdict(int)
    by_confidence = defaultdict(int)
    for f in findings:
        by_category[f.category] += 1
        by_confidence[f.confidence] += 1

    output = {
        'total': len(findings),
        'files_affected': len({f.file_name for f in findings}),
        'by_confidence': dict(by_confidence),
        'by_category': dict(by_category),
        'findings': [
            {
                'file': f.file_name,
                'line': f.line_number,
                'category': f.category,
                'confidence': f.confidence,
                'matched_text': f.matched_text,
                'suggestion': f.suggestion,
            }
            for f in sorted(findings, key=lambda x: (x.file_name, x.line_number))
        ],
    }
    return json.dumps(output, ensure_ascii=False, indent=2)


# ── Main ──────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(
        description='Scan memory/*.md files for derived facts',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog='Examples:\n'
               '  %(prog)s                    # list all derived facts\n'
               '  %(prog)s --brief            # one-line summary\n'
               '  %(prog)s --json             # machine-readable JSON\n'
               '  %(prog)s --verify           # verify file counts against repo\n'
               '  %(prog)s --fail-under 20    # CI gate: exit 1 if >20 facts',
    )
    parser.add_argument('--brief', '-b', action='store_true',
                        help='Print only a one-line summary')
    parser.add_argument('--json', '-j', action='store_true',
                        help='Output as JSON')
    parser.add_argument('--verify', '-v', action='store_true',
                        help='Verify file-count claims against actual repo files')
    parser.add_argument('--fail-under', '-f', type=int, metavar='N',
                        help='Exit with code 1 if more than N derived facts found')
    parser.add_argument('--memory-dir', '-m',
                        help='Path to memory directory (default: auto-detect)')
    parser.add_argument('--repo-root', '-r', default='.',
                        help='Repository root for --verify mode (default: current dir)')
    parser.add_argument('--confidence', '-c', choices=['high', 'medium', 'all'],
                        default='all',
                        help='Only show findings of this confidence level')
    parser.add_argument('--category', choices=['file_count', 'line_count', 'test_count',
                        'coverage_stat', 'tree_file_block', 'directory_tree',
                        'class_list', 'all'],
                        default='all',
                        help='Only show findings of this category')
    args = parser.parse_args()

    # Ensure UTF-8 output on Windows (box-drawing chars in memory files)
    if hasattr(sys.stdout, 'reconfigure'):
        try:
            sys.stdout.reconfigure(encoding='utf-8')
        except Exception:
            pass

    # Resolve memory directory
    memory_dir = args.memory_dir
    if not memory_dir:
        memory_dir = find_memory_dir()
    if not memory_dir:
        print('Error: Could not find memory directory. '
              'Use --memory-dir to specify the path.', file=sys.stderr)
        sys.exit(2)

    if not os.path.isdir(memory_dir):
        print(f'Error: Memory directory not found: {memory_dir}', file=sys.stderr)
        sys.exit(2)

    # Collect findings
    md_files = find_memory_files(memory_dir)
    all_findings = []
    for md_file in md_files:
        findings = detect_derived_facts(
            str(md_file),
            confidence_filter=args.confidence,
            category_filter=args.category,
        )
        all_findings.extend(findings)

    # ── Output ──────────────────────────────────────────────────

    if args.json:
        print(format_json(all_findings))
    elif args.brief:
        print(format_brief(all_findings))
    else:
        print(format_default(all_findings))

    # ── Verify mode ─────────────────────────────────────────────

    if args.verify:
        print('\n── Verify: file counts ──')
        discrepancies = verify_tree_structure(memory_dir, args.repo_root)
        if not discrepancies:
            print('  All verified file counts match the current repo.')
        else:
            for d in discrepancies:
                delta = d['actual'] - d['claimed']
                sign = '+' if delta > 0 else ''
                print(f'  {d["directory"]:<15} claimed {d["claimed"]:>2} files, '
                      f'actual {d["actual"]:>2} files ({sign}{delta}) '
                      f'— {d["path"]}')
            print(f'  {len(discrepancies)} discrepancy(s) — memory may be stale.')

    # ── CI gate ─────────────────────────────────────────────────

    if args.fail_under is not None and len(all_findings) > args.fail_under:
        print(f'\n{len(all_findings)} derived facts exceeds limit of {args.fail_under}',
              file=sys.stderr)
        sys.exit(1)


if __name__ == '__main__':
    main()
