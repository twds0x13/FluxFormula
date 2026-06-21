/**
 * update-benchmark-docs.js
 *
 * Reads BenchmarkDotNet JSON results and updates the performance tables in:
 *   - docs/index.md        (Chinese — summary table)
 *   - docs/en/index.md     (English — summary table)
 *   - README.md            (Chinese — flat table)
 *   - README.en.md         (English — flat table)
 *
 * Usage: node scripts/update-benchmark-docs.js [results-dir]
 *   results-dir defaults to "BenchmarkDotNet.Artifacts/results"
 */

const fs = require("fs");
const path = require("path");

// ── Config ──────────────────────────────────────────────────────────

const RESULTS_DIR = process.argv[2] || "BenchmarkDotNet.Artifacts/results";

// Flat table row definitions for README files
const FLAT_ROWS_CN = [
  { type: "LexerBenchmarks", method: "Simple",   stage: "Lexer",   op: "简单表达式" },
  { type: "LexerBenchmarks", method: "Complex",  stage: "Lexer",   op: "复杂表达式" },
  { type: "CompileBenchmarks", method: "Simple",  stage: "Compile", op: "简单表达式" },
  { type: "CompileBenchmarks", method: "Complex", stage: "Compile", op: "复杂表达式" },
  { type: "InterpreterBenchmarks", method: "Simple",  stage: "解释器", op: "简单公式求值" },
  { type: "InterpreterBenchmarks", method: "Complex", stage: "解释器", op: "复杂公式求值" },
  { type: "JitBenchmarks", method: "Simple",  stage: "JIT", op: "简单公式求值" },
  { type: "JitBenchmarks", method: "Complex", stage: "JIT", op: "复杂公式求值" },
];

const FLAT_ROWS_EN = [
  { type: "LexerBenchmarks", method: "Simple",   stage: "Lexer",   op: "Simple expression" },
  { type: "LexerBenchmarks", method: "Complex",  stage: "Lexer",   op: "Complex expression" },
  { type: "CompileBenchmarks", method: "Simple",  stage: "Compile", op: "Simple expression" },
  { type: "CompileBenchmarks", method: "Complex", stage: "Compile", op: "Complex expression" },
  { type: "InterpreterBenchmarks", method: "Simple",  stage: "Interpreter", op: "Simple eval" },
  { type: "InterpreterBenchmarks", method: "Complex", stage: "Interpreter", op: "Complex eval" },
  { type: "JitBenchmarks", method: "Simple",  stage: "JIT", op: "Simple eval" },
  { type: "JitBenchmarks", method: "Complex", stage: "JIT", op: "Complex eval" },
];

const TARGETS = [
  {
    file: "docs/index.md",
    heading: "## 性能一览",
    format: "summary",
    stageNames: ["Lexer", "Compile", "解释器求值", "JIT 求值"],
    hostInfoLine: (info) =>
      `BenchmarkDotNet on ${info.ProcessorName}，.NET ${extractDotNetMajor(info.RuntimeVersion)}，ShortRun：`,
    footer: (jitRatio) =>
      `编译为一次性开销，执行期零堆分配。JIT 比解释器快 ${jitRatio} 倍。`,
  },
  {
    file: "docs/en/index.md",
    heading: "## Performance at a Glance",
    format: "summary",
    stageNames: ["Lexer", "Compile", "Interpreter Eval", "JIT Eval"],
    hostInfoLine: (info) =>
      `BenchmarkDotNet on ${info.ProcessorName}, .NET ${extractDotNetMajor(info.RuntimeVersion)}, ShortRun:`,
    footer: (jitRatio) =>
      `One-time compilation cost. Execution: zero heap allocation. JIT is ${jitRatio}× faster than the interpreter.`,
  },
  {
    file: "README.md",
    heading: "## 性能",
    format: "flat",
    flatRows: FLAT_ROWS_CN,
    hostInfoLine: (info) =>
      `以下数据来自 BenchmarkDotNet（${info.ProcessorName}，.NET ${extractDotNetMajor(info.RuntimeVersion)}，ShortRun）：`,
    footer: (jitRatio) =>
      `编译一次性开销 ~30–110 ns + 数百字节分配。执行期零分配，JIT 比解释器快约 ${jitRatio} 倍。`,
  },
  {
    file: "README.en.md",
    heading: "## Performance",
    format: "flat",
    flatRows: FLAT_ROWS_EN,
    hostInfoLine: (info) =>
      `BenchmarkDotNet on ${info.ProcessorName}, .NET ${extractDotNetMajor(info.RuntimeVersion)}, ShortRun:`,
    footer: (jitRatio) =>
      `One-time compilation cost: ~30–110 ns + a few hundred bytes. Execution: zero allocation. JIT is ~${jitRatio}× faster than the interpreter.`,
  },
];

// Maps BenchmarkDotNet type → config stage index
const TYPE_MAP = {
  LexerBenchmarks: 0,
  CompileBenchmarks: 1,
  InterpreterBenchmarks: 2,
  JitBenchmarks: 3,
};

// ── Helpers ─────────────────────────────────────────────────────────

function extractDotNetMajor(versionStr) {
  // Input: ".NET 9.0.16 (9.0.1626.22923)" → Output: "9"
  if (!versionStr) return "9";
  const match = versionStr.match(/\.NET\s+(\d+)/);
  return match ? match[1] : "9";
}

function formatTime(nanoseconds) {
  if (nanoseconds < 1000) return `~${Math.round(nanoseconds)} ns`;
  return `~${(nanoseconds / 1000).toFixed(1)} μs`;
}

function formatAlloc(bytes) {
  if (bytes === 0) return "**0 B**";
  return `${bytes} B`;
}

function loadBenchmarkData(resultsDir) {
  const files = fs.readdirSync(resultsDir).filter((f) => f.endsWith("-report-full-compressed.json"));
  if (files.length === 0) {
    console.error(`No *-report-full-compressed.json files found in ${resultsDir}`);
    process.exit(1);
  }

  // Collect: { LexerBenchmarks: { Simple: { mean, alloc }, Complex: { mean, alloc } }, ... }
  const data = {};
  /** @type {object|null} */
  let hostInfo = null;

  for (const file of files) {
    const raw = fs.readFileSync(path.join(resultsDir, file), "utf-8");
    const json = JSON.parse(raw);

    if (!hostInfo && json.HostEnvironmentInfo) {
      hostInfo = json.HostEnvironmentInfo;
    }

    for (const bench of json.Benchmarks) {
      const type = bench.Type; // e.g. "LexerBenchmarks"
      const method = bench.Method; // e.g. "Simple", "Complex"
      const mean = bench.Statistics?.Mean;
      const alloc = bench.Memory?.BytesAllocatedPerOperation;

      if (!data[type]) data[type] = {};
      data[type][method] = { mean, alloc };
    }
  }

  return { data, hostInfo };
}

function buildTableRow(stageName, simple, complex) {
  const simpleTime = formatTime(simple.mean);
  const complexTime = formatTime(complex.mean);

  // Allocation: show range if different, else single value
  let allocStr;
  if (simple.alloc === complex.alloc) {
    allocStr = formatAlloc(simple.alloc);
  } else {
    const lo = Math.min(simple.alloc, complex.alloc);
    const hi = Math.max(simple.alloc, complex.alloc);
    if (lo === 0 && hi === 0) {
      allocStr = "**0 B**";
    } else {
      allocStr = `${lo}–${hi} B`;
    }
  }

  return `| ${stageName} | ${simpleTime} | ${complexTime} | ${allocStr} |`;
}

function updateFile(target, data, hostInfo) {
  const content = fs.readFileSync(target.file, "utf-8");
  const lines = content.split(/\r?\n/);

  // Find the heading line index
  let headingIdx = -1;
  for (let i = 0; i < lines.length; i++) {
    if (lines[i].trim() === target.heading) {
      headingIdx = i;
      break;
    }
  }

  if (headingIdx === -1) {
    console.error(`Could not find heading "${target.heading}" in ${target.file}`);
    return false;
  }

  // Find the next heading or end of document
  let endIdx = lines.length;
  for (let i = headingIdx + 1; i < lines.length; i++) {
    if (/^##\s/.test(lines[i].trimStart())) {
      endIdx = i;
      break;
    }
  }

  const hostLine = target.hostInfoLine(hostInfo || { ProcessorName: "Unknown", RuntimeVersion: ".NET 9.0" });

  // Calculate JIT / Interpreter ratio
  const jitSimple = data["JitBenchmarks"]?.["Simple"]?.mean;
  const interpSimple = data["InterpreterBenchmarks"]?.["Simple"]?.mean;
  let jitRatio = "5–11";
  if (jitSimple && interpSimple && interpSimple > 0) {
    jitRatio = Math.round(interpSimple / jitSimple).toString();
  }

  let tableHeader, tableSep, rows;

  if (target.format === "flat") {
    tableHeader = target.file.includes("README.en")
      ? "| Stage | Operation | Time | Allocation |"
      : "| 阶段 | 操作 | 耗时 | 分配 |";
    tableSep = target.file.includes("README.en")
      ? "|------|------|------|------|"
      : "|------|------|------|------|";

    rows = target.flatRows.map((r) => {
      const entry = data[r.type]?.[r.method];
      if (!entry) return null;
      return `| ${r.stage} | ${r.op} | ${formatTime(entry.mean)} | ${formatAlloc(entry.alloc)} |`;
    }).filter(Boolean);
  } else {
    // summary format — one row per stage with Simple | Complex | Allocation
    tableHeader = target.file.includes("/en/")
      ? "| Stage | Simple | Complex | Allocation |"
      : "| 阶段 | 简单表达式 | 复杂表达式 | 分配 |";
    tableSep = target.file.includes("/en/")
      ? "|------|--------|---------|------------|"
      : "|------|-----------|-----------|------|";

    rows = [];
    for (const [type, idx] of Object.entries(TYPE_MAP)) {
      const stageData = data[type];
      if (!stageData) {
        console.warn(`  No data for ${type}, skipping`);
        continue;
      }
      const simple = stageData["Simple"];
      const complex = stageData["Complex"];
      if (!simple || !complex) {
        console.warn(`  Missing Simple/Complex for ${type}, skipping`);
        continue;
      }
      rows[idx] = buildTableRow(target.stageNames[idx], simple, complex);
    }
    rows = rows.filter(Boolean);
  }

  const replacement = [
    target.heading,
    "",
    hostLine,
    "",
    tableHeader,
    tableSep,
    ...rows,
    "",
    target.footer(jitRatio),
  ];

  // Replace lines from headingIdx to endIdx
  const newLines = [...lines.slice(0, headingIdx), ...replacement, ...lines.slice(endIdx)];
  fs.writeFileSync(target.file, newLines.join("\n"), "utf-8");
  console.log(`  Updated ${target.file}`);
  return true;
}

// ── Main ────────────────────────────────────────────────────────────

console.log(`Reading benchmark results from: ${RESULTS_DIR}`);

const { data, hostInfo } = loadBenchmarkData(RESULTS_DIR);
console.log(`  Found data for ${Object.keys(data).length} benchmark types`);

if (hostInfo) {
  console.log(`  Host: ${hostInfo.ProcessorName}, .NET ${hostInfo.RuntimeVersion}`);
}

let updated = 0;
for (const target of TARGETS) {
  if (updateFile(target, data, hostInfo)) updated++;
}

console.log(`\nDone. Updated ${updated} file(s).`);
