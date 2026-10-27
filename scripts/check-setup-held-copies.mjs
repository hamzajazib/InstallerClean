#!/usr/bin/env node
// Fails (exit 1) when the setup's [Code] does not hold aside every file [Files] writes to
// {app}, or when the names the setup and the app use for a file held aside, or for the
// mutex the setup holds while it runs, drift apart.
//
// Before writing, the setup renames each file it is about to write to {app} aside, and
// moves it back where the install fails, so a file a running program holds open cannot
// leave {app} part old and part new. A [Files] entry added without its HoldAside call is
// written over in place again, which is not an error anywhere. The window and the command
// line delete the earlier version the setup held aside of their own exe at their next
// start, and leave it while the setup's mutex exists, so the suffix and the mutex name in
// installer/InstallerClean.iss and in ExeReplacement must be the same. Inno Setup cannot
// read the C# constants, so this check holds them together.
//
// Both files are read with their comments blanked, through csharp-source.mjs and
// iss-source.mjs. Each [Files] destination and each HoldAside call found is printed with
// its line.
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
const csSuffix = constant('SetupHeldSuffix');
const csMutex = constant('SetupMutexName');

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
const issSuffix = define('HeldSuffix');
const issMutex = define('SetupMutexName');
if (csSuffix !== undefined && issSuffix !== undefined && csSuffix !== issSuffix) {
  problems.push(`HeldSuffix is '${issSuffix}' in ${SETUP_FILE}, SetupHeldSuffix is '${csSuffix}' in ${CODE_FILE}`);
}
if (csMutex !== undefined && issMutex !== undefined && csMutex !== issMutex) {
  problems.push(`SetupMutexName is '${issMutex}' in ${SETUP_FILE}, '${csMutex}' in ${CODE_FILE}`);
}

const setupMutex = lines.filter((l) => /^SetupMutex\s*=/i.test(l.text));
if (setupMutex.length !== 1) {
  problems.push(`${SETUP_FILE}: expected one SetupMutex line, found ${setupMutex.length}`);
} else {
  console.log(`${SETUP_FILE}:${setupMutex[0].line}: ${setupMutex[0].text}`);
  if (setupMutex[0].text.replace(/\s+/g, '') !== 'SetupMutex={#SetupMutexName}') {
    problems.push(`${SETUP_FILE}:${setupMutex[0].line}: SetupMutex does not name {#SetupMutexName}`);
  }
}

// Each [Files] entry writing to {app}, by the name it is written under.
let section = '';
const written = new Map();
const held = new Map();
for (const l of lines) {
  const heading = l.text.match(/^\[([^\]]+)\]$/);
  if (heading) {
    section = heading[1].toLowerCase();
    continue;
  }
  if (section === 'files' && /^Source\s*:/i.test(l.text)) {
    const field = (name) => l.text.match(new RegExp(`(?:^|;)\\s*${name}\\s*:\\s*"([^"]*)"`, 'i'))?.[1];
    if (field('DestDir') !== '{app}') continue;
    const name = field('DestName') ?? field('Source')?.split('\\').pop();
    console.log(`${SETUP_FILE}:${l.line}: writes {app}\\${name}`);
    written.set(name.toLowerCase(), l.line);
  }
  if (section === 'code') {
    for (const m of l.text.matchAll(/HoldAside\s*\(\s*'([^']*)'\s*\)/g)) {
      console.log(`${SETUP_FILE}:${l.line}: holds aside ${m[1]}`);
      held.set(m[1].toLowerCase(), l.line);
    }
  }
}

if (written.size === 0) problems.push(`${SETUP_FILE}: no [Files] entry writing to {app} found; the check has read nothing`);
if (held.size === 0) problems.push(`${SETUP_FILE}: no HoldAside('...') call found; the check has read nothing`);
for (const [name, line] of written) {
  if (!held.has(name)) problems.push(`${SETUP_FILE}:${line}: writes {app}\\${name}, which [Code] does not hold aside`);
}
for (const [name, line] of held) {
  if (!written.has(name)) problems.push(`${SETUP_FILE}:${line}: holds aside ${name}, which [Files] does not write to {app}`);
}

if (problems.length > 0) {
  for (const p of problems) console.error(`check-setup-held-copies: ${p}`);
  process.exit(1);
}
console.log(`check-setup-held-copies: the setup holds aside each of the ${written.size} files it writes to {app}, and its names match ExeReplacement`);
