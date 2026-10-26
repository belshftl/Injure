#!/usr/bin/env python3
# SPDX-FileCopyrightText: 2026 belshftl
# SPDX-License-Identifier: MIT

# generates the test fonts; every glyph is a box with a multiple-of-10 advance so measurements
# at 10px are whole pixels
#
# needs fontTools; the output is deterministic so regenerating is only necessary if this script
# changes

import os
from fontTools.feaLib.builder import addOpenTypeFeaturesFromString
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen

UPM = 1000
ASCENT = 800
DESCENT = -200
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

def build(family, chars, extra=None, fea=None, line_gap=0):
	# chars maps characters to advances; .notdef is a 500-unit box, space is 300 units
    # extra maps names of unmapped glyphs (for fea's substitutions) to advances
	extra = extra or {}
	names = ['.notdef', 'space'] + [f'uni{ord(c):04X}' for c in chars] + list(extra)
	advances = {'.notdef': 500, 'space': 300}
	glyphs = {'.notdef': box(500), 'space': empty()}
	cmap = {0x20: 'space'}
	for c, adv in chars.items():
		name = f'uni{ord(c):04X}'
		advances[name] = adv
		glyphs[name] = box(adv)
		cmap[ord(c)] = name
	for name, adv in extra.items():
		advances[name] = adv
		glyphs[name] = box(adv)

	fb = FontBuilder(UPM, isTTF=True)
	fb.setupGlyphOrder(names)
	fb.setupCharacterMap(cmap)
	fb.setupGlyf(glyphs)
	metrics = {}
	for name in names:
		g = glyphs[name]
		metrics[name] = (advances[name], getattr(g, 'xMin', 0))
	fb.setupHorizontalMetrics(metrics)
	fb.setupHorizontalHeader(ascent=ASCENT, descent=DESCENT, lineGap=line_gap)
	fb.setupNameTable({'familyName': family, 'styleName': 'Regular'})
	fb.setupOS2(sTypoAscender=ASCENT, sTypoDescender=DESCENT, sTypoLineGap=line_gap, usWinAscent=ASCENT, usWinDescent=-DESCENT)
	fb.setupPost()
	fb.updateHead(created=TIMESTAMP, modified=TIMESTAMP)
	if fea is not None:
		addOpenTypeFeaturesFromString(fb.font, fea)
	return fb.font

# 'b' before 'c' and 'c' after 'b' become 300-unit alternates, so breaking between them changes
# the widths on both sides
CONTEXTUAL_FEA = '''
languagesystem DFLT dflt;
languagesystem latn dflt;
lookup CtxC {
	sub uni0062 uni0063' by c.ctx;
} CtxC;
lookup CtxB {
	sub uni0062' [uni0063 c.ctx] by b.ctx;
} CtxB;
feature calt {
	lookup CtxC;
	lookup CtxB;
} calt;
'''

def main():
	here = os.path.dirname(os.path.abspath(__file__))
	letters = {c: 500 for c in 'abcdefghijklmnopqrstuvwxyz'}
	letters['m'] = 1000
	fonts = {
		'InjureTestSans.ttf': build('Injure Test Sans', letters),
		'InjureTestSansWide.ttf': build('Injure Test Sans Wide', {c: 700 for c in 'abcdefghijklmnopqrstuvwxyz'}),
		'InjureTestDigits.ttf': build('Injure Test Digits', {c: 600 for c in '0123456789'}),
		'InjureTestSansGap.ttf': build('Injure Test Sans Gap', letters, line_gap=200),
		'InjureTestEllipsis.ttf': build('Injure Test Ellipsis', letters | {'\u2026': 800}),
		'InjureTestDots.ttf': build('Injure Test Dots', letters | {'.': 200, '-': 300}),
		'InjureTestHyphen.ttf': build('Injure Test Hyphen', letters | {'\u2010': 400, '-': 300, '\u2026': 800}),
		'InjureTestContextual.ttf': build(
			'Injure Test Contextual',
			{c: 500 for c in 'abcdefghijklmnopqrstuvwxyz'},
			extra={'b.ctx': 300, 'c.ctx': 300},
			fea=CONTEXTUAL_FEA,
		),
	}
	for name, font in fonts.items():
		font.save(os.path.join(here, name))
		print('wrote', name)

if __name__ == '__main__':
	main()
