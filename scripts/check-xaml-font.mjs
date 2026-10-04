#!/usr/bin/env node
// Fails (exit 1) when text can be drawn under a root that does not carry the
// app's font, Type.FontFamily.
//
// TEXT TAKES ITS FONT FROM THE ROOT IT IS DRAWN UNDER. FontFamily is inherited,
// and inheritance carries a value set on the parent, never a default, so an
// element with no font of its own draws in whatever its root was given. A root
// given nothing draws in the system font. The implicit TextBlock style in
// Themes/Components.xaml does not reach every element: a TextBlock carrying a
// Style of its own does not take it, and a TextBox draws its text itself rather
// than through a TextBlock. So the font is held at the roots.
//
// THE ROOTS ARE WINDOWS AND POPUPS, and each has its own rule.
//
//   A window. Every XAML file whose root element is Window sets
//   FontFamily="{DynamicResource Type.FontFamily}" on that root tag. An implicit
//   Window style cannot do it: an implicit style matches its exact type, and
//   every window in the app is a class derived from Window. A window built in C# alone has
//   no root tag to read, so every C# class derived from Window, directly or
//   through another, must be the x:Class of such a file, and C# constructs no
//   Window itself.
//
//   A popup. A ToolTip and a ContextMenu open in a popup with no parent, so
//   they inherit nothing from the window that opens them. Every Style targeting
//   either sets the font, by a Setter of its own or through BasedOn. Each of
//   the two has an implicit style, which is what a ToolTip="..." attribute and
//   a ContextMenu built in code take. A ToolTip or ContextMenu element naming a
//   Style names one of those, and C# sets no Style on either.
//
// EVERY REFERENCE TO THE TOKEN IS A DynamicResource. App.OnStartup writes the
// displayed language's family over Type.FontFamily in the application's own
// resources. A StaticResource inside Components.xaml resolves against the
// dictionaries the style was written in, Tokens.xaml among them, and never sees
// that entry, so a StaticResource anywhere in the XAML fails.
//
// EVERY TABLE FAILS CLOSED. A XAML root element this file does not know, and a
// Style= in C# outside an object initializer whose type it can read, stop the
// build rather than pass unread.
//
// XML and C# comments are blanked before anything is matched, newlines kept, so
// a reported line number is the line on disk and a tag quoted in prose is not
// read as markup. C# is read through csharp-source.mjs, and a file it cannot follow
// to its end stops the run (exit 2).
//
// Run from the repo root: node scripts/check-xaml-font.mjs
import { readFileSync } from 'node:fs';
import { readCSharp } from './csharp-source.mjs';
import { sourceFiles } from './source-files.mjs';

const SRC = 'src';
const FONT = '{DynamicResource Type.FontFamily}';
const POPUPS = ['ToolTip', 'ContextMenu'];
// Roots that draw nothing themselves: Application hosts resources, and a
// ResourceDictionary's styles are checked where they target a popup.
const NON_DRAWING_ROOTS = new Set(['Application', 'ResourceDictionary']);

const blank = (m) => m.replace(/[^\n]/g, ' ');
const stripXmlComments = (s) => s.replace(/<!--[\s\S]*?-->/g, blank);
// C# with its comments taken out, string literals left in place.
const csCode = (file) => {
  try {
    return readCSharp(readFileSync(file, 'utf8')).code;
  } catch (e) {
    console.error(`check-xaml-font: ${file} cannot be read to its end (${e.message}).`);
    console.error('Refusing to report on a file whose code cannot be told from its comments.');
    process.exit(2);
  }
};

const lineAt = (s, i) => s.slice(0, i).split('\n').length;

// Tags in document order, quotes respected, so a '>' inside an attribute value
// does not end the tag. Processing instructions are skipped.
function tags(text) {
  const out = [];
  let i = 0;
  while ((i = text.indexOf('<', i)) !== -1) {
    if (text[i + 1] === '?' || text[i + 1] === '!') {
      i = text.indexOf('>', i) + 1;
      continue;
    }
    let j = i + 1;
    let quote = null;
    for (; j < text.length; j++) {
      const c = text[j];
      if (quote) {
        if (c === quote) quote = null;
      } else if (c === '"' || c === "'") quote = c;
      else if (c === '>') break;
    }
    const raw = text.slice(i, j + 1);
    const m = raw.match(/^<(\/?)([\w:.]+)/);
    if (m) {
      out.push({
        name: m[2],
        closing: m[1] === '/',
        selfClosing: raw.endsWith('/>'),
        raw,
        line: lineAt(text, i),
      });
    }
    i = j + 1;
  }
  return out;
}

const attr = (raw, name) => {
  const m = raw.match(new RegExp(`\\s${name.replace('.', '\\.')}="([^"]*)"`));
  return m ? m[1] : null;
};

// The key a {StaticResource ...} names: a plain key, or {x:Type T}, which is
// the key an implicit style for T is stored under.
const styleKeyOf = (value) =>
  value.match(/^\{StaticResource\s+([\w.]+|\{x:Type\s+[\w:]+\})\}$/)?.[1]?.replace(/\s+/g, ' ') ?? null;

// TargetType="ToolTip" and TargetType="{x:Type ToolTip}" name the same type.
const targetOf = (raw) => {
  const t = attr(raw, 'TargetType');
  if (t === null) return null;
  const m = t.match(/^\{x:Type\s+([\w:]+)\}$/);
  return m ? m[1] : t;
};

const problems = [];
const matched = { windows: [], subclasses: [], popupStyles: [], popupElements: [], csStyles: [], tokenRefs: [] };

const xamlFiles = sourceFiles(SRC, '.xaml');
const csFiles = sourceFiles(SRC, '.cs').filter((f) => !f.endsWith('.Designer.cs'));

if (xamlFiles.length === 0 || csFiles.length === 0) {
  console.error(`check-xaml-font: found ${xamlFiles.length} XAML and ${csFiles.length} C# files under ${SRC}/.`);
  console.error('Both are expected, so the walk is reading the wrong place. Run from the repo root.');
  process.exit(1);
}

// --- XAML -------------------------------------------------------------------

const windowClasses = new Set(); // x:Class of every Window root carrying the font
const popupStyles = []; // { file, line, target, key, basedOn, setsFont, fontValue }
const popupElements = []; // { file, line, name, style }

for (const file of xamlFiles) {
  const text = stripXmlComments(readFileSync(file, 'utf8'));

  for (const m of text.matchAll(/\{(StaticResource|DynamicResource)\s+Type\.FontFamily\s*\}/g)) {
    const line = lineAt(text, m.index);
    matched.tokenRefs.push(`${file}:${line} ${m[1]}`);
    if (m[1] === 'StaticResource') {
      problems.push(`${file}:${line}: names Type.FontFamily through StaticResource, which cannot see the family App.OnStartup writes; expected ${FONT}`);
    }
  }
  const all = tags(text);
  const root = all.find((t) => !t.closing);
  if (!root) {
    problems.push(`${file}: no root element found`);
    continue;
  }

  if (root.name === 'Window') {
    const font = attr(root.raw, 'FontFamily');
    const cls = attr(root.raw, 'x:Class');
    matched.windows.push(`${file}:${root.line} ${cls ?? '(no x:Class)'} FontFamily=${font ?? '(none)'}`);
    if (font !== FONT) {
      problems.push(`${file}:${root.line}: the Window root sets ${font === null ? 'no FontFamily' : `FontFamily="${font}"`}, expected FontFamily="${FONT}"`);
    } else if (cls) {
      windowClasses.add(cls);
    }
  } else if (!NON_DRAWING_ROOTS.has(root.name)) {
    problems.push(`${file}:${root.line}: root element <${root.name}> is not one this check knows. Teach it where text under that root takes its font.`);
  }

  // Styles targeting a popup: collect each one's direct Setters by depth.
  const stack = [];
  for (const t of all) {
    if (t.closing) {
      const open = stack.pop();
      if (open?.style) popupStyles.push(open.style);
      continue;
    }
    const parent = stack[stack.length - 1];
    if (parent?.style && t.name === 'Setter' && attr(t.raw, 'Property') === 'FontFamily') {
      parent.style.setsFont = attr(t.raw, 'Value') === FONT;
      parent.style.fontValue = attr(t.raw, 'Value');
    }
    let style = null;
    if (t.name === 'Style' && POPUPS.includes(targetOf(t.raw))) {
      const based = attr(t.raw, 'BasedOn');
      style = {
        file,
        line: t.line,
        target: targetOf(t.raw),
        key: attr(t.raw, 'x:Key'),
        basedOn: based ? (styleKeyOf(based) ?? `unreadable: ${based}`) : null,
        setsFont: false,
        fontValue: null,
      };
    }
    if (POPUPS.includes(t.name)) {
      popupElements.push({ file, line: t.line, name: t.name, style: attr(t.raw, 'Style') });
    }
    if (t.selfClosing) {
      if (style) popupStyles.push(style);
    } else {
      stack.push({ name: t.name, style });
    }
  }
}

// A style sets the font through its own Setter or through the chain it is
// BasedOn. Keys are looked up among the popup styles, so a BasedOn naming any
// other style is unreadable here and fails.
const byKey = new Map(popupStyles.map((s) => [s.key ?? `{x:Type ${s.target}}`, s]));
const carriesFont = (s, seen = new Set()) => {
  if (s.setsFont) return true;
  if (!s.basedOn || seen.has(s)) return false;
  seen.add(s);
  const base = byKey.get(s.basedOn);
  return base ? carriesFont(base, seen) : false;
};

for (const s of popupStyles) {
  const ok = carriesFont(s);
  matched.popupStyles.push(`${s.file}:${s.line} ${s.target} ${s.key ? `x:Key=${s.key}` : '(implicit)'}${s.basedOn ? ` BasedOn=${s.basedOn}` : ''} font=${ok ? 'yes' : 'no'}`);
  if (!ok) {
    const why = s.fontValue !== null ? `its FontFamily Setter has Value="${s.fontValue}"` : s.basedOn ? `neither it nor ${s.basedOn} sets FontFamily` : 'it sets no FontFamily';
    problems.push(`${s.file}:${s.line}: a ${s.target} style${s.key ? ` (${s.key})` : ''} does not carry the font: ${why}, expected Value="${FONT}"`);
  }
}

for (const p of POPUPS) {
  const implicit = popupStyles.filter((s) => s.target === p && !s.key);
  if (implicit.length !== 1) {
    problems.push(`expected exactly one implicit ${p} style, found ${implicit.length}: a ${p} with no Style of its own has to take one that carries the font`);
  }
}

for (const e of popupElements) {
  matched.popupElements.push(`${e.file}:${e.line} <${e.name}>${e.style ? ` Style=${e.style}` : ''}`);
  if (e.style === null) continue;
  const key = styleKeyOf(e.style);
  const s = key ? byKey.get(key) : null;
  if (!s || s.target !== e.name || !carriesFont(s)) {
    problems.push(`${e.file}:${e.line}: <${e.name} Style="${e.style}"> names no ${e.name} style carrying the font`);
  }
}

// --- C# ---------------------------------------------------------------------

// Every class whose base chain reaches Window, from `class X : Base` across
// every file, the namespace taken from the file's own declaration.
const bases = new Map(); // full name -> { base, file, line }
const simpleToFull = new Map();
const csTexts = [];
for (const file of csFiles) {
  const text = csCode(file);
  csTexts.push([file, text]);
  const ns = text.match(/^\s*namespace\s+([\w.]+)/m)?.[1] ?? '';
  for (const m of text.matchAll(/\bclass\s+(\w+)(?:<[^>]*>)?\s*:\s*([\w.]+)/g)) {
    const full = ns ? `${ns}.${m[1]}` : m[1];
    bases.set(full, { base: m[2].replace(/^System\.Windows\./, ''), file, line: lineAt(text, m.index) });
    if (!simpleToFull.has(m[1])) simpleToFull.set(m[1], []);
    simpleToFull.get(m[1]).push(full);
  }
}

const reachesWindow = (full, seen = new Set()) => {
  const entry = bases.get(full);
  if (!entry || seen.has(full)) return false;
  seen.add(full);
  if (entry.base === 'Window') return true;
  const next = bases.has(entry.base) ? [entry.base] : simpleToFull.get(entry.base.split('.').pop()) ?? [];
  return next.some((n) => reachesWindow(n, seen));
};

for (const [full, entry] of bases) {
  if (!reachesWindow(full)) continue;
  const ok = windowClasses.has(full);
  matched.subclasses.push(`${entry.file}:${entry.line} ${full} : ${entry.base} xaml-root-with-font=${ok ? 'yes' : 'no'}`);
  if (!ok) {
    problems.push(`${entry.file}:${entry.line}: ${full} derives from Window and is not the x:Class of a XAML Window root carrying FontFamily="${FONT}"`);
  }
}

for (const [file, text] of csTexts) {
  for (const m of text.matchAll(/\bnew\s+(?:System\.Windows\.)?Window\s*[({]/g)) {
    problems.push(`${file}:${lineAt(text, m.index)}: constructs a Window in C#, which has no root tag to carry the font`);
  }

  // A Style set in C#. In an object initializer the type being built is read
  // from the nearest unclosed `new T {` before it; anywhere else the target
  // cannot be read, so it fails.
  for (const m of text.matchAll(/(?<![\w.])(\w+\.)?Style\s*=(?!=)/g)) {
    const line = lineAt(text, m.index);
    if (m[1]) {
      problems.push(`${file}:${line}: sets ${m[0].replace(/\s*=$/, '')} outside an object initializer, where this check cannot read the type being styled`);
      continue;
    }
    const before = text.slice(0, m.index);
    let depth = 0;
    let open = -1;
    for (let k = before.length - 1; k >= 0; k--) {
      if (before[k] === '}') depth++;
      else if (before[k] === '{') {
        if (depth === 0) { open = k; break; }
        depth--;
      }
    }
    // Constructor arguments may nest, as in new Hyperlink(new Run(text)), so
    // the parentheses are balanced back to their opener before the type is read.
    let head = open === -1 ? null : before.slice(0, open).trimEnd();
    if (head?.endsWith(')')) {
      let d = 0;
      let k = head.length - 1;
      for (; k >= 0; k--) {
        if (head[k] === ')') d++;
        else if (head[k] === '(' && --d === 0) break;
      }
      head = k < 0 ? null : head.slice(0, k);
    }
    const type = head?.match(/\bnew\s+([\w.]+)\s*$/)?.[1];
    matched.csStyles.push(`${file}:${line} Style= in new ${type ?? '(unreadable)'}`);
    if (!type) {
      problems.push(`${file}:${line}: sets a Style where this check cannot read the type being styled`);
    } else if (POPUPS.includes(type.split('.').pop())) {
      problems.push(`${file}:${line}: sets a Style on a ${type} in C#, which this check cannot follow to the font`);
    }
  }
}

// --- report -----------------------------------------------------------------

const print = (title, rows) => {
  console.log(`${title} (${rows.length}):`);
  for (const r of rows) console.log(`  ${r}`);
};
print('Window roots', matched.windows);
print('C# classes derived from Window', matched.subclasses);
print('Popup styles', matched.popupStyles);
print('Popup elements in XAML', matched.popupElements);
print('Styles set in C#', matched.csStyles);
print('References to Type.FontFamily in XAML', matched.tokenRefs);

// A walk that matched no window, or no popup style, has stopped reading the
// files rather than found them all correct.
if (matched.windows.length === 0 || matched.popupStyles.length === 0 || matched.tokenRefs.length === 0) {
  problems.push('the walk matched no Window root, no popup style or no reference to Type.FontFamily, so it is not reading the files it is meant to');
}

if (problems.length) {
  console.error('\ncheck-xaml-font: text can be drawn under a root without the app font.\n');
  for (const p of problems) console.error(`  ${p}`);
  console.error(`\nFix: set FontFamily="${FONT}" on the window's root tag, or a FontFamily`);
  console.error('Setter with that value in the popup style. Without it the text under that');
  console.error('root draws in the system font wherever a TextBlock carries a Style of its');
  console.error('own and wherever a TextBox draws its text.');
  process.exit(1);
}

console.log('\ncheck-xaml-font: OK');
