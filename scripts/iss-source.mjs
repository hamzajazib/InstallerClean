// iss-source.mjs: reads an Inno Setup script into its text with every comment blanked,
// for the checks in this folder that search the setup.
//
// readIss(text) returns text with each comment character replaced by a space and every
// newline kept where it was, so a match in it has the line number it has in the file.
// Outside the [Code] section a comment is a line whose first character, after spaces, is
// ';'. The [Code] section runs from its heading, matched in any case as Inno Setup
// matches it, to the next section's. There the script is Pascal Script, whose comments
// are '//' to the end of the line, '{ ... }' and '(* ... *)', and where a quoted string
// is text, so a brace or a '//' inside one, as in '{#AppMutexName}', stays. A line there
// whose first character, after spaces, is '#' is the preprocessor's and is kept as it
// is, its quotes included. A block comment that is never closed runs to the end of the
// section.
//
// The text outside [Code] keeps its braces: there '{app}' and '{#AppVersion}' are
// constants and preprocessor values, not comments.

/** Blanks the comments of a [Code] section, keeping its strings and newlines. */
function blankPascal(code) {
  let out = '';
  let i = 0;
  while (i < code.length) {
    const c = code[i];
    if (c === '#' && code.slice(code.lastIndexOf('\n', i - 1) + 1, i).trim() === '') {
      const end = code.indexOf('\n', i);
      const stop = end === -1 ? code.length : end;
      out += code.slice(i, stop);
      i = stop;
      continue;
    }
    let close = null;
    let open = 1;
    if (c === "'") close = "'";
    else if (code.startsWith('//', i)) [close, open] = ['\n', 2];
    else if (c === '{') close = '}';
    else if (code.startsWith('(*', i)) [close, open] = ['*)', 2];
    if (close === null) {
      out += c;
      i += 1;
      continue;
    }
    let end = code.indexOf(close, i + open);
    if (end === -1) end = code.length;
    else if (close !== '\n') end += close.length;
    const piece = code.slice(i, end);
    out += c === "'" ? piece : piece.replace(/[^\r\n]/g, ' ');
    i = end;
  }
  return out;
}

/** Blanks the ';' comment lines of the sections that are not [Code]. */
function blankSemicolonLines(text) {
  return text
    .split('\n')
    .map((line) => (line.trimStart().startsWith(';') ? line.replace(/[^\r]/g, ' ') : line))
    .join('\n');
}

export function readIss(text) {
  // Each section runs from its heading to the next one.
  const headings = [...text.matchAll(/^[ \t]*\[([A-Za-z]+)\][ \t]*\r?$/gm)];
  let out = '';
  let from = 0;
  for (let k = 0; k <= headings.length; k++) {
    const to = k < headings.length ? headings[k].index : text.length;
    const inCode = k > 0 && headings[k - 1][1].toLowerCase() === 'code';
    const part = text.slice(from, to);
    out += inCode ? blankPascal(part) : blankSemicolonLines(part);
    from = to;
  }
  return out;
}
