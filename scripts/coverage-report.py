#!/usr/bin/env python3
"""
Coverage report post-processor.

Reads a Cobertura coverage XML, identifies #else / #endif preprocessor
branches in source files that are dead code on the current TFM, and
outputs adjusted coverage percentages.

Usage:
    dotnet-coverage collect -f cobertura -o cov.xml "dotnet test ..."
    python3 scripts/coverage-report.py cov.xml

Output:
    Per-class coverage with #else noise excluded.
    Print only classes below --threshold (default 95%).
"""

import argparse
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
    ifdef_re = re.compile(r'^\s*#\s*if\s+NET6_0_OR_GREATER')

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
                dead.add(i)  # the #else directive itself
            elif stripped.startswith('#endif'):
                if in_else:
                    dead.add(i)  # the #endif
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
    seen = set()  # dedup: same class may appear in multiple packages

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

            # Only count class-level <lines> to avoid double-counting method-level duplicates
            lines_elem = cls.find('lines')
            all_lines = lines_elem.findall('line') if lines_elem is not None else []
            name = cls.get('name', '?')
            # Dedup by combining name + filename
            dedup_key = (name, fname)
            if dedup_key in seen:
                continue
            seen.add(dedup_key)

            # Find dead #else lines for this file
            dead = dead_lines.get(fname, set())
            uncovered = [l for l in all_lines if l.get('hits', '0') == '0']

            # Exclude dead #else lines from both valid and uncovered counts
            real_valid = len(all_lines) - len([l for l in all_lines if int(l.get('number', '0')) in dead])
            real_uncovered_count = len([l for l in uncovered if int(l.get('number', '0')) not in dead])
            real_covered = real_valid - real_uncovered_count
            real_rate = real_covered / real_valid if real_valid > 0 else 1.0

            total_covered += real_covered
            total_valid += real_valid
            results.append((real_rate, real_covered, real_valid, real_uncovered_count, name))

    overall = total_covered / total_valid if total_valid > 0 else 0
    return results, overall, total_covered, total_valid


def main():
    parser = argparse.ArgumentParser(description='Coverage report with #else noise filtered')
    parser.add_argument('xml', nargs='?', help='Cobertura coverage XML file (if omitted, auto-collect)')
    parser.add_argument('--source', default='packages/fluxformula.core/Runtime/Core',
                        help='Source directory to scan for #else blocks')
    parser.add_argument('--threshold', type=float, default=95.0,
                        help='Only show classes below this coverage %% (default 95)')
    parser.add_argument('--all', action='store_true', help='Show all classes, not just below threshold')
    parser.add_argument('--framework', default='net8.0', help='Target framework for test (default net8.0)')
    parser.add_argument('--project', default='tests/FluxFormula.Core.Tests/FluxFormula.Tests.csproj',
                        help='Test project path')
    args = parser.parse_args()

    xml_path = args.xml
    if xml_path is None:
        # Auto-collect coverage
        xml_path = '.coverage-report.xml'
        print(f'Collecting coverage ({args.framework})...')
        cmd = (
            f'dotnet-coverage collect -f cobertura -o {xml_path} '
            f'"dotnet test {args.project} --framework {args.framework}"'
        )
        ret = os.system(cmd)
        if ret != 0:
            print('dotnet-coverage failed', file=sys.stderr)
            sys.exit(1)
        print()

    # Find dead #else lines
    source_files = [f for f in os.listdir(args.source) if f.endswith('.cs')]
    dead_lines = find_dead_else_lines(args.source, source_files)

    # Process coverage
    results, overall, total_covered, total_valid = process_coverage(xml_path, dead_lines)

    # Output
    print(f"{'Coverage':>7} {'Covered':>7} {'Valid':>6} {'Gap':>4}  Class")
    print(f"{'─'*7:>7} {'─'*7:>7} {'─'*6:>6} {'─'*4:>4}  {'─'*30}")

    shown = 0
    for rate, covered, valid, uncovered, name in sorted(results):
        if not args.all and rate * 100 >= args.threshold:
            continue
        shown += 1
        pct = f"{rate*100:5.1f}%"
        print(f"{pct:>7} {covered:>7} {valid:>6} {uncovered:>4}  {name}")

    print(f"{'─'*7:>7} {'─'*7:>7} {'─'*6:>6} {'─'*4:>4}  {'─'*30}")
    print(f"{overall*100:5.1f}% {total_covered:>7} {total_valid:>6}       OVERALL (#else filtered)")
    if shown == 0:
        print("  All classes above threshold.")


if __name__ == '__main__':
    main()
