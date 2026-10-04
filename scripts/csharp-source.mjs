// csharp-source.mjs: reads a C# file into its code, its comments and its preprocessor
// lines, and finds the brackets of a call in it, for the checks in this folder that
// search C# source.
//
// readCSharp(source) returns four things:
//   code        the source with every comment character replaced by a space. Strings,
//               character literals and everything else stay as written.
//   bare        code with the text inside every string and character literal replaced
//               by spaces as well, the quotes kept. An interpolation hole is code and
//               stays; its format text is string text and goes. A bracket in bare is a
//               bracket of the code.
//   comments    the comment characters, everything else a space.
//   directives  a Map from line number to the text of each preprocessor line, its
//               comment cut off.
// The three strings keep every newline where it was, so a match in any of them has the
// line number it has in the file. The text of a preprocessor line is in none of them.
//
// Every string form is walked, so a // or a /* inside one is text: regular and verbatim
// strings, interpolated strings with their holes (a string or a character literal
// inside a hole included), raw strings and raw interpolated strings with any number of
// $ signs, and character literals. Inside a hole, a ':' outside any bracket starts the
// format text, which runs to the closing brace, as the compiler reads it.
//
// A '#' is a preprocessor line only where it is the first thing on a line of top-level
// code, so a #if inside a multi-line string or comment is text. The branches of an #if
// are all read as code, whichever of them a build compiles.
//
// A file that cannot be followed to its end throws, naming the line: an unclosed
// string, character literal, block comment or interpolation hole. A check that reads a
// file this throws on refuses its run.
//
// argumentSpan(bare, open) takes the offset of a call's '(' and returns [from, to], from
// the first character inside the brackets to the ')' that closes them, or null where the
// text ends first. It counts every '(' and ')' it meets, so it is given bare, where a
// bracket inside a string or a comment is already a space; given code, a bracket inside
// a string would close the call early or keep it open.

const isSpace = (c) => c === ' ' || c === '\t' || c === '\r' || c === '\f' || c === '\v' || c === '\uFEFF';

// Where the comment on a preprocessor line starts, or -1. After #region, #endregion,
// #error and #warning the rest of the line is a message, and a // is a comment only
// where nothing comes before it. On any other line a // starts a comment unless it is
// inside quotes, as in the file name a #line or #pragma checksum carries.
const directiveCommentAt = (text) => {
  const [head, keyword] = /^#\s*(\w*)\s*/.exec(text);
  if (['region', 'endregion', 'error', 'warning'].includes(keyword))
    return text.startsWith('//', head.length) ? head.length : -1;
  let quoted = false;
  for (let k = head.length; k < text.length; k++) {
    if (text[k] === '"') quoted = !quoted;
    else if (!quoted && text[k] === '/' && text[k + 1] === '/') return k;
  }
  return -1;
};

export function readCSharp(source) {
  const length = source.length;
  const code = source.split('');
  const bare = source.split('');
  const comments = source.replace(/[^\n]/g, ' ').split('');
  const directives = new Map();

  const lineAt = (at) => source.slice(0, at).split('\n').length;
  const refuse = (at, what) => { throw new Error(`line ${lineAt(at)}: ${what}`); };
  const toComment = (from, to) => {
    for (let k = from; k < to; k++) {
      if (source[k] === '\n') continue;
      comments[k] = source[k];
      code[k] = ' ';
      bare[k] = ' ';
    }
  };
  const blank = (from, to) => {
    for (let k = from; k < to; k++) if (source[k] !== '\n') code[k] = bare[k] = ' ';
  };
  const literal = (from, to) => {
    for (let k = from; k < to; k++) if (source[k] !== '\n') bare[k] = ' ';
  };

  // One frame per nesting level. A code frame is the file itself or an interpolation
  // hole; a string frame is a string literal that holes can open inside.
  //   code:   { kind: 'code', hole, braces, brackets }
  //   string: { kind: 'string', verbatim, quotes, dollars }
  // quotes is the length of a raw string's delimiter, 0 for every other string;
  // dollars is the number of $ signs, 0 for a string with no holes.
  const stack = [{ kind: 'code', hole: false, braces: 0, brackets: 0 }];
  let i = 0;
  let lineStart = true;

  while (i < length) {
    const top = stack[stack.length - 1];
    const c = source[i];

    if (top.kind === 'code') {
      if (stack.length === 1 && lineStart) {
        if (isSpace(c)) { i++; continue; }
        lineStart = false;
        if (c === '#') {
          let end = source.indexOf('\n', i);
          if (end === -1) end = length;
          let text = source.slice(i, end);
          const cut = directiveCommentAt(text);
          if (cut !== -1) {
            toComment(i + cut, end);
            text = text.slice(0, cut);
          }
          directives.set(lineAt(i), text.trim());
          blank(i, end);
          i = end;
          continue;
        }
      }
      if (c === '\n') {
        if (stack.length === 1) lineStart = true;
        i++;
        continue;
      }
      if (c === '/' && source[i + 1] === '/') {
        let end = source.indexOf('\n', i);
        if (end === -1) end = length;
        toComment(i, end);
        i = end;
        continue;
      }
      if (c === '/' && source[i + 1] === '*') {
        const close = source.indexOf('*/', i + 2);
        if (close === -1) refuse(i, 'a block comment does not close');
        toComment(i, close + 2);
        i = close + 2;
        continue;
      }
      if (c === "'") {
        let j = source[i + 1] === '\\' ? i + 3 : i + 2;
        while (j < length && source[j] !== "'" && source[j] !== '\n') j++;
        if (source[j] !== "'") refuse(i, 'a character literal does not close');
        literal(i + 1, j);
        i = j + 1;
        continue;
      }
      if (c === '"' || c === '$' || c === '@') {
        let k = i;
        let dollars = 0;
        let verbatim = false;
        while (source[k] === '$' || source[k] === '@') {
          if (source[k] === '$') dollars++;
          else verbatim = true;
          k++;
        }
        if (source[k] !== '"') {
          // An @ before an identifier, which names a keyword as an identifier.
          i = k;
          continue;
        }
        let quotes = 0;
        while (source[k + quotes] === '"') quotes++;
        if (!verbatim && quotes >= 3) {
          stack.push({ kind: 'string', verbatim: false, quotes, dollars });
          i = k + quotes;
        } else if (!verbatim && quotes === 2) {
          i = k + 2; // An empty string.
        } else {
          stack.push({ kind: 'string', verbatim, quotes: 0, dollars });
          i = k + 1;
        }
        continue;
      }
      if (top.hole) {
        if (c === '(' || c === '[') top.brackets++;
        else if (c === ')' || c === ']') top.brackets--;
        else if (c === '{') top.braces++;
        else if (c === '}') {
          if (top.braces === 0) {
            stack.pop();
            i++;
            continue;
          }
          top.braces--;
        } else if (c === ':' && top.braces === 0 && top.brackets === 0
          && source[i + 1] !== ':' && source[i - 1] !== ':') {
          const close = source.indexOf('}', i);
          if (close === -1) refuse(i, 'an interpolation hole does not close');
          literal(i + 1, close);
          stack.pop();
          i = close + 1;
          continue;
        }
      }
      i++;
      continue;
    }

    // Inside a raw string: no escapes, and only a run of at least its own number of
    // quotes ends it.
    if (top.quotes) {
      if (c === '"') {
        let run = 0;
        while (source[i + run] === '"') run++;
        if (run >= top.quotes) stack.pop();
        else literal(i, i + run);
        i += run;
        continue;
      }
      if (top.dollars && c === '{') {
        let run = 0;
        while (source[i + run] === '{') run++;
        if (run >= top.dollars) stack.push({ kind: 'code', hole: true, braces: 0, brackets: 0 });
        else literal(i, i + run);
        i += run;
        continue;
      }
      literal(i, i + 1);
      i++;
      continue;
    }

    // Inside a regular or verbatim string.
    if (!top.verbatim && c === '\\') { literal(i, i + 2); i += 2; continue; }
    if (!top.verbatim && c === '\n') refuse(i, 'a string runs to the end of its line');
    if (c === '"') {
      if (top.verbatim && source[i + 1] === '"') { literal(i, i + 2); i += 2; continue; }
      stack.pop();
      i++;
      continue;
    }
    if (top.dollars && c === '{') {
      if (source[i + 1] === '{') { literal(i, i + 2); i += 2; continue; }
      stack.push({ kind: 'code', hole: true, braces: 0, brackets: 0 });
      i++;
      continue;
    }
    if (top.dollars && c === '}' && source[i + 1] === '}') { literal(i, i + 2); i += 2; continue; }
    literal(i, i + 1);
    i++;
  }

  if (stack.length !== 1) {
    const open = stack[stack.length - 1];
    refuse(length, `the file ends inside ${open.kind === 'code' ? 'an interpolation hole' : 'a string'}`);
  }
  return { code: code.join(''), bare: bare.join(''), comments: comments.join(''), directives };
}

export function argumentSpan(bare, open) {
  let depth = 0;
  for (let i = open; i < bare.length; i++) {
    if (bare[i] === '(') depth++;
    else if (bare[i] === ')' && --depth === 0) return [open + 1, i];
  }
  return null;
}
