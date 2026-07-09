#!/usr/bin/env python3
"""
Coverage report post-processor.

Reads a Cobertura coverage XML, identifies #else / #endif preprocessor
branches in source files that are dead code on the current TFM, and
outputs adjusted coverage percentages.

Usage:
    # Quick: auto-collect and show only below-threshold classes
    python3 scripts/coverage-report.py

    # Brief: auto-collect and show only the overall percentage
    python3 scripts/coverage-report.py --brief

    # CI gate: exit non-zero if coverage below 95%
    python3 scripts/coverage-report.py --fail-under 95

    # Full detail: show every class
    python3 scripts/coverage-report.py --all

    # Machine-readable JSON
    python3 scripts/coverage-report.py --json

    # From existing XML file
    python3 scripts/coverage-report.py cov.xml --threshold 90
"""

import argparse
import json
import os
import re
import sys
import xml.etree.ElementTree as ET


def find_dead_else_lines(source_dir, filenames):
    """
    Scan source files for #if NET6_0_OR_GREATER / #else / #endif blocks.
    Returns {filename: set(line_numbers)} of lines inside #else blocks
    that are dead on net6.0+ targets.
    """
    dead_lines = {}

    for fname in filenames:
        path = os.path.join(source_dir, fname)
        if not os.path.exists(path):
            continue

        with open(path, encoding='utf-8') as f:
            lines = f.readlines()

        dead = set()
        depth = 0
        in_else = False

        for i, line in enumerate(lines, start=1):
            stripped = line.strip()
            if stripped.startswith('#if ') and 'NET6_0_OR_GREATER' in stripped:
                depth += 1
            elif stripped.startswith('#else') and depth > 0:
                in_else = True
                dead.add(i)
            elif stripped.startswith('#endif'):
                if in_else:
                    dead.add(i)
                in_else = False
                if depth > 0:
                    depth -= 1
            elif in_else and stripped and not stripped.startswith('//'):
                dead.add(i)

        if dead:
            dead_lines[fname] = dead

    return dead_lines


def process_coverage(xml_path, dead_lines):
    """Adjust coverage by removing dead #else lines from valid line counts."""
    tree = ET.parse(xml_path)
    root = tree.getroot()

    results = []
    total_covered = 0
    total_valid = 0
    seen = set()

    for pkg in root.iter('package'):
        for cls in pkg.iter('class'):
            filename = cls.get('filename', '')
            # Strip path to relative filename
            if '\\Runtime\\Core\\' in filename:
                fname = filename.split('\\Runtime\\Core\\')[-1]
            elif '/' in filename:
                fname = filename.split('/')[-1]
            else:
                fname = filename

            lines_elem = cls.find('lines')
            all_lines = lines_elem.findall('line') if lines_elem is not None else []
            name = cls.get('name', '?')
            dedup_key = (name, fname)
            if dedup_key in seen:
                continue
            seen.add(dedup_key)

            dead = dead_lines.get(fname, set())
            uncovered = [l for l in all_lines if l.get('hits', '0') == '0']

            real_valid = len(all_lines) - len([l for l in all_lines if int(l.get('number', '0')) in dead])
            real_uncovered_count = len([l for l in uncovered if int(l.get('number', '0')) not in dead])
            real_covered = real_valid - real_uncovered_count
            real_rate = real_covered / real_valid if real_valid > 0 else 1.0

            total_covered += real_covered
            total_valid += real_valid
            results.append((real_rate, real_covered, real_valid, real_uncovered_count, name))

    overall = total_covered / total_valid if total_valid > 0 else 0
    return results, overall, total_covered, total_valid


def format_table(results, overall, total_covered, total_valid, threshold, show_all):
    """Format the coverage table as a string."""
    lines = []
    lines.append(f"{'Coverage':>7} {'Covered':>7} {'Valid':>6} {'Gap':>4}  Class")
    lines.append(f"{'─'*7:>7} {'─'*7:>7} {'─'*6:>6} {'─'*4:>4}  {'─'*30}")

    shown = 0
    for rate, covered, valid, uncovered, name in sorted(results):
        if not show_all and rate * 100 >= threshold:
            continue
        shown += 1
        pct = f"{rate*100:5.1f}%"
        lines.append(f"{pct:>7} {covered:>7} {valid:>6} {uncovered:>4}  {name}")

    lines.append(f"{'─'*7:>7} {'─'*7:>7} {'─'*6:>6} {'─'*4:>4}  {'─'*30}")
    lines.append(f"{overall*100:5.1f}% {total_covered:>7} {total_valid:>6}       OVERALL (#else filtered)")
    if shown == 0 and not show_all:
        lines.append("  All classes above threshold.")

    return '\n'.join(lines)


def collect_coverage(xml_path, framework, project):
    """Run dotnet-coverage to collect coverage data. Returns True on success."""
    print(f'Collecting coverage ({framework})...')
    cmd = (
        f'dotnet-coverage collect -f cobertura -o {xml_path} '
        f'"dotnet test {project} --framework {framework}"'
    )
    ret = os.system(cmd)
    if ret != 0:
        # dotnet-coverage may fail if tests fail, but XML might still exist
        if os.path.exists(xml_path):
            print('Tests had failures, but coverage XML was generated.', file=sys.stderr)
            return True
        print('dotnet-coverage failed and no coverage XML generated.', file=sys.stderr)
        return False
    return True


def main():
    parser = argparse.ArgumentParser(
        description='Coverage report with #else noise filtered',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog='Examples:\n'
               '  %(prog)s                    # auto-collect, show gaps below 95%\n'
               '  %(prog)s --brief            # auto-collect, one-line summary\n'
               '  %(prog)s --all              # auto-collect, show every class\n'
               '  %(prog)s --fail-under 95    # CI gate mode\n'
               '  %(prog)s --json             # machine-readable JSON\n'
               '  %(prog)s cov.xml -t 90      # from existing XML file')
    parser.add_argument('xml', nargs='?', help='Cobertura coverage XML file (if omitted, auto-collect)')
    parser.add_argument('--source', default='packages/fluxformula.core/Runtime/Core',
                        help='Source directory to scan for #else blocks')
    parser.add_argument('--threshold', '-t', type=float, default=95.0,
                        help='Only show classes below this coverage %% (default 95)')
    parser.add_argument('--all', '-a', action='store_true', help='Show all classes, not just below threshold')
    parser.add_argument('--brief', '-b', action='store_true', help='Only print the overall percentage line')
    parser.add_argument('--json', '-j', action='store_true', help='Output as JSON')
    parser.add_argument('--fail-under', '-f', type=float, metavar='PCT',
                        help='Exit with code 1 if overall coverage is below PCT%%')
    parser.add_argument('--framework', default='net9.0', help='Target framework for test (default net9.0)')
    parser.add_argument('--project', default='tests/FluxFormula.Core.Tests/FluxFormula.Tests.csproj',
                        help='Test project path')
    args = parser.parse_args()

    xml_path = args.xml
    if xml_path is None:
        xml_path = '.coverage-report.xml'
        if not collect_coverage(xml_path, args.framework, args.project):
            sys.exit(2)
        print()

    # Find dead #else lines
    source_files = [f for f in os.listdir(args.source) if f.endswith('.cs')]
    dead_lines = find_dead_else_lines(args.source, source_files)

    # Process coverage
    results, overall, total_covered, total_valid = process_coverage(xml_path, dead_lines)

    # ── Output ──────────────────────────────────────────────────

    if args.json:
        output = {
            'overall_pct': round(overall * 100, 1),
            'covered': total_covered,
            'valid': total_valid,
            'classes': [
                {
                    'name': name,
                    'pct': round(rate * 100, 1),
                    'covered': covered,
                    'valid': valid,
                    'gap': uncovered,
                }
                for rate, covered, valid, uncovered, name in sorted(results)
            ]
        }
        print(json.dumps(output, ensure_ascii=False, indent=2))
    elif args.brief:
        print(f'{overall*100:5.1f}%  ({total_covered}/{total_valid} lines)')
    else:
        print(format_table(results, overall, total_covered, total_valid,
                          args.threshold, args.all))

    # ── CI gate ─────────────────────────────────────────────────
    if args.fail_under is not None and overall * 100 < args.fail_under:
        print(f'\nCoverage {overall*100:.1f}% is below required {args.fail_under}%', file=sys.stderr)
        sys.exit(1)


if __name__ == '__main__':
    main()
