#!/usr/bin/env python3
# SPDX-FileCopyrightText: 2026 belshftl
# SPDX-License-Identifier: MIT

# generates the test fonts; every glyph is a box with a multiple-of-10 advance so measurements
# at 10px are whole pixels
#
# needs fontTools; the output is deterministic so regenerating is only necessary if this script
# changes

import os
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen

UPM = 1000
ASCENT = 800
DESCENT = -200
# a fixed timestamp keeps the output identical across runs
TIMESTAMP = 0x80000000

def box(advance):
	# clockwise, i.e. an outer contour in TrueType's orientation
	pen = TTGlyphPen(None)
	pen.moveTo((50, 0))
	pen.lineTo((50, 700))
	pen.lineTo((advance - 50, 700))
	pen.lineTo((advance - 50, 0))
	pen.closePath()
	return pen.glyph()

def empty():
	return TTGlyphPen(None).glyph()

def build(family, chars):
	# chars maps characters to advances; .notdef is a 500-unit box, space is 300 units
	names = ['.notdef', 'space'] + [f'uni{ord(c):04X}' for c in chars]
	advances = {'.notdef': 500, 'space': 300}
	glyphs = {'.notdef': box(500), 'space': empty()}
	cmap = {0x20: 'space'}
	for c, adv in chars.items():
		name = f'uni{ord(c):04X}'
		advances[name] = adv
		glyphs[name] = box(adv)
		cmap[ord(c)] = name

	fb = FontBuilder(UPM, isTTF=True)
	fb.setupGlyphOrder(names)
	fb.setupCharacterMap(cmap)
	fb.setupGlyf(glyphs)
	metrics = {}
	for name in names:
		g = glyphs[name]
		metrics[name] = (advances[name], getattr(g, 'xMin', 0))
	fb.setupHorizontalMetrics(metrics)
	fb.setupHorizontalHeader(ascent=ASCENT, descent=DESCENT)
	fb.setupNameTable({'familyName': family, 'styleName': 'Regular'})
	fb.setupOS2(sTypoAscender=ASCENT, sTypoDescender=DESCENT, sTypoLineGap=0, usWinAscent=ASCENT, usWinDescent=-DESCENT)
	fb.setupPost()
	fb.updateHead(created=TIMESTAMP, modified=TIMESTAMP)
	return fb.font

def main():
	here = os.path.dirname(os.path.abspath(__file__))
	letters = {c: 500 for c in 'abcdefghijklmnopqrstuvwxyz'}
	letters['m'] = 1000
	fonts = {
		'InjureTestSans.ttf': build('Injure Test Sans', letters),
		'InjureTestSansWide.ttf': build('Injure Test Sans Wide', {c: 700 for c in 'abcdefghijklmnopqrstuvwxyz'}),
		'InjureTestDigits.ttf': build('Injure Test Digits', {c: 600 for c in '0123456789'}),
	}
	for name, font in fonts.items():
		font.save(os.path.join(here, name))
		print('wrote', name)

if __name__ == '__main__':
	main()
