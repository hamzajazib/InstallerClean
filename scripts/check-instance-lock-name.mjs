#!/usr/bin/env node
// Fails (exit 1) when the single-instance mutex is named anywhere but InstanceLock.Name,
// or when the setup's AppMutexName define names a different one, or when the setup
// spells the name anywhere but that define.
//
// The window, a command-line Delete or Move, a command-line update and the setup exclude
// each other only through that one name. A second spelling in the source that drifts
// lets two of them run at once; an AppMutex that drifts lets the setup replace files a
// running copy has open. Inno Setup cannot read the C# constant, so the setup spells the
// name itself, once, in a #define that AppMutex and [Code] both use, and this check holds
// that define and the constant together.
//
// The C# is read with its comments blanked, through csharp-source.mjs, so a comment
// naming the mutex is not counted as a spelling of it. A .iss comment line starts with
// ';', or '//' in [Code], and is skipped the same way.
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

const setupLines = read(SETUP_FILE)
  .split(/\r?\n/)
  .map((text, i) => ({ line: i + 1, text: text.trim() }))
  .filter((l) => !l.text.startsWith(';') && !l.text.startsWith('//'));
const defines = setupLines.filter((l) => /^#define\s+AppMutexName\b/.test(l.text));
const appMutex = setupLines.filter((l) => /^AppMutex\s*=/i.test(l.text));
if (defines.length !== 1) {
  problems.push(`${SETUP_FILE}: expected one #define AppMutexName, found ${defines.length}`);
} else {
  const m = defines[0].text.match(/^#define\s+AppMutexName\s+"([^"]*)"\s*$/);
  const value = m ? m[1] : null;
  console.log(`${SETUP_FILE}:${defines[0].line}: ${defines[0].text}`);
  if (value === null) {
    problems.push(`${SETUP_FILE}:${defines[0].line}: AppMutexName is not a plain quoted string`);
  } else if (name !== undefined && value !== name) {
    problems.push(`${SETUP_FILE}:${defines[0].line}: AppMutexName is '${value}', InstanceLock.Name is '${name}'`);
  }
}
if (appMutex.length !== 1) {
  problems.push(`${SETUP_FILE}: expected one AppMutex line, found ${appMutex.length}`);
} else {
  console.log(`${SETUP_FILE}:${appMutex[0].line}: ${appMutex[0].text}`);
  if (appMutex[0].text.replace(/\s+/g, '') !== 'AppMutex={#AppMutexName}') {
    problems.push(`${SETUP_FILE}:${appMutex[0].line}: AppMutex does not name {#AppMutexName}`);
  }
}
// The wait in [Code] polls the define.
const waits = setupLines.filter((l) => /CheckForMutexes\s*\(/.test(l.text));
if (waits.length === 0) problems.push(`${SETUP_FILE}: no CheckForMutexes call in [Code]`);
for (const l of waits) {
  console.log(`${SETUP_FILE}:${l.line}: ${l.text}`);
  if (!/CheckForMutexes\s*\(\s*'\{#AppMutexName\}'\s*\)/.test(l.text)) {
    problems.push(`${SETUP_FILE}:${l.line}: CheckForMutexes does not poll '{#AppMutexName}'`);
  }
}
// The setup spells the name once, in the define, and names the define everywhere else.
for (const l of setupLines) {
  if (l.text.includes(MARK) && !/^#define\s+AppMutexName\b/.test(l.text)) {
    problems.push(`${SETUP_FILE}:${l.line}: names the mutex; use {#AppMutexName}`);
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
