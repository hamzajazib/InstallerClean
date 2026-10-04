// source-files.mjs: lists the source files under a folder, for the checks in this folder
// that read the source.
//
// sourceFiles(dir, extension) returns the path of every file under dir whose name ends
// in extension. A folder named bin or obj is build output, which mirrors the source, and
// is not entered. Each folder's entries are sorted by name before they are walked, so the
// list comes out in the same order on every machine. Every path is written with forward
// slashes, on Windows as elsewhere: the checks print these paths and compare them with
// paths written by hand, so do not build them with path.join, which writes backslashes
// on Windows.
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
