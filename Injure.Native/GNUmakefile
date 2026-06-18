RID      ?= $(error RID must be set)
MODE     ?= debug

ROOT     := .
AUDIODIR := $(ROOT)/audio
MISCDIR  := $(ROOT)/misc

ifeq ($(MODE),debug)
	CARGOFLAGS ?=
else ifeq ($(MODE),release)
	CARGOFLAGS ?= --release
else
	$(error unsupported build mode $(MODE), supported: debug, release)
endif

ifneq ($(filter osx-x64 osx-arm64,$(RID)),)
	AUDIOOUT := $(AUDIODIR)/target/$(MODE)/libinjureaudio.dylib
	MISCOUT := $(MISCDIR)/out/$(RID)/libinjuremisc.dylib
else ifneq ($(filter linux-x64 linux-arm64,$(RID)),)
	AUDIOOUT := $(AUDIODIR)/target/$(MODE)/libinjureaudio.so
	MISCOUT := $(MISCDIR)/out/$(RID)/libinjuremisc.so
else
	$(error unsupported RID $(RID))
endif

AUDIOSRC := $(shell find $(AUDIODIR)/src -name '*.rs') $(AUDIODIR)/build.rs
MISCSRC  := $(MISCDIR)/src/precisewait.c

.PHONY: all clean
all: $(AUDIOOUT) $(MISCOUT)

$(AUDIOOUT): $(AUDIOSRC) $(AUDIODIR)/Cargo.toml
	cargo build $(CARGOFLAGS) --manifest-path $(AUDIODIR)/Cargo.toml

$(MISCOUT): $(MISCSRC) $(MISCDIR)/GNUmakefile
	make -C $(MISCDIR) RID=$(RID)

clean: $(AUDIODIR)/Cargo.toml $(MISCDIR)/GNUmakefile
	cargo clean --manifest-path $(AUDIODIR)/Cargo.toml
	make -C $(MISCDIR) clean RID=$(RID)
