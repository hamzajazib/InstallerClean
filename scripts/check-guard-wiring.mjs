#!/usr/bin/env node
// Fails (exit 1) when a guard that both workflows have to invoke by name has
// lost one of those invocations. Only verify-shipped-artefacts.mjs is held this
// way, and the reason is its name: every other guard here is check-*.mjs, which
// the release workflow picks up as a glob, so a new one is run by the release
// on the day it is written and no list can fall out of step.
//
// verify-shipped-artefacts.mjs cannot be in that glob, because the glob runs
// before anything is built and that guard reads built files. It is invoked by
// name after the publish steps in each workflow instead, and a line invoked by
// name is a line that can be deleted or renamed away while everything stays
// green. This is what makes the two invocations a rule rather than a habit.
//
// THE INVOCATION HAS TO OPEN THE COMMAND, after the indent and an optional
// "run:". A line carrying the name anywhere else is text about the guard rather
// than a step that runs it: a commented-out step, a step name, an environment
// value, a string another command echoes. Anchoring it there is what lets this
// file leave YAML's comment rules alone, since it never has to work out whether
// a # opens a comment or sits inside a quoted string.
//
// Run from the repo root: node scripts/check-guard-wiring.mjs
import { readFileSync } from 'node:fs';

const GUARD = 'scripts/verify-shipped-artefacts.mjs';

const WORKFLOWS = [
  '.github/workflows/ci.yml',
  '.github/workflows/release.yml',
];

const read = (p) => {
  try {
    return readFileSync(p, 'utf8');
  } catch {
    console.error(`check-guard-wiring: cannot read ${p}`);
    process.exit(1);
  }
};

// The test is on the line rather than on the whole file, and it takes both
// shapes these workflows use for a node step: straight after "run:", and alone
// on its own line inside a block scalar. Whatever follows the name is arguments
// or a trailing comment, and the step runs the guard on either reading. GUARD
// is a literal, and the one character in it a regular expression reads as
// anything but itself is the dot before mjs.
const RUNS_GUARD = new RegExp(
  String.raw`^\s*(?:run:\s+)?node ${GUARD.replace(/\./g, '\\.')}(?:\s|$)`,
);

const invocations = (yaml) =>
  yaml
    .split('\n')
    .map((text, i) => ({ line: i + 1, text }))
    .filter(({ text }) => RUNS_GUARD.test(text));

const problems = [];
const wired = [];

for (const path of WORKFLOWS) {
  const found = invocations(read(path));
  if (found.length === 0) {
    problems.push(`${path}: no step runs "node ${GUARD}"`);
  }
  for (const { line, text } of found) wired.push(`  ${path}:${line}: ${text.trim()}`);
}

if (problems.length) {
  console.error(`check-guard-wiring: ${GUARD} has to be invoked by both workflows.\n`);
  for (const p of problems) console.error(`  ${p}`);
  console.error(`\nFix: add a step running "node ${GUARD}" after the publish steps`);
  console.error('in the workflow named above. It reads built files, so it has to run after');
  console.error('they exist, which is why it is invoked by name rather than picked up with');
  console.error('the check-*.mjs guards that run before the build. The name has to open the');
  console.error('command, either straight after "run:" or alone on a line in a block scalar.');
  process.exit(1);
}

console.log(`check-guard-wiring: OK, every workflow runs ${GUARD}:`);
for (const w of wired) console.log(w);
