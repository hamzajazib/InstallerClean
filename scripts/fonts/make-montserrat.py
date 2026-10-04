#!/usr/bin/env python3
"""Makes the four Montserrat files in src/InstallerClean/Fonts from Montserrat's own release.

The app draws Russian, Ukrainian and Vietnamese in Montserrat, and names it second after Poppins for
the other Latin-script languages. Its files come from github.com/JulietaUla/Montserrat at commit
555facfb2a18c72c3c0380f0d9c0f060453a9058, fonts/ttf/, which are static and hinted. WPF cannot select
a weight from a variable font, so the four weights the app uses are four files.

Two changes are made to each file, and nothing else:

- The vertical metrics become Poppins's: ascender 1050, descender -350 and line gap 100 on a
  1000-unit em, in the OS/2 typo fields and in hhea. WPF sets a line at (ascender + descender +
  line gap) / em and the baseline at (ascender + line gap / 2) / em, reading the typo values
  because USE_TYPO_METRICS is set, which it is in all four files and stays set. That gives 1.5
  and 1.1, Poppins's own, so a line in Montserrat is as tall as one in Poppins and its text sits
  at the same depth. As published, Montserrat sets 1.219 and 0.968. The win values are left as
  published.
- The Greek block, U+0370 to U+03FF, leaves every character map. Montserrat draws eight Greek
  letters and Poppins one, so with Montserrat named after Poppins a Greek word would take letters
  from three fonts. Without them Greek falls to Segoe UI whole, apart from Poppins's pi. The OS/2
  bit declaring the Greek block is cleared with them.

The outlines, the names and the hinting are untouched. The licence is the SIL Open Font License
1.1 and Montserrat has no Reserved Font Name, so the modified files keep the name.

Output is byte for byte the same on every run with the same fontTools: head.modified is kept as
published, and nothing else in the file depends on the time or the machine. These four outputs
were made with fontTools 4.63.0.

scripts/check-font-coverage.mjs asserts the result in CI: the 1.5 and 1.1, USE_TYPO_METRICS and
the absence of Greek.

Needs fontTools (pip install fonttools). Run from the repository root, with the four upstream
files in one folder:

    python3 scripts/fonts/make-montserrat.py <folder>
"""
import hashlib
import pathlib
import sys

from fontTools.ttLib import TTFont

UPSTREAM = {
    'Montserrat-Regular.ttf': '3e8abe50c44c82e2242e97d1ec8c0d385c4890cdc50447bcdb8605c81a38cfb2',
    'Montserrat-Medium.ttf': 'dae47428bb041f9716604e0e07b5b0c8585b3bdd8183362f75c69fe7bb3cfaf4',
    'Montserrat-SemiBold.ttf': 'b4e1563393d73fdff491a869441245aef31add2ec03d9c97a6dae4de07c52fd0',
    'Montserrat-Bold.ttf': 'bc6e854971cea46b463be6f9eef4d9cd52f51cfc1fc0dd90c9d3e6483dc0ec61',
}
OUT = pathlib.Path('src/InstallerClean/Fonts')
ASCENDER, DESCENDER, LINE_GAP = 1050, -350, 100
GREEK = range(0x0370, 0x0400)
# OS/2 Unicode range bit 7 is Greek and Coptic, the block the character maps lose.
GREEK_RANGE_BIT = 1 << 7
USE_TYPO_METRICS = 1 << 7


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__.rstrip().rsplit('\n', 1)[-1].strip())
    source = pathlib.Path(sys.argv[1])
    if not OUT.is_dir():
        sys.exit(f'{OUT} not found: run from the repository root')

    for name, expected in UPSTREAM.items():
        path = source / name
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        if digest != expected:
            sys.exit(f'{path}: sha256 {digest}, expected {expected} from commit 555facfb')

        # recalcTimestamp=False keeps head.modified as published, which is what makes a re-run
        # give the same bytes.
        font = TTFont(str(path), recalcTimestamp=False)
        if font['head'].unitsPerEm != 1000:
            sys.exit(f'{name}: {font["head"].unitsPerEm} units per em; the metrics here assume 1000')
        if 'MVAR' in font:
            sys.exit(f'{name}: has an MVAR table, which could vary the metrics; a static file has none')

        os2, hhea = font['OS/2'], font['hhea']
        if not os2.fsSelection & USE_TYPO_METRICS:
            sys.exit(f'{name}: USE_TYPO_METRICS is not set, so WPF would not read the typo values')
        os2.sTypoAscender, os2.sTypoDescender, os2.sTypoLineGap = ASCENDER, DESCENDER, LINE_GAP
        hhea.ascent, hhea.descent, hhea.lineGap = ASCENDER, DESCENDER, LINE_GAP

        dropped = set()
        for table in font['cmap'].tables:
            for code in [c for c in table.cmap if c in GREEK]:
                del table.cmap[code]
                dropped.add(code)
        os2.ulUnicodeRange1 &= ~GREEK_RANGE_BIT

        target = OUT / name
        font.save(str(target))
        size = hashlib.sha256(target.read_bytes()).hexdigest()
        letters = ''.join(chr(c) for c in sorted(dropped))
        print(f'{target}: line {(ASCENDER - DESCENDER + LINE_GAP) / 1000}, '
              f'baseline {(ASCENDER + LINE_GAP / 2) / 1000}, Greek dropped {letters}, sha256 {size}')


if __name__ == '__main__':
    main()
