#!/usr/bin/env node
// Fails (exit 1) when any line of any Cli.Help.* value, in the neutral resx or a
// satellite, is wider than the help screen's column budget.
//
// The budget is 74 columns, comfortably inside an 80-column console. It is a
// fixed number rather than the width of the longest English line, so a
// translation can be written against it and it does not move when the English
// is reworded. Program.cs prints each line of a value with Console.WriteLine and
// wraps nothing, so a line the console cannot fit is broken across two rows by
// the terminal, the second row starting hard against the left margin under a
// two-column layout. Nothing in the resx says how wide a line may be, and a
// faithful translation of a line near the budget can land well over it.
//
// WIDTH IS COUNTED IN TERMINAL COLUMNS, NOT CHARACTERS. A CJK glyph occupies two
// cells in a fixed-pitch console, so a Japanese or Korean line can be among the
// shortest in characters and the widest on screen. East Asian Wide and
// Fullwidth (UAX #11) count 2, everything else 1, which is the same number as a
// character count for any all-Latin or all-Cyrillic value.
//
// The two-column command lines carry a second rule this guard does NOT enforce,
// because it is per-language: the description column is set by the longest
// "installerclean-cli <flag> <METAVAR>" prefix in that language, so a language
// with a long metavariable (Italian PERCORSO, Vietnamese ĐƯỜNG_DẪN) sits further
// right and has less room for the description. Pad with SPACES to that column
// measured in the same terminal columns; padding a CJK metavariable as though it
// were ASCII pushes its description two cells out of line with every other row.
//
// Run from the repo root: node scripts/check-cli-help-width.mjs
import { readdirSync, readFileSync } from 'node:fs';

const dir = 'src/InstallerClean.Core/Resources';
const BUDGET = 74;

// UAX #11 East Asian Wide (W) and Fullwidth (F) ranges: the code points a
// fixed-pitch console draws two cells wide. Everything else, Latin, Cyrillic,
// Greek, Vietnamese diacritics and the halfwidth kana, is one cell.
const WIDE = [
  [0x1100, 0x115f], [0x2e80, 0x303e], [0x3041, 0x33ff], [0x3400, 0x4dbf],
  [0x4e00, 0x9fff], [0xa000, 0xa4cf], [0xa960, 0xa97f], [0xac00, 0xd7a3],
  [0xf900, 0xfaff], [0xfe10, 0xfe19], [0xfe30, 0xfe6f], [0xff00, 0xff60],
  [0xffe0, 0xffe6], [0x1f300, 0x1f64f], [0x1f900, 0x1f9ff],
  [0x20000, 0x2fffd], [0x30000, 0x3fffd],
];
const columns = (s) => {
  let n = 0;
  for (const ch of s) {
    const cp = ch.codePointAt(0);
    n += WIDE.some(([lo, hi]) => cp >= lo && cp <= hi) ? 2 : 1;
  }
  return n;
};

// The value as the console draws it rather than as the XML holds it, which are
// two edits apart.
//
// The resx is XML, so &, <, >, " and the &#10; line break are entities in the
// file and characters by the time ResourceManager hands the value over: an
// &amp; measured raw counts five columns where the console draws one, and a
// &#10; counted raw hides a line break from the split below. &amp; is decoded
// last so that &amp;lt; stays &lt; rather than becoming <.
//
// And Strings.Get spends {InstallerFolder} on the way out (InstallerFolderToken
// over InstallerCacheHelpers.InstallerFolder, which is SpecialFolder.Windows +
// "Installer"), so Cli.Help.Header is three columns wider on screen than in the
// file. A machine whose Windows lives somewhere else draws it wider again, by
// however much longer its path is than C:\Windows\Installer.
const FOLDER = 'C:\\Windows\\Installer';
const printed = (value) => value
  .replace(/&#(\d+);/g, (_, n) => String.fromCodePoint(Number(n)))
  .replace(/&#x([0-9a-fA-F]+);/g, (_, n) => String.fromCodePoint(parseInt(n, 16)))
  .replace(/&lt;/g, '<')
  .replace(/&gt;/g, '>')
  .replace(/&quot;/g, '"')
  .replace(/&apos;/g, "'")
  .replace(/&amp;/g, '&')
  .replaceAll('{InstallerFolder}', FOLDER);

// PARSE CONTROL. About the READING and not about the content: a regex that has
// stopped matching yields an empty set, and a silent zero over an empty set reads
// exactly like a clean result. BOTH legs are load-bearing. raw === 0 catches a
// file that declares no entry at all, which the equality cannot see on its own
// because 0 === 0 holds; parsed !== raw catches entries the reader dropped, which
// one <comment> moved above its <value> does to every regex wanting <value> on the
// same whitespace run as <data>, and the Visual Studio resx editor writes that
// shape. Counted with <data\b rather than '<data ' so a tab after the tag name is
// not read as an empty file. Neither figure is written down here, so adding a
// string to the resx cannot make this go stale.
//
// Cli.Help lines are a SUBSET of what is parsed, so the control counts every entry
// read while the return stays filtered: a reader that had stopped matching would
// leave the filtered list empty, and an empty list is also what a file with no
// long lines produces.
const parseControl = (file, xml, parsed) => {
  const raw = (xml.match(/<data\b/g) || []).length;
  if (raw !== 0 && parsed === raw) return;
  console.error(`PARSE CONTROL FAILED for ${file}: ${raw} '<data' occurrence(s), ${parsed} parsed.`);
  console.error('Refusing to report on a file this check cannot show it read.');
  process.exit(2);
};

const parse = (file) => {
  const xml = readFileSync(`${dir}/${file}`, 'utf8');
  const re = /<data\s+name="([^"]+)"[^>]*>\s*<value>([\s\S]*?)<\/value>/g;
  const out = [];
  let entries = 0;
  let m;
  while ((m = re.exec(xml)) !== null) {
    entries++;
    if (m[1].startsWith('Cli.Help.')) out.push([m[1], printed(m[2])]);
  }
  parseControl(`${dir}/${file}`, xml, entries);
  return out;
};

const files = ['Strings.resx', ...readdirSync(dir)
  .filter((f) => /^Strings\.[A-Za-z-]+\.resx$/.test(f) && f !== 'Strings.resx')
  .sort()];

const failures = [];
let lines = 0;
for (const file of files) {
  for (const [key, value] of parse(file)) {
    // A value may hold several printed lines; each is measured on its own.
    for (const line of value.split('\n')) {
      lines++;
      const w = columns(line);
      if (w > BUDGET) failures.push({ file, key, w, line });
    }
  }
}

if (failures.length) {
  console.error(`CLI help width FAILED (${failures.length} over ${BUDGET} columns):`);
  for (const f of failures) {
    console.error(`  ${f.file} ${f.key}: ${f.w} columns`);
    console.error(`    ${f.line}`);
  }
  console.error(`\nShorten the wording. The budget is the English text's own maximum, and`);
  console.error(`a line over it wraps in the console instead of sitting in its column.`);
  process.exit(1);
}
console.log(`CLI help width OK: ${lines} Cli.Help lines across ${files.length} resx files, all within ${BUDGET} terminal columns.`);
