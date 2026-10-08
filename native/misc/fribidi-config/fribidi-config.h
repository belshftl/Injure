/*
 * SPDX-FileCopyrightText: 2026 belshftl
 * SPDX-License-Identifier: MIT
 */

/*
 * hand-written replacement for the fribidi-config.h that fribidi's meson build generates from
 * lib/fribidi-config.h.in; keep in sync with the submodule's version
 */
#ifndef FRIBIDI_CONFIG_H
#define FRIBIDI_CONFIG_H

#define FRIBIDI "fribidi"
#define FRIBIDI_NAME "GNU FriBidi"
#define FRIBIDI_BUGREPORT "https://github.com/fribidi/fribidi/issues/new"

#define FRIBIDI_VERSION "1.0.16"
#define FRIBIDI_MAJOR_VERSION 1
#define FRIBIDI_MINOR_VERSION 0
#define FRIBIDI_MICRO_VERSION 16
#define FRIBIDI_INTERFACE_VERSION 4
#define FRIBIDI_INTERFACE_VERSION_STRING "4"

#define FRIBIDI_SIZEOF_INT 4

#undef FRIBIDI_BUILT_WITH_MSVC

#endif /* FRIBIDI_CONFIG_H */
