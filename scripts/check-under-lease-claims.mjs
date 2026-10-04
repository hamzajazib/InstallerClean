#!/usr/bin/env node
// check-under-lease-claims.mjs: fails where a production call to an action service
// passes anything but the claims the pre-lease re-verify produced.
//
// Both action services take the claims as a required, non-nullable argument, so the
// compiler holds every caller to passing something and not to passing the right
// thing. UnderLeaseClaims.None is a legal value, and RecheckUnderLease returns
// before asking anything for an empty batch, so a caller passing None gets a pass
// from the last check in front of a permanent delete.
//
// What is matched: every call to DeleteFilesAsync or MoveFilesAsync under src/
// outside the test project, read through csharp-source.mjs with the comments and the
// text inside strings taken out. Each has to pass UnderLeaseClaims.From(...) in its
// argument list, found by balancing its brackets, since the call sites wrap across
// lines. What From hands the service is held by CliUnderLeaseClaimsTests and
// MainViewModelTests, which drive the call sites and read what the service received.
//
// The test project is not read. Its tests hand the services an empty batch on
// purpose, to test what a service does with one.
//
// Every call site is printed with whether it passes From. Exit 1 where one does not;
// exit 2 where a file cannot be read to its end, an argument list does not close, or
// fewer than 4 call sites are found.
//
// Usage (from the repo root):
//   node scripts/check-under-lease-claims.mjs
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { readCSharp } from './csharp-source.mjs';

const ROOT = 'src';
const TESTS = join(ROOT, 'InstallerClean.Tests');
const METHODS = ['DeleteFilesAsync', 'MoveFilesAsync'];
const REQUIRED = 'UnderLeaseClaims.From(';

function* csFiles(dir) {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (path === TESTS || name === 'bin' || name === 'obj') continue;
    if (statSync(path).isDirectory()) yield* csFiles(path);
    else if (name.endsWith('.cs')) yield path.replace(/\\/g, '/');
  }
}

// The balanced span after an opening parenthesis, or null where the file ends first.
function argumentSpan(text, openIndex) {
  let depth = 0;
  for (let i = openIndex; i < text.length; i++) {
    if (text[i] === '(') depth++;
    else if (text[i] === ')' && --depth === 0) return text.slice(openIndex + 1, i);
  }
  return null;
}

const problems = [];
const refusals = [];
let callSites = 0;

// Each file is read with its comments and the text inside its strings taken out, so a
// call is code, a bracket is a bracket of the code, and a string holding the text of
// a call is neither.
console.log('Production calls to the action services:');
for (const file of csFiles(ROOT)) {
  let code;
  try {
    code = readCSharp(readFileSync(file, 'utf8')).bare;
  } catch (e) {
    refusals.push(`${file}: cannot be read to its end (${e.message})`);
    continue;
  }
  for (const method of METHODS) {
    const re = new RegExp(`\\.${method}\\s*\\(`, 'g');
    for (const m of code.matchAll(re)) {
      const line = code.slice(0, m.index).split('\n').length;
      const args = argumentSpan(code, code.indexOf('(', m.index));
      if (args === null) {
        refusals.push(`${file}:${line} a call to ${method} whose argument list does not close`);
        continue;
      }
      callSites++;
      const passes = args.includes(REQUIRED);
      console.log(`  ${file}:${line}  ${method}  ${passes ? `passes ${REQUIRED}...)` : `NOT passed ${REQUIRED}...)`}`);
      if (!passes)
        problems.push(
          `${file}:${line} ${method} is not passed `
          + `${REQUIRED}...). The claims the pre-lease re-verify produced are what the `
          + 'under-lease re-read is for; anything else hands it an empty batch and it '
          + 'returns a pass without asking.');
    }
  }
}

for (const r of refusals) console.error(`  REFUSING: ${r}`);
if (refusals.length) process.exit(2);

for (const p of problems) console.error(`  ${p}`);

// A run matching no call site would print no problem, and renaming the methods or
// changing how the calls are written is what would cause it. A floor rather than a
// count, so that adding a caller does not fail this.
if (callSites < 4) {
  console.error(`FLOOR FAILED: ${callSites} production call site(s) found, expected at least 4.`);
  console.error('Refusing to report clean over a set this small: the methods have been renamed, moved or rewritten.');
  process.exit(2);
}

console.log(`TOTALS: ${callSites} production call site(s) checked, ${problems.length} not passing ${REQUIRED}...).`);
process.exit(problems.length ? 1 : 0);
