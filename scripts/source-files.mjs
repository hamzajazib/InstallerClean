// source-files.mjs: lists the source files under a folder and numbers the lines of a
// text, for the checks in this folder that read the source.
//
// sourceFiles(dir, extension) returns the path of every file under dir whose name ends
// in extension. A folder named bin or obj is build output, which mirrors the source, and
// is not entered. Each folder's entries are sorted by name before they are walked, so the
// list comes out in the same order on every machine. Every path is written with forward
// slashes, on Windows as elsewhere: the checks print these paths and compare them with
// paths written by hand, so do not build them with path.join, which writes backslashes
// on Windows.
//
// lineIndex(text) finds every newline in text once and returns a function from an
// offset in text to the number of the line it is on, counted from 1. A line ends at
// '\n' alone, so a '\r' before it stays on its line, and an offset at the end of a text
// ending in '\n' is on the empty line after it, as text.slice(0, at).split('\n').length
// counts. The function answers for any string with its newlines at the same offsets as
// text, which readCSharp's code, bare and comments keep.
import { readdirSync, statSync } from 'node:fs';

export function sourceFiles(dir, extension) {
  const out = [];
  for (const name of readdirSync(dir).sort()) {
    if (name === 'bin' || name === 'obj') continue;
    const path = `${dir}/${name}`;
    if (statSync(path).isDirectory()) out.push(...sourceFiles(path, extension));
    else if (name.endsWith(extension)) out.push(path);
  }
  return out;
}

export function lineIndex(text) {
  const newlines = [];
  for (let at = text.indexOf('\n'); at !== -1; at = text.indexOf('\n', at + 1)) newlines.push(at);
  return (at) => {
    let low = 0;
    let high = newlines.length;
    while (low < high) {
      const middle = (low + high) >> 1;
      if (newlines[middle] < at) low = middle + 1;
      else high = middle;
    }
    return low + 1;
  };
}
