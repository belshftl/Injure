/*
 * SPDX-FileCopyrightText: 2026 belshftl
 * SPDX-License-Identifier: MIT
 */

/*
 * hand-written replacement for the config.h that fribidi's meson build generates; only covers the
 * currently supported (POSIX) RIDs
 */
#ifndef IJMISC_FRIBIDI_CONFIG_H
#define IJMISC_FRIBIDI_CONFIG_H

#define HAVE_MEMMOVE 1
#define HAVE_MEMSET 1
#define HAVE_STRDUP 1

#define HAVE_STDLIB_H 1
#define HAVE_STRING_H 1
#define HAVE_MEMORY_H 1
#define HAVE_STRINGS_H 1
#define HAVE_SYS_TIMES_H 1

#define STDC_HEADERS 1
#define HAVE_STRINGIZE 1

#endif /* IJMISC_FRIBIDI_CONFIG_H */
