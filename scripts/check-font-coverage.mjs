#!/usr/bin/env node
// Fails (exit 1) when a language's text has a character the font it is drawn in
// lacks, or when a bundled font would change the height of a line.
//
// EACH LANGUAGE IS DRAWN IN ONE FAMILY, named first in a list, and a character
// that family lacks is drawn from the next family that has it. The list comes
// from Type.FontFamily in Themes/Tokens.xaml, unless Helpers/LanguageFonts.cs
// names the language: MontserratCultures take MontserratFamily, and a key of
// WindowsFamilies takes its own list. Every list is read from those two files
// and split the way WPF splits a family list, where ",," is a comma inside a
// name. A name with a "#" is a family in the app's Fonts folder, found among
// the csproj's Resource fonts by its name table. A name without one is a family
// in the Windows Fonts folder, found the same way. A face belongs to a family
// when the family is its name ID 21, else its ID 16, else its ID 1, in any
// language. WPF reads a pack URI up to its third comma as a name no font
// carries, and that name is printed and skipped.
//
// THE RULES.
//
//   Text. Every character of every resx value a language displays, its own or
//   the English it falls back to, is in every face of its first family, and so
//   is every printable ASCII character, U+0020 to U+007E, which digits, paths
//   and program names put on any screen. Every face, so the result does not
//   depend on which face a weight selects. The values include the console
//   host's, which share the resx.
//
//   Sort arrows. The characters a Details window appends to its sorted column
//   heading, read from each C# file that sorts by ListSortDirection, are in
//   every face of the first family or of the second.
//
//   Language menu. Each language's name in MainWindow.xaml.cs's Endonyms is in
//   every face of that language's first family, which is the family it is
//   drawn in on every screen.
//
//   Line height. Every bundled face sets USE_TYPO_METRICS, carries the same
//   ascender, descender and line gap in hhea as in its OS/2 typo fields, and
//   gives a line of 1.5 and a baseline of 1.1 em. WPF takes a family's line as
//   (ascender + descender + line gap) / em and its baseline as (ascender + line
//   gap / 2) / em. LanguageFonts gives each Windows family the theme family's
//   two values, so a line is the same height in every language only while
//   every bundled face sets the same.
//
//   Greek. Every bundled family a list names, other than the first in
//   Type.FontFamily, maps nothing in U+0370 to U+03FF, so a Greek word on a
//   screen in another language takes no letters from it, only from the first
//   family and Segoe UI.
//
// Spaces, line breaks and invisible format characters (Unicode Zs, Cc and Cf)
// are listed apart with the families that carry them, and do not fail: the
// number format supplies the same kind from the user's region, which no file
// here holds.
//
// IT FAILS CLOSED. A source it cannot read, a family no font file carries, a
// bundled font it cannot parse, a satellite resx with no row in
// SupportedLanguages.CultureNames, a row with no satellite, a culture named in
// LanguageFonts that the app does not ship, and a language with no endonym
// each fail rather than shrink what is checked. A Windows font missing from the
// machine fails like any other, naming the folder the check looked in and any
// file there it could not read.
//
// It prints every family's faces and every language's characters, so a pass
// can be read for what it covered.
//
// Run from the repo root: node scripts/check-font-coverage.mjs
// On Windows it reads %WINDIR%\Fonts. Elsewhere, pass a copy of that folder:
//   node scripts/check-font-coverage.mjs --windows-fonts <folder>
import { closeSync, openSync, readdirSync, readFileSync, readSync, statSync } from 'node:fs';
import { join, sep } from 'node:path';

const TOKENS = 'src/InstallerClean/Themes/Tokens.xaml';
const LANGUAGE_FONTS = 'src/InstallerClean/Helpers/LanguageFonts.cs';
const SUPPORTED = 'src/InstallerClean.Core/Helpers/SupportedLanguages.cs';
const CSPROJ = 'src/InstallerClean/InstallerClean.csproj';
const MAIN_WINDOW = 'src/InstallerClean/MainWindow.xaml.cs';
const RESX_DIR = 'src/InstallerClean.Core/Resources';
const APP_DIR = 'src/InstallerClean';
const LINE_SPACING = 1.5;
const BASELINE = 1.1;
const GREEK = [0x0370, 0x03ff];
const USE_TYPO_METRICS = 1 << 7;

const problems = [];
const fail = (message) => {
  console.error(`\ncheck-font-coverage: ${message}`);
  process.exit(1);
};

// --- arguments -----------------------------------------------------------------

const argv = process.argv.slice(2);
let windowsFonts = process.platform === 'win32'
  ? join(process.env.WINDIR ?? 'C:\\Windows', 'Fonts')
  : null;
for (let i = 0; i < argv.length; i++) {
  if (argv[i] === '--windows-fonts' && argv[i + 1]) windowsFonts = argv[++i];
  else fail(`unknown argument ${argv[i]}; the only option is --windows-fonts <folder>`);
}
if (!windowsFonts) fail('not on Windows, so pass the Windows Fonts folder with --windows-fonts <folder>');
try {
  if (!statSync(windowsFonts).isDirectory()) throw new Error();
} catch {
  fail(`${windowsFonts} is not a folder`);
}

// --- reading the sources ---------------------------------------------------------

const read = (file) => {
  try {
    return readFileSync(file, 'utf8');
  } catch {
    return fail(`cannot read ${file}`);
  }
};

const blank = (m) => m.replace(/[^\n]/g, ' ');
// The string-literal alternative is matched first and put back verbatim, so a
// // or /* inside a string is not read as a comment.
const stripCsComments = (s) =>
  s.replace(/"(?:\\.|[^"\\])*"|\/\/[^\n]*|\/\*[\s\S]*?\*\//g, (m) =>
    m.startsWith('"') ? m : blank(m));
const stripXmlComments = (s) => s.replace(/<!--[\s\S]*?-->/g, blank);

// A C# regular string literal, quotes included, to the string it holds.
const csString = (literal) =>
  literal.slice(1, -1).replace(/\\(u[0-9a-fA-F]{4}|.)/g, (_, e) =>
    e.length === 5 ? String.fromCharCode(parseInt(e.slice(1), 16))
      : { n: '\n', t: '\t', r: '\r', 0: '\0' }[e] ?? e);

const xmlText = (s) =>
  s.replace(/&(#x[0-9a-fA-F]+|#[0-9]+|amp|lt|gt|quot|apos);/g, (_, e) =>
    e[0] === '#'
      ? String.fromCodePoint(e[1] === 'x' ? parseInt(e.slice(2), 16) : parseInt(e.slice(1), 10))
      : { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'" }[e]);

const need = (match, file, what) => match ?? fail(`${file}: cannot find ${what}`);
const STRING = /"(?:\\.|[^"\\])*"/g;

const tokensXaml = stripXmlComments(read(TOKENS));
const themeList = xmlText(need(
  tokensXaml.match(/<FontFamily\s+x:Key="Type\.FontFamily"\s*>([^<]*)<\/FontFamily>/),
  TOKENS, '<FontFamily x:Key="Type.FontFamily">')[1]);

const languageFontsCs = stripCsComments(read(LANGUAGE_FONTS));
const montserratCultures = need(
  languageFontsCs.match(/\bMontserratCultures\s*=\s*\{([^}]*)\}/),
  LANGUAGE_FONTS, 'MontserratCultures = { ... }')[1].match(STRING)?.map(csString) ?? [];
const montserratList = csString(need(
  languageFontsCs.match(/\bconst\s+string\s+MontserratFamily\s*=\s*("(?:\\.|[^"\\])*")\s*;/),
  LANGUAGE_FONTS, 'const string MontserratFamily = "..."')[1]);
const windowsFamilies = new Map(
  [...need(
    languageFontsCs.match(/\bWindowsFamilies\s*=\s*new\([^)]*\)\s*\{([^}]*)\}/),
    LANGUAGE_FONTS, 'WindowsFamilies = new(...) { ... }')[1]
    .matchAll(/\[\s*("(?:\\.|[^"\\])*")\s*\]\s*=\s*("(?:\\.|[^"\\])*")/g)]
    .map((m) => [csString(m[1]), csString(m[2])]));
if (montserratCultures.length === 0 || windowsFamilies.size === 0) {
  fail(`${LANGUAGE_FONTS}: read no culture from MontserratCultures or WindowsFamilies`);
}

const supportedCs = stripCsComments(read(SUPPORTED));
const neutral = csString(need(
  supportedCs.match(/\bconst\s+string\s+Neutral\s*=\s*("(?:\\.|[^"\\])*")\s*;/),
  SUPPORTED, 'const string Neutral = "..."')[1]);
const cultureNames = [...need(
  supportedCs.match(/\bCultureNames\s*=\s*new\[\]\s*\{([^}]*)\}/),
  SUPPORTED, 'CultureNames = new[] { ... }')[1].matchAll(/"(?:\\.|[^"\\])*"|\bNeutral\b/g)]
  .map((m) => (m[0] === 'Neutral' ? neutral : csString(m[0])));
if (!cultureNames.includes(neutral)) fail(`${SUPPORTED}: CultureNames does not hold the neutral ${neutral}`);

const mainWindowCs = stripCsComments(read(MAIN_WINDOW));
const endonyms = new Map(
  [...need(
    mainWindowCs.match(/\bEndonyms\s*=\s*new\([^)]*\)\s*\{([^}]*)\}/),
    MAIN_WINDOW, 'Endonyms = new(...) { ... }')[1]
    .matchAll(/\[\s*("(?:\\.|[^"\\])*")\s*\]\s*=\s*("(?:\\.|[^"\\])*")/g)]
    .map((m) => [csString(m[1]).toLowerCase(), csString(m[2])]));

// Paths are normalised to forward slashes, so a report reads the same on
// Windows as elsewhere.
function collect(dir, ext, out = []) {
  for (const name of readdirSync(dir)) {
    if (name === 'bin' || name === 'obj') continue;
    const p = join(dir, name);
    if (statSync(p).isDirectory()) collect(p, ext, out);
    else if (name.endsWith(ext)) out.push(p.split(sep).join('/'));
  }
  return out;
}

// The arrow is whatever a sorting window appends, as the two branches of
// `var arrow = <ascending> ? "..." : "...";`.
const arrowSources = [];
const arrows = new Set();
for (const file of collect(APP_DIR, '.cs')) {
  const text = stripCsComments(read(file));
  if (!/\bListSortDirection\b/.test(text)) continue;
  const m = text.match(/\bvar\s+arrow\s*=[^;?]*\?\s*("(?:\\.|[^"\\])*")\s*:\s*("(?:\\.|[^"\\])*")\s*;/);
  if (!m) {
    problems.push(`${file}: sorts by ListSortDirection and has no \`var arrow = ... ? "..." : "...";\` for this check to read the sort indicator from`);
    continue;
  }
  const drawn = [...csString(m[1]) + csString(m[2])].filter((c) => !/\s/u.test(c));
  for (const c of drawn) arrows.add(c);
  arrowSources.push(`${file}:${text.slice(0, m.index).split('\n').length} ${drawn.join(' ')}`);
}
if (arrowSources.length === 0) fail(`no C# file under ${APP_DIR} sorts by ListSortDirection, so the sort arrows were not read`);

// --- font files ------------------------------------------------------------------

// Positional reads, so the name table of each file in the Windows Fonts folder
// is read without loading the file.
class FontFile {
  constructor(path) {
    this.path = path;
    this.fd = openSync(path, 'r');
    this.size = statSync(path).size;
  }
  bytes(offset, length) {
    if (offset < 0 || length < 0 || offset + length > this.size) {
      throw new Error(`reads past the end of the file at ${offset}+${length}`);
    }
    const b = Buffer.alloc(length);
    readSync(this.fd, b, 0, length, offset);
    return b;
  }
  close() {
    closeSync(this.fd);
  }
}

// The offset of each face's table directory: one for a font file, one per face
// for a collection.
function faceOffsets(file) {
  const head = file.bytes(0, 12);
  const tag = head.toString('latin1', 0, 4);
  if (tag === 'ttcf') {
    const count = head.readUInt32BE(8);
    const list = file.bytes(12, 4 * count);
    return Array.from({ length: count }, (_, i) => list.readUInt32BE(4 * i));
  }
  if (head.readUInt32BE(0) === 0x00010000 || tag === 'OTTO' || tag === 'true') return [0];
  throw new Error(`is not a TrueType or OpenType font or collection (starts ${JSON.stringify(tag)})`);
}

function tablesAt(file, offset) {
  const count = file.bytes(offset + 4, 2).readUInt16BE(0);
  const dir = file.bytes(offset + 12, 16 * count);
  const tables = new Map();
  for (let i = 0; i < count; i++) {
    tables.set(dir.toString('latin1', 16 * i, 16 * i + 4),
      { offset: dir.readUInt32BE(16 * i + 8), length: dir.readUInt32BE(16 * i + 12) });
  }
  return tables;
}

function table(file, tables, tag) {
  const t = tables.get(tag);
  if (!t) throw new Error(`has no ${tag} table`);
  return file.bytes(t.offset, t.length);
}

// The face's family under WPF's grouping: name ID 21, else 16, else 1, each in
// every language the name table holds.
function familyNames(file, tables) {
  const name = table(file, tables, 'name');
  const count = name.readUInt16BE(2);
  const strings = name.readUInt16BE(4);
  const byId = { 1: new Set(), 16: new Set(), 21: new Set() };
  for (let i = 0; i < count; i++) {
    const r = 6 + 12 * i;
    const platform = name.readUInt16BE(r);
    const id = name.readUInt16BE(r + 6);
    if (!byId[id]) continue;
    const start = strings + name.readUInt16BE(r + 10);
    const raw = name.subarray(start, start + name.readUInt16BE(r + 8));
    if (platform === 0 || platform === 3) byId[id].add(Buffer.from(raw).swap16().toString('utf16le'));
    else if (platform === 1) byId[id].add(raw.toString('latin1'));
  }
  for (const id of [21, 16, 1]) if (byId[id].size) return [...byId[id]];
  throw new Error('has no family name');
}

// Every code point a cmap subtable maps to a glyph other than 0. Format 14
// maps variation sequences rather than characters, and is passed over.
function subtableCodes(cmap, at) {
  const format = cmap.readUInt16BE(at);
  const codes = new Set();
  if (format === 0) {
    for (let c = 0; c < 256; c++) if (cmap[at + 6 + c]) codes.add(c);
  } else if (format === 4) {
    const segX2 = cmap.readUInt16BE(at + 6);
    const ends = at + 14;
    const starts = ends + segX2 + 2;
    const deltas = starts + segX2;
    const ranges = deltas + segX2;
    for (let s = 0; s < segX2; s += 2) {
      const end = cmap.readUInt16BE(ends + s);
      const start = cmap.readUInt16BE(starts + s);
      const delta = cmap.readUInt16BE(deltas + s);
      const range = cmap.readUInt16BE(ranges + s);
      for (let c = start; c <= end && c !== 0xffff; c++) {
        let glyph;
        if (range === 0) glyph = (c + delta) & 0xffff;
        else {
          glyph = cmap.readUInt16BE(ranges + s + range + 2 * (c - start));
          if (glyph) glyph = (glyph + delta) & 0xffff;
        }
        if (glyph) codes.add(c);
      }
    }
  } else if (format === 6) {
    const first = cmap.readUInt16BE(at + 6);
    const count = cmap.readUInt16BE(at + 8);
    for (let i = 0; i < count; i++) if (cmap.readUInt16BE(at + 10 + 2 * i)) codes.add(first + i);
  } else if (format === 12 || format === 13) {
    const groups = cmap.readUInt32BE(at + 12);
    for (let g = 0; g < groups; g++) {
      const r = at + 16 + 12 * g;
      const start = cmap.readUInt32BE(r);
      const end = cmap.readUInt32BE(r + 4);
      const glyph = cmap.readUInt32BE(r + 8);
      for (let c = start; c <= end; c++) {
        if (format === 13 ? glyph : glyph + (c - start)) codes.add(c);
      }
    }
  } else if (format !== 14) {
    throw new Error(`has a cmap subtable of format ${format}, which this check does not read`);
  }
  return { format, codes };
}

// The characters the face draws, from its Unicode subtable (3,10 over 3,1 over
// platform 0), and every code point any subtable maps, for the Greek rule.
function characterMaps(file, tables) {
  const cmap = table(file, tables, 'cmap');
  const count = cmap.readUInt16BE(2);
  const subtables = [];
  for (let i = 0; i < count; i++) {
    const r = 4 + 8 * i;
    const platform = cmap.readUInt16BE(r);
    const encoding = cmap.readUInt16BE(r + 2);
    subtables.push({ platform, encoding, ...subtableCodes(cmap, cmap.readUInt32BE(r + 4)) });
  }
  const unicode = subtables.filter((s) => s.format !== 14);
  const best = unicode.find((s) => s.platform === 3 && s.encoding === 10)
    ?? unicode.find((s) => s.platform === 3 && s.encoding === 1)
    ?? unicode.filter((s) => s.platform === 0).sort((a, b) => b.encoding - a.encoding)[0];
  if (!best) throw new Error('has no Unicode cmap subtable');
  const any = new Set();
  for (const s of unicode) for (const c of s.codes) any.add(c);
  return { codes: best.codes, any };
}

function metrics(file, tables) {
  const os2 = table(file, tables, 'OS/2');
  const hhea = table(file, tables, 'hhea');
  const head = table(file, tables, 'head');
  return {
    weight: os2.readUInt16BE(4),
    fsSelection: os2.readUInt16BE(62),
    typo: [os2.readInt16BE(68), os2.readInt16BE(70), os2.readInt16BE(72)],
    hhea: [hhea.readInt16BE(4), hhea.readInt16BE(6), hhea.readInt16BE(8)],
    unitsPerEm: head.readUInt16BE(18),
  };
}

// Each face of a file with its family names. The character map and metrics
// are read later, only for faces a rule uses.
function scanFile(path) {
  const file = new FontFile(path);
  try {
    return faceOffsets(file).map((offset, index) => {
      const tables = tablesAt(file, offset);
      return { path, index, offset, families: familyNames(file, tables) };
    });
  } finally {
    file.close();
  }
}

function loadFace(face) {
  const file = new FontFile(face.path);
  try {
    const tables = tablesAt(file, face.offset);
    Object.assign(face, characterMaps(file, tables), metrics(file, tables));
  } finally {
    file.close();
  }
  return face;
}

const faceName = (f) => `${f.path.split(/[\\/]/).pop()}${f.index || f.path.endsWith('.ttc') ? `[${f.index}]` : ''}`;

// Bundled: the csproj's Resource fonts. Each is read whole and must parse.
const csproj = read(CSPROJ);
const bundledFiles = [...csproj.matchAll(/<Resource\s+Include="([^"]+\.(?:ttf|otf|ttc))"/gi)].map((m) => m[1].replace(/\\/g, '/'));
if (bundledFiles.length === 0) fail(`${CSPROJ}: no <Resource Include="...ttf"> font`);
const bundledFolders = new Set(bundledFiles.map((f) => `/${f.slice(0, f.lastIndexOf('/') + 1)}`));
const bundledFaces = [];
for (const f of bundledFiles) {
  try {
    bundledFaces.push(...scanFile(join(APP_DIR, f)).map((face) => ({ ...face, folder: `/${f.slice(0, f.lastIndexOf('/') + 1)}` })));
  } catch (e) {
    problems.push(`${APP_DIR}/${f}: ${e.message}`);
  }
}

// Windows: every font in the folder. A file that does not parse is listed and
// only fails the run if a family named in a list is then not found.
const windowsFaces = [];
const unreadable = [];
for (const name of readdirSync(windowsFonts).sort()) {
  if (!/\.(ttf|ttc|otf)$/i.test(name)) continue;
  const path = join(windowsFonts, name).split(sep).join('/');
  try {
    windowsFaces.push(...scanFile(path));
  } catch (e) {
    unreadable.push(`${name}: ${e.message}`);
  }
}

// --- family lists ------------------------------------------------------------------

// FontFamilyIdentifier.FindToken: a comma ends a name unless the next
// character is a comma too, and ",," is then one comma inside the name.
function splitFamilyList(list) {
  const names = [];
  let i = 0;
  while (i < list.length) {
    let token = '';
    while (i < list.length) {
      if (list[i] === ',') {
        if (list[i + 1] !== ',') break;
        token += ',';
        i += 2;
      } else token += list[i++];
    }
    i++;
    token = token.trim();
    if (token) names.push(token);
  }
  return names;
}

// Each family in a list, with its faces. `base` is the URI the list is read
// against: Tokens.xaml's own for the theme, the application root for one built
// in code.
const familyCache = new Map();
function resolveList(list, base, owner) {
  const families = [];
  const skipped = [];
  let missing = 0;
  for (const token of splitFamilyList(list)) {
    const hash = token.indexOf('#');
    let key;
    let faces;
    if (hash < 0 && token.includes('://')) {
      skipped.push(token);
      continue;
    }
    if (hash < 0) {
      key = token;
      faces = windowsFaces.filter((f) => f.families.some((n) => n.toUpperCase() === token.toUpperCase()));
      if (faces.length === 0) {
        missing++;
        problems.push(`${owner}: no font in ${windowsFonts} has the family "${token}"`
          + (unreadable.length ? `; these files could not be read: ${unreadable.join('; ')}` : ''));
      }
    } else {
      const family = decodeURIComponent(token.slice(hash + 1));
      const folder = new URL(token.slice(0, hash) || './', base).pathname;
      key = `${folder}#${family}`;
      if (!bundledFolders.has(folder)) {
        missing++;
        problems.push(`${owner}: "${token}" names the folder ${folder}, which holds none of the csproj's Resource fonts`);
        faces = [];
      } else {
        faces = bundledFaces.filter((f) => f.folder === folder && f.families.some((n) => n.toUpperCase() === family.toUpperCase()));
        if (faces.length === 0) {
          missing++;
          problems.push(`${owner}: no Resource font in ${folder} has the family "${family}"`);
        }
      }
    }
    if (!familyCache.has(key)) {
      const loaded = [];
      for (const face of faces) {
        try {
          loaded.push(loadFace(face));
        } catch (e) {
          problems.push(`${faceName(face)}: ${e.message}`);
        }
      }
      familyCache.set(key, { name: key, faces: loaded });
    }
    if (faces.length) families.push(familyCache.get(key));
  }
  if (families.length === 0) problems.push(`${owner}: "${list}" names no family a font carries`);
  return { list, families, skipped, missing };
}

const THEME_BASE = `pack://app/${TOKENS.slice(APP_DIR.length + 1)}`;
const CODE_BASE = 'pack://app/';
const theme = resolveList(themeList, THEME_BASE, `${TOKENS} Type.FontFamily`);
const montserrat = resolveList(montserratList, CODE_BASE, `${LANGUAGE_FONTS} MontserratFamily`);

// --- languages ------------------------------------------------------------------------

const resxFile = (culture) => `${RESX_DIR}/Strings${culture === neutral ? '' : `.${culture}`}.resx`;
function resxValues(culture) {
  const text = stripXmlComments(read(resxFile(culture)));
  const values = new Map();
  for (const m of text.matchAll(/<data\s+([^>]*)>([\s\S]*?)<\/data>/g)) {
    const name = m[1].match(/\bname="([^"]*)"/)?.[1];
    if (!name) continue;
    if (/\b(?:type|mimetype)=/.test(m[1])) {
      problems.push(`${resxFile(culture)}: ${name} is not a string value, which this check does not read`);
      continue;
    }
    const value = m[2].match(/<value>([\s\S]*?)<\/value>|<value\s*\/>/);
    values.set(name, value ? xmlText(value[1] ?? '') : '');
  }
  if (values.size === 0) fail(`${resxFile(culture)}: read no <data> value`);
  return values;
}

const satellites = readdirSync(RESX_DIR)
  .map((n) => n.match(/^Strings\.(.+)\.resx$/)?.[1])
  .filter(Boolean);
for (const s of satellites) {
  if (!cultureNames.includes(s)) problems.push(`${RESX_DIR}/Strings.${s}.resx has no row in SupportedLanguages.CultureNames`);
}
for (const c of cultureNames) {
  if (c !== neutral && !satellites.includes(c)) problems.push(`SupportedLanguages.CultureNames has ${c}, and there is no ${resxFile(c)}`);
}
for (const c of [...montserratCultures, ...windowsFamilies.keys()]) {
  if (!cultureNames.some((n) => n.toLowerCase() === c.toLowerCase())) {
    problems.push(`${LANGUAGE_FONTS} names ${c}, which is not in SupportedLanguages.CultureNames`);
  }
}

const hex = (c) => `U+${c.codePointAt(0).toString(16).toUpperCase().padStart(4, '0')}`;
const inEvery = (family, c) => family.faces.every((f) => f.codes.has(c.codePointAt(0)));
const missingFrom = (family, c) => family.faces.filter((f) => !f.codes.has(c.codePointAt(0))).map(faceName);
const invisible = (c) => /[\p{Zs}\p{Cf}\p{Cc}]/u.test(c);

const english = resxValues(neutral);
const languages = [];
for (const culture of cultureNames.filter((c) => c === neutral || satellites.includes(c))) {
  const own = culture === neutral ? english : resxValues(culture);
  const values = new Map([...english, ...own]);
  const lower = culture.toLowerCase();
  const windowsList = [...windowsFamilies].find(([c]) => c.toLowerCase() === lower)?.[1];
  const chain = montserratCultures.some((c) => c.toLowerCase() === lower) ? montserrat
    : windowsList ? resolveList(windowsList, CODE_BASE, `${LANGUAGE_FONTS} WindowsFamilies["${culture}"]`)
      : theme;
  const [first, second] = chain.families;
  const lang = { culture, chain, file: resxFile(culture), own: own.size, values: values.size, chars: [], spaces: [], arrows: [], endonym: null };
  languages.push(lang);
  // A family missing from the list has already failed the run, and checking
  // the rest against whichever family moved up would report its characters
  // instead.
  if (!first || chain.missing) continue;

  const keysOf = new Map();
  for (let c = 0x20; c <= 0x7e; c++) keysOf.set(String.fromCharCode(c), ['printable ASCII']);
  for (const [key, value] of values) {
    for (const c of value) {
      if (!keysOf.has(c)) keysOf.set(c, []);
      keysOf.get(c).push(key);
    }
  }
  for (const c of [...keysOf.keys()].sort((a, b) => a.codePointAt(0) - b.codePointAt(0))) {
    if (invisible(c)) {
      const carriers = chain.families.filter((f) => inEvery(f, c)).map((f) => f.name);
      lang.spaces.push(`${hex(c)} in ${carriers.length ? carriers.join(', ') : 'none of them'}`);
      continue;
    }
    lang.chars.push(c);
    if (!inEvery(first, c)) {
      const keys = keysOf.get(c);
      problems.push(`${culture}: ${hex(c)} '${c}' is not in ${missingFrom(first, c).join(', ')} (${first.name}); `
        + `${keys.slice(0, 3).join(', ')}${keys.length > 3 ? ` and ${keys.length - 3} more` : ''}`);
    }
  }

  for (const c of arrows) {
    const from = inEvery(first, c) ? first : second && inEvery(second, c) ? second : null;
    lang.arrows.push(`${c} from ${from ? from.name : 'neither'}`);
    if (!from) {
      problems.push(`${culture}: the sort arrow ${hex(c)} '${c}' is in neither ${first.name} nor ${second ? second.name : 'a second family'}`);
    }
  }

  const name = endonyms.get(lower);
  if (name === undefined) {
    problems.push(`${culture}: ${MAIN_WINDOW} Endonyms has no name for it, so the language menu draws a name this check cannot read`);
  } else {
    const missing = [...name].filter((c) => !invisible(c) && !inEvery(first, c));
    lang.endonym = `"${name}" in ${first.name}`;
    for (const c of missing) problems.push(`${culture}: the endonym "${name}" has ${hex(c)} '${c}', which is not in ${missingFrom(first, c).join(', ')}`);
  }
}

// --- bundled faces ------------------------------------------------------------------

const bundledRows = [];
for (const face of bundledFaces) {
  if (face.unitsPerEm === undefined) {
    try {
      loadFace(face);
    } catch (e) {
      problems.push(`${faceName(face)}: ${e.message}`);
      continue;
    }
  }
  const [asc, desc, gap] = face.typo;
  const line = (asc - desc + gap) / face.unitsPerEm;
  const baseline = (asc + gap / 2) / face.unitsPerEm;
  const typoBit = Boolean(face.fsSelection & USE_TYPO_METRICS);
  bundledRows.push(`${faceName(face)} ${face.families.join('/')} ${face.weight}: typo ${face.typo.join('/')}, hhea ${face.hhea.join('/')}, `
    + `em ${face.unitsPerEm}, USE_TYPO_METRICS ${typoBit ? 'set' : 'clear'}, line ${line}, baseline ${baseline}`);
  if (!typoBit) problems.push(`${faceName(face)}: USE_TYPO_METRICS is clear, so WPF may not read the typo values`);
  if (face.hhea.join() !== face.typo.join()) problems.push(`${faceName(face)}: hhea ${face.hhea.join('/')} differs from the typo values ${face.typo.join('/')}`);
  if (Math.abs(line - LINE_SPACING) > 1e-9) problems.push(`${faceName(face)}: a line of ${line} em, not ${LINE_SPACING}`);
  if (Math.abs(baseline - BASELINE) > 1e-9) problems.push(`${faceName(face)}: a baseline of ${baseline} em, not ${BASELINE}`);
}

const greekRows = [];
const greekFamilies = [...familyCache.values()]
  .filter((f) => f !== theme.families[0] && f.faces.some((x) => x.folder));
for (const family of greekFamilies) {
  for (const face of family.faces) {
    const greek = [...face.any].filter((c) => c >= GREEK[0] && c <= GREEK[1]);
    greekRows.push(`${faceName(face)}: ${greek.length ? greek.map((c) => String.fromCodePoint(c)).join('') : 'none'}`);
    if (greek.length) problems.push(`${faceName(face)}: maps Greek ${greek.map((c) => hex(String.fromCodePoint(c))).join(' ')}`);
  }
}

// --- report ------------------------------------------------------------------------------

const print = (title, rows) => {
  console.log(`${title} (${rows.length}):`);
  for (const r of rows) console.log(`  ${r}`);
};
const wrap = (chars) => {
  const out = [];
  for (let i = 0; i < chars.length; i += 80) out.push(chars.slice(i, i + 80).join(''));
  return out;
};

print('Family lists', [
  `${TOKENS} Type.FontFamily: ${themeList}`,
  `${LANGUAGE_FONTS} MontserratFamily (${montserratCultures.join(', ')}): ${montserratList}`,
  ...[...windowsFamilies].map(([c, l]) => `${LANGUAGE_FONTS} WindowsFamilies[${c}]: ${l}`),
  ...[...new Set([theme, montserrat].flatMap((l) => l.skipped))].map((s) => `skipped, a name no font carries: ${s}`),
]);
print('Families and their faces', [...familyCache.values()].map((f) =>
  `${f.name}: ${f.faces.map((x) => `${faceName(x)} ${x.weight}`).join(', ') || 'none'}`));
print('Sort arrows', arrowSources);
print('Bundled faces', bundledRows);
print(`Greek in the bundled families other than the first in Type.FontFamily (${greekFamilies.map((f) => f.name).join(', ') || 'none'})`, greekRows);
if (unreadable.length) print(`Files in ${windowsFonts} that could not be read`, unreadable);
console.log(`Languages (${languages.length}):`);
for (const l of languages) {
  console.log(`  ${l.culture}: ${l.chain.families.map((f) => f.name).join(', ')}; ${l.file}, ${l.own} values of its own, ${l.values} with the English`);
  console.log(`    characters (${l.chars.length}):`);
  for (const line of wrap(l.chars)) console.log(`      ${line}`);
  console.log(`    spaces and format characters: ${l.spaces.join('; ') || 'none'}`);
  console.log(`    sort arrows: ${l.arrows.join('; ')}`);
  console.log(`    endonym: ${l.endonym ?? 'none'}`);
}

if (problems.length) {
  console.error('\ncheck-font-coverage: a language has a character its font lacks, or a bundled font sets another line height.\n');
  for (const p of problems) console.error(`  ${p}`);
  console.error('\nA character missing from a language\'s first family is drawn from the next');
  console.error('family that has it, in another typeface. A bundled face with other metrics');
  console.error('changes the height of every line drawn in it.');
  process.exit(1);
}

console.log('\ncheck-font-coverage: OK');
