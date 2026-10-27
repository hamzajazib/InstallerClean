#!/usr/bin/env node
// Fails (exit 1) when the setup's HoldAllAside does not hold aside every file [Files]
// writes to {app}, or does not stop at the first it cannot, or when the name the setup
// gives an earlier file left once an install has completed drifts from the one the app
// deletes, or the app names the held suffix.
//
// Before any file is written, the setup renames each file it writes to {app} aside, and
// moves each back where the install fails or where one cannot be renamed, so a file a
// running program holds open never leaves {app} part old and part new. A file left out
// of HoldAllAside is written over in place, which is not an error anywhere. Where a held
// file is still running once the install completes, the setup renames it to a done
// name, which the window and the command line delete beside their own exe at their next
// start, so DoneSuffix in installer/InstallerClean.iss and SetupDoneSuffix in
// ExeReplacement must be the same. A held file can be the only copy of an earlier one
// after a failed install, so the app must never name the held suffix. Inno Setup
// cannot read the C# constants, so this check holds them together.
//
// Both files are read with their comments blanked, through csharp-source.mjs and
// iss-source.mjs. Each [Files] destination and each HoldAside call is printed with its
// line.
//
// Run from the repo root: node scripts/check-setup-held-copies.mjs
import { readFileSync } from 'node:fs';
import { readCSharp } from './csharp-source.mjs';
import { readIss } from './iss-source.mjs';

const SETUP_FILE = 'installer/InstallerClean.iss';
const CODE_FILE = 'src/InstallerClean.Core/Services/ExeReplacement.cs';

const read = (p) => {
  try {
    return readFileSync(p, 'utf8');
  } catch {
    console.error(`check-setup-held-copies: cannot read ${p}`);
    process.exit(1);
  }
};

const problems = [];

const code = readCSharp(read(CODE_FILE)).code;
const constant = (name) => {
  const found = [...code.matchAll(new RegExp(`const\\s+string\\s+${name}\\s*=\\s*@?"([^"]*)"`, 'g'))].map((m) => m[1]);
  if (found.length !== 1) problems.push(`${CODE_FILE}: expected one 'const string ${name}', found ${found.length}`);
  return found[0];
};
const csDone = constant('SetupDoneSuffix');

const lines = readIss(read(SETUP_FILE))
  .split(/\r?\n/)
  .map((text, i) => ({ line: i + 1, text: text.trim() }));

const define = (name) => {
  const found = lines.filter((l) => new RegExp(`^#define\\s+${name}\\b`).test(l.text));
  if (found.length !== 1) {
    problems.push(`${SETUP_FILE}: expected one #define ${name}, found ${found.length}`);
    return undefined;
  }
  console.log(`${SETUP_FILE}:${found[0].line}: ${found[0].text}`);
  const m = found[0].text.match(new RegExp(`^#define\\s+${name}\\s+"([^"]*)"\\s*$`));
  if (!m) problems.push(`${SETUP_FILE}:${found[0].line}: ${name} is not a plain quoted string`);
  return m ? m[1] : undefined;
};
const issHeld = define('HeldSuffix');
const issDone = define('DoneSuffix');
if (csDone !== undefined && issDone !== undefined && csDone !== issDone) {
  problems.push(`DoneSuffix is '${issDone}' in ${SETUP_FILE}, SetupDoneSuffix is '${csDone}' in ${CODE_FILE}`);
}
// A held file can be the only copy of an earlier one, so the app never deletes one.
if (issHeld !== undefined && issDone !== undefined && (issHeld === issDone || issDone.startsWith(issHeld))) {
  problems.push(`${SETUP_FILE}: DoneSuffix '${issDone}' does not tell a done file from a held one ('${issHeld}')`);
}
if (issHeld !== undefined && code.includes(issHeld)) {
  problems.push(`${CODE_FILE}: names the held suffix '${issHeld}', which only the setup moves and only the uninstaller deletes`);
}

// Each [Files] entry writing under {app}, by the path it writes relative to {app}. A
// field's value may be quoted or not, and {app} is matched in any case, as Inno Setup
// reads them.
let section = '';
const written = new Map();
for (const l of lines) {
  const heading = l.text.match(/^\[([^\]]+)\]$/);
  if (heading) {
    section = heading[1].toLowerCase();
    continue;
  }
  if (section !== 'files' || l.text === '') continue;
  const field = (name) => {
    const m = l.text.match(new RegExp(`(?:^|;)\\s*${name}\\s*:\\s*("([^"]*)"|[^;]*)`, 'i'));
    return m ? (m[2] ?? m[1]).trim() : undefined;
  };
  const destDir = field('DestDir');
  if (destDir === undefined) {
    problems.push(`${SETUP_FILE}:${l.line}: a [Files] entry with no DestDir the check can read`);
    continue;
  }
  const under = destDir.match(/^\{app\}(?:\\(.*))?$/i);
  if (!under) continue;
  const leaf = field('DestName') ?? field('Source')?.split('\\').pop();
  if (!leaf) {
    problems.push(`${SETUP_FILE}:${l.line}: a [Files] entry for {app} with no Source or DestName the check can read`);
    continue;
  }
  const sub = (under[1] ?? '').replace(/\\+$/, '');
  const name = sub ? `${sub}\\${leaf}` : leaf;
  console.log(`${SETUP_FILE}:${l.line}: writes {app}\\${name}`);
  written.set(name.toLowerCase(), { name, line: l.line });
}

// The calls in HoldAllAside, each one's answer carried in Result, so the first file
// that cannot be held aside stops the install.
const text = lines.map((l) => l.text).join('\n');
const body = text.match(/function\s+HoldAllAside\s*:\s*Boolean\s*;\s*begin([\s\S]*?)\nend;/i);
const held = new Map();
if (!body) problems.push(`${SETUP_FILE}: no 'function HoldAllAside: Boolean;' found`);
else {
  const startLine = text.slice(0, body.index).split('\n').length;
  for (const m of body[1].matchAll(/HoldAside\s*\(\s*'([^']*)'\s*\)/g)) {
    const line = startLine + body[0].slice(0, body[0].indexOf(body[1]) + m.index).split('\n').length - 1;
    const statement = body[1].slice(0, m.index).split(';').pop() + m[0];
    console.log(`${SETUP_FILE}:${line}: holds aside ${m[1]}`);
    const shape = held.size === 0 ? /^\s*Result\s*:=\s*HoldAside/i : /^\s*if\s+Result\s+then\s+Result\s*:=\s*HoldAside/i;
    if (!shape.test(statement)) {
      problems.push(`${SETUP_FILE}:${line}: HoldAside('${m[1]}') does not carry its answer in Result, after the call before it answered true`);
    }
    held.set(m[1].toLowerCase(), line);
  }
}

if (written.size === 0) problems.push(`${SETUP_FILE}: no [Files] entry writing to {app} found; the check has read nothing`);
if (body && held.size === 0) problems.push(`${SETUP_FILE}: HoldAllAside holds nothing aside; the check has read nothing`);
for (const [key, { name, line }] of written) {
  if (!held.has(key)) problems.push(`${SETUP_FILE}:${line}: writes {app}\\${name}, which HoldAllAside does not hold aside`);
}
for (const [key, line] of held) {
  if (!written.has(key)) problems.push(`${SETUP_FILE}:${line}: holds aside ${key}, which [Files] does not write to {app}`);
}
if (!/if\s+not\s+HoldAllAside\s+then/i.test(text)) problems.push(`${SETUP_FILE}: HoldAllAside's answer is never acted on`);

if (problems.length > 0) {
  for (const p of problems) console.error(`check-setup-held-copies: ${p}`);
  process.exit(1);
}
console.log(`check-setup-held-copies: HoldAllAside holds aside each of the ${written.size} files the setup writes to {app}, and its names match ExeReplacement`);
