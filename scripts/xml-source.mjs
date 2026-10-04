// xml-source.mjs: takes the comments out of an XML file, for the checks in this folder
// that read XAML, resx or manifest files.
//
// blankXmlComments(text) returns text with every character of every comment, from its
// '<!--' to its '-->', replaced by a space. Newlines stay where they are, so a match in
// the result has the line number it has in the file. A comment closes at the first
// '-->' after its opening '<!--', so '<!-->' opens a comment rather than being a whole
// one.
//
// A comment that does not close throws, naming the line it opens on. Without its '-->'
// the rest of the file would be read as markup, so a check that reads a file this throws
// on refuses its run.
import { lineIndex } from './source-files.mjs';

export function blankXmlComments(text) {
  let out = '';
  let from = 0;
  for (let open = text.indexOf('<!--'); open !== -1; open = text.indexOf('<!--', from)) {
    const close = text.indexOf('-->', open + 4);
    if (close === -1) throw new Error(`line ${lineIndex(text)(open)}: a comment does not close`);
    out += text.slice(from, open) + text.slice(open, close + 3).replace(/[^\n]/g, ' ');
    from = close + 3;
  }
  return out + text.slice(from);
}
