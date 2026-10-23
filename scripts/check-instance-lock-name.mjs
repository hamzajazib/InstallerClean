#!/usr/bin/env node
// Fails (exit 1) when the single-instance mutex is named anywhere but InstanceLock.Name,
// or when the setup's AppMutex names a different one.
//
// The window, a command-line Delete or Move, a command-line update and the setup exclude
// each other only through that one name. A second spelling in the source that drifts
// lets two of them run at once; an AppMutex that drifts lets the setup replace files a
// running copy has open. Inno Setup cannot read the C# constant, so the setup spells the
// name itself, and this check holds the two together.
//
// The C# is read with its comments blanked, through csharp-source.mjs, so a comment
// naming the mutex is not counted as a spelling of it. A .iss comment line starts with
// ';' and is skipped the same way.
//
// Run from the repo root: node scripts/check-instance-lock-name.mjs
import { readFileSync } from 'node:fs';
import { readCSharp } from './csharp-source.mjs';
import { lineIndex, sourceFiles } from './source-files.mjs';

const LOCK_FILE = 'src/InstallerClean.Core/Services/InstanceLock.cs';
const SETUP_FILE = 'installer/InstallerClean.iss';
const MARK = 'InstallerClean_SingleInstance';
// The test pinning the name spells it on purpose, so a change to the constant fails there.
const PIN_FILE = 'src/InstallerClean.Tests/Services.Integration/InstanceLockTests.cs';

const read = (p) => {
  try {
    return readFileSync(p, 'utf8');
  } catch {
    console.error(`check-instance-lock-name: cannot read ${p}`);
    process.exit(1);
  }
};

const problems = [];

const lockCode = readCSharp(read(LOCK_FILE)).code;
const declared = [...lockCode.matchAll(/const\s+string\s+Name\s*=\s*@"([^"]*)"/g)].map((m) => m[1]);
if (declared.length !== 1) {
  problems.push(`${LOCK_FILE}: expected one 'const string Name = @"..."', found ${declared.length}`);
}
const name = declared[0];

const setupLines = read(SETUP_FILE).split(/\r?\n/);
const appMutex = setupLines
  .map((line, i) => ({ line: i + 1, text: line.trim() }))
  .filter((l) => !l.text.startsWith(';') && /^AppMutex\s*=/i.test(l.text));
if (appMutex.length !== 1) {
  problems.push(`${SETUP_FILE}: expected one AppMutex line, found ${appMutex.length}`);
} else {
  const value = appMutex[0].text.replace(/^AppMutex\s*=\s*/i, '');
  console.log(`${SETUP_FILE}:${appMutex[0].line}: AppMutex=${value}`);
  if (name !== undefined && value !== name) {
    problems.push(`${SETUP_FILE}:${appMutex[0].line}: AppMutex is '${value}', InstanceLock.Name is '${name}'`);
  }
}
if (name !== undefined) console.log(`${LOCK_FILE}: InstanceLock.Name = ${name}`);

// Every spelling of the name in source code outside the constant itself.
for (const path of sourceFiles('src', '.cs')) {
  const code = readCSharp(read(path)).code;
  const lineOf = lineIndex(code);
  let at = code.indexOf(MARK);
  while (at !== -1) {
    if (path !== LOCK_FILE && path !== PIN_FILE) problems.push(`${path}:${lineOf(at)}: names the mutex; use InstanceLock.Name`);
    at = code.indexOf(MARK, at + MARK.length);
  }
}

if (problems.length > 0) {
  for (const p of problems) console.error(`check-instance-lock-name: ${p}`);
  process.exit(1);
}
console.log('check-instance-lock-name: one name, and the setup names it');
