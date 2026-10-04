#!/usr/bin/env node
// check-debug-only-switches.mjs: fail when the name of a Debug-only switch appears in a
// C# file under src/ anywhere but between #if DEBUG and its #endif, or appears nowhere.
//
// WHAT A SWITCH IS HERE. An environment variable a Debug build reads to change what the
// app does on the developer's own PC. The window's start check has one, which leaves the
// Application log unread. A Release build must not contain it, so that no shipped copy of
// the app can be told to behave that way. Code between #if DEBUG and #endif is not
// compiled into a Release build, the name's text included.
//
// WHY THE NAME AND NOT THE CODE. The text is what a Release binary would carry, and a
// read of the variable has to spell it somewhere. A name found outside the block is a
// read or a constant a Release build compiles.
//
// THE NAME HAS TO BE FOUND, which is the control. A switch renamed in the code and not
// here would otherwise pass this check by matching nothing.
//
// WHAT THIS DOES NOT COVER, AND THE RUN SAYS SO. A block guarded by a compound condition
// (#if DEBUG && X) is treated as outside, so it fails rather than passes. A name built
// from pieces is not seen.
//
// Usage (from the repo root):
//   node scripts/check-debug-only-switches.mjs
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';

// Each switch, and the folder its read must be found in.
const SWITCHES = [
  { name: 'INSTALLERCLEAN_DEBUG_SKIP_APPLICATION_LOG', home: 'src/InstallerClean.Core/' },
];

const ROOT = 'src';

const csFiles = (dir) => {
  const out = [];
  for (const entry of readdirSync(dir)) {
    if (entry === 'bin' || entry === 'obj') continue;
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) out.push(...csFiles(path));
    else if (path.endsWith('.cs')) out.push(path);
  }
  return out;
};

// Walks one file's preprocessor lines. A line is inside the Debug block where any
// enclosing #if is exactly DEBUG and the line is in that #if's own branch, not in an
// #elif or #else after it.
const occurrences = (path, names) => {
  const found = [];
  const stack = [];
  const lines = readFileSync(path, 'utf8').split('\n');
  lines.forEach((text, i) => {
    const directive = text.trim().replace(/\/\/.*$/, '').trim();
    if (directive.startsWith('#if ')) {
      stack.push({ debug: directive.slice(4).trim() === 'DEBUG', active: true });
      return;
    }
    if (directive.startsWith('#elif') || directive === '#else') {
      if (stack.length) stack[stack.length - 1].active = false;
      return;
    }
    if (directive === '#endif') {
      stack.pop();
      return;
    }
    for (const name of names) {
      if (text.includes(name)) {
        found.push({ name, path, line: i + 1, inDebug: stack.some((f) => f.debug && f.active) });
      }
    }
  });
  return found;
};

const names = SWITCHES.map((s) => s.name);
const all = csFiles(ROOT).flatMap((path) => occurrences(path, names));
const problems = [];

for (const { name, home } of SWITCHES) {
  const mine = all.filter((o) => o.name === name);
  console.log(`${name}:`);
  for (const o of mine) {
    console.log(`  ${o.path}:${o.line}  ${o.inDebug ? 'inside #if DEBUG' : 'OUTSIDE #if DEBUG'}`);
    if (!o.inDebug) problems.push(`${o.path}:${o.line} names ${name} outside #if DEBUG`);
  }
  if (!mine.some((o) => o.path.replace(/\\/g, '/').startsWith(home))) {
    problems.push(`${name} is not found under ${home}, so this check is reading nothing`);
  }
}

console.log('Not covered: a compound #if is treated as outside; a name built from pieces is not seen.');

if (problems.length) {
  console.error('\ncheck-debug-only-switches: a Debug-only switch can reach a Release build.\n');
  for (const p of problems) console.error(`  ${p}`);
  process.exit(1);
}
console.log('check-debug-only-switches: OK');
