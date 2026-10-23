# SPDX-FileCopyrightText: 2026 belshftl
# SPDX-License-Identifier: MIT

RID      ?= $(error RID must be set)

ROOT     := .
SRCDIR   := $(ROOT)/src
OBJDIR   := $(ROOT)/obj/$(RID)
OUTDIR   := $(ROOT)/out/$(RID)

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
	CPPFLAGS += -DIJMISC_MACOS
	CFLAGS += -fPIC
	LDFLAGS += -dynamiclib
else ifneq ($(filter linux-x64 linux-arm64,$(RID)),)
	OUT := $(OUTDIR)/libijmisc.so
	CPPFLAGS += -DIJMISC_POSIX -D_POSIX_C_SOURCE=200112L
	CFLAGS += -fPIC
	LDFLAGS += -shared
else
	$(error unsupported RID $(RID))
endif

.PHONY: all clean
all: $(OUT)

$(OUT): $(OBJ)
	mkdir -p $(dir $@)
	$(CC) $(OBJ) $(LDFLAGS) -o $@ $(LDLIBS)

$(OBJDIR)/%.o: $(SRCDIR)/%.c
	mkdir -p $(dir $@)
	$(CC) $(CPPFLAGS) $(CFLAGS) -MMD -MP -MF $(@:.o=.d) -MT $@ -c -o $@ $<

clean:
	rm -rf $(OBJDIR) $(OUTDIR)

-include $(DEP)
