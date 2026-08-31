# SPDX-FileCopyrightText: 2026 belshftl
# SPDX-License-Identifier: MIT

MAKEOVERRIDES =

RID      ?= $(error RID must be set)
MODE     ?= debug

ROOT       := .
AUDIODIR   := $(ROOT)/audio
CLRPROFDIR := $(ROOT)/clrprof
MISCDIR    := $(ROOT)/misc

ifeq ($(MODE),debug)
	CARGOFLAGS ?=
else ifeq ($(MODE),release)
	CARGOFLAGS ?= --release
else
	$(error unsupported build mode $(MODE), supported: debug, release)
endif

ifneq ($(filter osx-x64 osx-arm64,$(RID)),)
	AUDIOOUTNAME   := libinjureaudio.dylib
	CLRPROFOUTNAME := libinjureclrprof.dylib
	MISCOUTNAME    := libinjuremisc.dylib
else ifneq ($(filter linux-x64 linux-arm64,$(RID)),)
	AUDIOOUTNAME   := libinjureaudio.so
	CLRPROFOUTNAME := libinjureclrprof.so
	MISCOUTNAME    := libinjuremisc.so
else
	$(error unsupported RID $(RID))
endif

OUTDIR     ?= $(ROOT)/pack/$(RID)

AUDIOOUT   ?= $(OUTDIR)/$(AUDIOOUTNAME)
CLRPROFOUT ?= $(OUTDIR)/$(CLRPROFOUTNAME)
MISCOUT    ?= $(OUTDIR)/$(MISCOUTNAME)

AUDIOBUILD   := $(AUDIODIR)/target/$(MODE)/$(AUDIOOUTNAME)
CLRPROFBUILD := $(CLRPROFDIR)/target/$(MODE)/$(CLRPROFOUTNAME)
MISCBUILD    := $(MISCDIR)/out/$(RID)/$(MISCOUTNAME)

AUDIOSRC   := $(shell find $(AUDIODIR)/src -name '*.rs') $(AUDIODIR)/build.rs
CLRPROFSRC := $(shell find $(CLRPROFDIR)/src -name '*.rs')
MISCSRC    := $(MISCDIR)/src/precisewait.c

.PHONY: all pack clean
all: $(AUDIOOUT) $(CLRPROFOUT) $(MISCOUT)

$(AUDIOBUILD): $(AUDIOSRC) $(AUDIODIR)/Cargo.toml
	cargo build $(CARGOFLAGS) --manifest-path $(AUDIODIR)/Cargo.toml

$(CLRPROFBUILD): $(CLRPROFSRC) $(CLRPROFDIR)/Cargo.toml
	cargo build $(CARGOFLAGS) --manifest-path $(CLRPROFDIR)/Cargo.toml

$(MISCBUILD): $(MISCSRC) $(MISCDIR)/GNUmakefile
	make -C $(MISCDIR) RID=$(RID)

$(AUDIOOUT): $(AUDIOBUILD)
	mkdir -p $(OUTDIR)
	cp $(AUDIOBUILD) $(AUDIOOUT)

$(CLRPROFOUT): $(CLRPROFBUILD)
	mkdir -p $(OUTDIR)
	cp $(CLRPROFBUILD) $(CLRPROFOUT)

$(MISCOUT): $(MISCBUILD)
	mkdir -p $(OUTDIR)
	cp $(MISCBUILD) $(MISCOUT)

clean: $(AUDIODIR)/Cargo.toml $(CLRPROFDIR)/Cargo.toml $(MISCDIR)/GNUmakefile
	rm -rf $(OUTDIR)
	cargo clean --manifest-path $(AUDIODIR)/Cargo.toml
	cargo clean --manifest-path $(CLRPROFDIR)/Cargo.toml
	make -C $(MISCDIR) clean RID=$(RID)
