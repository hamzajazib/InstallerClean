#!/usr/bin/env node
// Checks the test inventory of counted strings against the switch that classifies
// them.
//
// A counted string is one whose wording changes with a number. Every one of them
// reaches DisplayHelpers.Pluralise with a keyPrefix, and DisplayHelpers.QuestionFor
// decides which question that prefix's one-form answers. The tests that render
// every counted string in every language are driven off one array,
// CountedStringTests.CountedPrefixes, and a theory walks exactly what its array
// holds: a prefix absent from the array is absent from every one of those tests at
// once, and they all still pass.
//
// So the two lists have to hold the same set, and nothing in the build could say
// so. A switch's arms are not enumerable at runtime, which is why this reads the
// source of both rather than reflecting over the assembly.
//
// Checked both ways:
//   - every prefix QuestionFor classifies is in the test inventory
//   - every prefix in the test inventory is classified by QuestionFor
//
// The second direction is not symmetrical decoration. An inventory entry with no
// arm reaches the switch's default and throws, which the tests do catch; this
// names it at the source in seconds instead.
//
// Run from the repo root: node scripts/check-counted-string-inventory.mjs
import { readFileSync } from 'node:fs';
import { readCSharp } from './csharp-source.mjs';

const switchPath = 'src/InstallerClean.Core/Helpers/DisplayHelpers.cs';
const inventoryPath = 'src/InstallerClean.Tests/Helpers/CountedStringTests.cs';

// Each file is read with its comments taken out. A literal is a regular string in the
// code: its quotes are found where the text inside every string and character literal
// is blanked, so a quote in a character literal is not one, and its text is read from
// the code between them. A literal holding an escape is not a prefix and is left out.
const read = (path) => {
  try {
    return readCSharp(readFileSync(path, 'utf8'));
  } catch (e) {
    console.error(`${path}: cannot be read to its end (${e.message}). Refusing to report on it.`);
    process.exit(2);
  }
};

const literals = ({ code, bare }, from, to) =>
  [...bare.slice(from, to).matchAll(/"( +)"/g)]
    .map((m) => code.slice(from + m.index + 1, from + m.index + 1 + m[1].length))
    .filter((text) => !text.includes('\\'));

// The classifier's arms, taken from the head of the switch to its default arm.
// Stopping at the default matters: the throw below it holds prose in double quotes
// that would otherwise read as prefixes.
function classifiedPrefixes(source) {
  const head = source.code.indexOf('QuestionFor(string keyPrefix) => keyPrefix switch');
  if (head === -1) fail(`${switchPath}: could not find the QuestionFor switch. Has it been renamed?`);
  const tail = source.code.indexOf('_ =>', head);
  if (tail === -1) fail(`${switchPath}: the QuestionFor switch has no default arm, so its end cannot be located.`);
  return literals(source, head, tail);
}

// The array the tests walk. Read to its closing brace rather than to the next
// array, because a second inventory sits directly below this one and the two hold
// deliberately different sets.
function inventoryPrefixes(source) {
  const head = source.code.indexOf('private static readonly string[] CountedPrefixes =');
  if (head === -1) fail(`${inventoryPath}: could not find CountedPrefixes. Has it been renamed?`);
  // Both closers, because the array's syntax is not this check's to pin: written as
  // a collection expression it closes with ]; instead, and a reader that knew only
  // one form would run on into the next array and report its members as duplicates
  // of nothing. Whichever comes first is this array's end.
  const braced = source.code.indexOf('};', head);
  const bracketed = source.code.indexOf('];', head);
  const ends = [braced, bracketed].filter((i) => i !== -1);
  if (ends.length === 0) fail(`${inventoryPath}: CountedPrefixes is not closed, so its end cannot be located.`);
  return literals(source, head, Math.min(...ends));
}

function fail(message) {
  console.error(message);
  process.exit(1);
}

const classified = classifiedPrefixes(read(switchPath));
const inventory = inventoryPrefixes(read(inventoryPath));

// PARSE CONTROL, about the READING rather than about the content. A regex that has
// stopped matching yields an empty set, and two empty sets agree with each other,
// so the comparison below would report a clean result over nothing at all. Neither
// figure is written down here, so adding a counted string cannot make this stale.
if (classified.length === 0) fail(`${switchPath}: no prefixes parsed out of QuestionFor, so this run establishes nothing.`);
if (inventory.length === 0) fail(`${inventoryPath}: no prefixes parsed out of CountedPrefixes, so this run establishes nothing.`);

// MUST-HIT CONTROL. A pair every counted-string arrangement has to carry, named so
// that a reader can see the instrument found a real member of each set rather than
// only a count of them.
const mustHit = 'Plural.File';
if (!classified.includes(mustHit)) fail(`${switchPath}: the parse did not find ${mustHit}, so it is not reading the switch.`);
if (!inventory.includes(mustHit)) fail(`${inventoryPath}: the parse did not find ${mustHit}, so it is not reading the array.`);

const missingFromInventory = classified.filter((p) => !inventory.includes(p)).sort();
const missingFromSwitch = inventory.filter((p) => !classified.includes(p)).sort();

const duplicates = (list) => [...new Set(list.filter((p, i) => list.indexOf(p) !== i))].sort();
const classifiedTwice = duplicates(classified);
const listedTwice = duplicates(inventory);

let bad = false;

if (missingFromInventory.length > 0) {
  bad = true;
  console.error(`NOT IN THE TEST INVENTORY (${missingFromInventory.length}), so no test renders them:`);
  for (const p of missingFromInventory) console.error(`  ${p}`);
  console.error(`Add each to CountedPrefixes in ${inventoryPath}.\n`);
}

if (missingFromSwitch.length > 0) {
  bad = true;
  console.error(`NOT CLASSIFIED (${missingFromSwitch.length}), so Pluralise throws on them:`);
  for (const p of missingFromSwitch) console.error(`  ${p}`);
  console.error(`Add an arm for each to QuestionFor in ${switchPath}.\n`);
}

for (const [what, list, where] of [
  ['classified twice', classifiedTwice, switchPath],
  ['listed twice', listedTwice, inventoryPath],
]) {
  if (list.length > 0) {
    bad = true;
    console.error(`${what.toUpperCase()} in ${where}: ${list.join(', ')}\n`);
  }
}

if (bad) {
  console.error(
    'A counted string is classified in QuestionFor and listed in CountedPrefixes, in the same\n'
    + 'edit that adds its resx keys. The tests walk the array, so a prefix that is only in the\n'
    + 'switch is rendered by nothing and every one of those tests still passes.');
  process.exit(1);
}

console.log(`Counted-string inventory OK: ${classified.length} prefix(es) classified in QuestionFor and the same ${inventory.length} listed in CountedPrefixes.`);
console.log(`Both parses found the control ${mustHit}, and neither side holds a duplicate.`);
console.log(`Classified: ${classified.slice().sort().join(', ')}`);
