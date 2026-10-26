#!/usr/bin/env node
// Fails (exit 1) when a switch the app passes the setup of its own is not one the setup
// reads, or the setup spells one anywhere but a HasSwitch call.
//
// The window starts the newer setup with /UPDATEFROMAPP and the command line with
// /UPDATEFROMCLI, and the setup's [Code] reads each through HasSwitch: the first has it
// wait for the app to close and open the app afterwards, the second has it wait and open
// nothing. Setup takes no meaning from a switch it does not know, so a spelling that
// drifts on either side is not an error anywhere: the setup runs without the wait, meets
// a copy still closing, and fails with nothing written. Inno Setup cannot read the C#,
// so this check holds the two spellings together. A switch the setup reads and the app
// no longer passes is left alone: an installed older version passes it to every later
// setup.
//
// A switch here is any /UPDATEFROM word. The C# is read with its comments blanked,
// through csharp-source.mjs, so only spellings in code count, and the tests are left
// out. The setup is read with its comments blanked, through iss-source.mjs. Each
// spelling found is printed with its file and line. What the setup does with a switch it
// reads is not judged here: ci.yml runs the setup with the command line's switch and
// without it.
//
// Run from the repo root: node scripts/check-setup-switches.mjs
import { readFileSync } from 'node:fs';
import { readCSharp } from './csharp-source.mjs';
import { readIss } from './iss-source.mjs';
import { lineIndex, sourceFiles } from './source-files.mjs';

const SETUP_FILE = 'installer/InstallerClean.iss';
// Setup compares a switch with CompareText, which ignores case, so the spellings are
// matched in any case and compared in capitals.
const SWITCH = /\/UPDATEFROM[A-Z]+/gi;

const read = (p) => {
  try {
    return readFileSync(p, 'utf8');
  } catch {
    console.error(`check-setup-switches: cannot read ${p}`);
    process.exit(1);
  }
};

const problems = [];

// What the app passes: every spelling in the code of the projects that ship.
const passed = new Map();
for (const path of sourceFiles('src', '.cs')) {
  if (path.startsWith('src/InstallerClean.Tests/')) continue;
  const code = readCSharp(read(path)).code;
  const lineOf = lineIndex(code);
  for (const m of code.matchAll(SWITCH)) {
    const name = m[0].toUpperCase();
    console.log(`${path}:${lineOf(m.index)}: passes ${m[0]}`);
    if (!passed.has(name)) passed.set(name, `${path}:${lineOf(m.index)}`);
  }
}

// What the setup reads: each HasSwitch call in its code, and no spelling anywhere else.
const setupCode = readIss(read(SETUP_FILE));
const setupLineOf = lineIndex(setupCode);
const calls = [...setupCode.matchAll(/HasSwitch\s*\(\s*'(\/UPDATEFROM[A-Z]+)'\s*\)/gi)].map((m) => ({
  start: m.index,
  end: m.index + m[0].length,
}));
const readBySetup = new Map();
for (const m of setupCode.matchAll(SWITCH)) {
  const line = setupLineOf(m.index);
  if (!calls.some((c) => c.start <= m.index && m.index < c.end)) {
    problems.push(`${SETUP_FILE}:${line}: spells ${m[0]} outside a HasSwitch('...') call`);
    continue;
  }
  console.log(`${SETUP_FILE}:${line}: reads ${m[0]}`);
  if (!readBySetup.has(m[0].toUpperCase())) readBySetup.set(m[0].toUpperCase(), line);
}

if (passed.size === 0) problems.push('src: no /UPDATEFROM switch found in the app; the check has read nothing');
if (readBySetup.size === 0) problems.push(`${SETUP_FILE}: no HasSwitch('/UPDATEFROM...') call found; the check has read nothing`);
for (const [name, where] of passed) {
  if (!readBySetup.has(name)) problems.push(`${where}: passes ${name}, which ${SETUP_FILE} does not read`);
}

if (problems.length > 0) {
  for (const p of problems) console.error(`check-setup-switches: ${p}`);
  process.exit(1);
}
console.log(`check-setup-switches: the setup reads each switch the app passes it: ${[...passed.keys()].join(', ')}`);
