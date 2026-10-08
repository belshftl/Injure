# SPDX-FileCopyrightText: 2026 belshftl
# SPDX-License-Identifier: MIT

RID      ?= $(error RID must be set)

ROOT     := .
SRCDIR   := $(ROOT)/src
OBJDIR   := $(ROOT)/obj/$(RID)
OUTDIR   := $(ROOT)/out/$(RID)

UNIBREAK     := $(ROOT)/extern/libunibreak
UNIBREAK_SRC := \
	$(UNIBREAK)/src/unibreakbase.c \
	$(UNIBREAK)/src/unibreakdef.c \
	$(UNIBREAK)/src/linebreak.c \
	$(UNIBREAK)/src/linebreakdata.c \
	$(UNIBREAK)/src/linebreakdef.c \
	$(UNIBREAK)/src/eastasianwidthdef.c
UNIBREAK_OBJ := $(patsubst $(UNIBREAK)/src/%.c,$(OBJDIR)/libunibreak/%.o,$(UNIBREAK_SRC))
UNIBREAK_DEP := $(UNIBREAK_OBJ:.o=.d)

# built as its own shared library rather than linked into libijmisc, since fribidi is LGPL
FRIBIDI     := $(ROOT)/extern/fribidi
FRIBIDI_SRC := \
	$(FRIBIDI)/lib/fribidi.c \
	$(FRIBIDI)/lib/fribidi-arabic.c \
	$(FRIBIDI)/lib/fribidi-bidi.c \
	$(FRIBIDI)/lib/fribidi-bidi-types.c \
	$(FRIBIDI)/lib/fribidi-char-sets.c \
	$(FRIBIDI)/lib/fribidi-char-sets-cap-rtl.c \
	$(FRIBIDI)/lib/fribidi-char-sets-cp1255.c \
	$(FRIBIDI)/lib/fribidi-char-sets-cp1256.c \
	$(FRIBIDI)/lib/fribidi-char-sets-iso8859-6.c \
	$(FRIBIDI)/lib/fribidi-char-sets-iso8859-8.c \
	$(FRIBIDI)/lib/fribidi-char-sets-utf8.c \
	$(FRIBIDI)/lib/fribidi-deprecated.c \
	$(FRIBIDI)/lib/fribidi-joining.c \
	$(FRIBIDI)/lib/fribidi-joining-types.c \
	$(FRIBIDI)/lib/fribidi-mirroring.c \
	$(FRIBIDI)/lib/fribidi-brackets.c \
	$(FRIBIDI)/lib/fribidi-run.c \
	$(FRIBIDI)/lib/fribidi-shape.c
FRIBIDI_OBJ := $(patsubst $(FRIBIDI)/lib/%.c,$(OBJDIR)/fribidi/%.o,$(FRIBIDI_SRC))
FRIBIDI_DEP := $(FRIBIDI_OBJ:.o=.d)
# the git tree doesn't have the Unicode tables, so build and run fribidi's generators like its meson
# build does; they run on the build machine, hence HOSTCC
HOSTCC              ?= cc
FRIBIDI_GENDIR      := $(OBJDIR)/fribidi-gen
FRIBIDI_UNIDATA     := $(FRIBIDI)/gen.tab/unidata
FRIBIDI_GEN_CPPFLAGS := -DHAVE_STRINGIZE -DDONT_HAVE_FRIBIDI_CONFIG_H -DHAVE_STDLIB_H \
	-DHAVE_STRING_H -DHAVE_STRINGS_H -DSTDC_HEADERS=1 \
	-I$(FRIBIDI_GENDIR) -I$(FRIBIDI)/lib -I$(FRIBIDI)/gen.tab
FRIBIDI_VERSION_H   := $(FRIBIDI_GENDIR)/fribidi-unicode-version.h
FRIBIDI_TABS        := bidi-type joining-type arabic-shaping mirroring brackets brackets-type
FRIBIDI_TAB_I       := $(patsubst %,$(FRIBIDI_GENDIR)/%.tab.i,$(FRIBIDI_TABS))
FRIBIDI_TAB_INPUTS_bidi-type      := UnicodeData.txt
FRIBIDI_TAB_INPUTS_joining-type   := UnicodeData.txt ArabicShaping.txt
FRIBIDI_TAB_INPUTS_arabic-shaping := UnicodeData.txt
FRIBIDI_TAB_INPUTS_mirroring      := BidiMirroring.txt
FRIBIDI_TAB_INPUTS_brackets       := BidiBrackets.txt UnicodeData.txt
FRIBIDI_TAB_INPUTS_brackets-type  := BidiBrackets.txt
FRIBIDI_TAB_COMPRESSION := 2
FRIBIDI_GEN_TOOLS   := $(patsubst %,$(FRIBIDI_GENDIR)/gen-%-tab,$(FRIBIDI_TABS))

# config.h and fribidi-config.h normally come from fribidi's meson build
FRIBIDI_CPPFLAGS := -DHAVE_CONFIG_H -I$(FRIBIDI_GENDIR) -I$(ROOT)/fribidi-config -I$(FRIBIDI)/lib
FRIBIDI_CFLAGS   := -fvisibility=hidden

SRC       := $(wildcard $(SRCDIR)/*.c)
OBJ       := $(patsubst $(SRCDIR)/%.c,$(OBJDIR)/%.o,$(SRC))
DEP       := $(OBJ:.o=.d)

CC        ?= cc
CPPFLAGS  ?=
CFLAGS    ?= -std=c11 -O2
LDFLAGS   ?=
LDLIBS    ?=

ifneq ($(filter osx-x64 osx-arm64,$(RID)),)
	OUT := $(OUTDIR)/libijmisc.dylib
	FRIBIDI_OUT := $(OUTDIR)/libfribidi.dylib
	CPPFLAGS += -DIJMISC_MACOS
	CFLAGS += -fPIC
	LDFLAGS += -dynamiclib
else ifneq ($(filter linux-x64 linux-arm64,$(RID)),)
	OUT := $(OUTDIR)/libijmisc.so
	FRIBIDI_OUT := $(OUTDIR)/libfribidi.so
	CPPFLAGS += -DIJMISC_POSIX -D_POSIX_C_SOURCE=200112L
	CFLAGS += -fPIC
	LDFLAGS += -shared
else
	$(error unsupported RID $(RID))
endif

.PHONY: all clean
all: $(OUT) $(FRIBIDI_OUT)

$(OUT): $(UNIBREAK_OBJ) $(OBJ)
	mkdir -p $(dir $@)
	$(CC) $(UNIBREAK_OBJ) $(OBJ) $(LDFLAGS) -o $@ $(LDLIBS)

$(FRIBIDI_OUT): $(FRIBIDI_OBJ)
	mkdir -p $(dir $@)
	$(CC) $(FRIBIDI_OBJ) $(LDFLAGS) -o $@ $(LDLIBS)

$(OBJDIR)/libunibreak/%.o: $(UNIBREAK)/src/%.c
	mkdir -p $(dir $@)
	$(CC) $(CPPFLAGS) $(CFLAGS) -MMD -MP -MF $(@:.o=.d) -MT $@ -c -o $@ $<

$(FRIBIDI_GENDIR)/gen-unicode-version: $(FRIBIDI)/gen.tab/gen-unicode-version.c
	mkdir -p $(dir $@)
	$(HOSTCC) $(FRIBIDI_GEN_CPPFLAGS) -o $@ $<

$(FRIBIDI_VERSION_H): $(FRIBIDI_GENDIR)/gen-unicode-version
	$< $(FRIBIDI_UNIDATA)/ReadMe.txt $(FRIBIDI_UNIDATA)/BidiMirroring.txt gen-unicode-version > $@.tmp
	mv $@.tmp $@

$(FRIBIDI_GENDIR)/gen-%-tab: $(FRIBIDI)/gen.tab/gen-%-tab.c $(FRIBIDI)/gen.tab/packtab.c $(FRIBIDI_VERSION_H)
	$(HOSTCC) $(FRIBIDI_GEN_CPPFLAGS) -o $@ $(FRIBIDI)/gen.tab/gen-$*-tab.c $(FRIBIDI)/gen.tab/packtab.c

.SECONDARY: $(FRIBIDI_TAB_I) $(FRIBIDI_GEN_TOOLS)
.SECONDEXPANSION:
$(FRIBIDI_GENDIR)/%.tab.i: $(FRIBIDI_GENDIR)/gen-%-tab $$(addprefix $(FRIBIDI_UNIDATA)/,$$(FRIBIDI_TAB_INPUTS_$$*))
	$< $(FRIBIDI_TAB_COMPRESSION) $(addprefix $(FRIBIDI_UNIDATA)/,$(FRIBIDI_TAB_INPUTS_$*)) gen-$*-tab > $@.tmp
	mv $@.tmp $@

$(OBJDIR)/fribidi/%.o: $(FRIBIDI)/lib/%.c | $(FRIBIDI_VERSION_H) $(FRIBIDI_TAB_I)
	mkdir -p $(dir $@)
	$(CC) $(CPPFLAGS) $(FRIBIDI_CPPFLAGS) $(CFLAGS) $(FRIBIDI_CFLAGS) -MMD -MP -MF $(@:.o=.d) -MT $@ -c -o $@ $<

$(OBJDIR)/%.o: $(SRCDIR)/%.c
	mkdir -p $(dir $@)
	$(CC) $(CPPFLAGS) $(CFLAGS) -MMD -MP -MF $(@:.o=.d) -MT $@ -c -o $@ $<

clean:
	rm -rf $(OBJDIR) $(OUTDIR)

-include $(UNIBREAK_DEP) $(FRIBIDI_DEP) $(DEP)
