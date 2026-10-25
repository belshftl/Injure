/*
 * SPDX-FileCopyrightText: 2026 belshftl
 * SPDX-License-Identifier: MIT
 */

/*
 * This header file is normative. Any deviation between this header and the actual exports or
 * behavior of the native library is a bug.
 */

/*
 * Unless stated otherwise, `T` is the pointee type, and terms such as "events" or "UTF-16 code
 * units" refer to elements of that type.
 *
 * A pointer is *readable* for `N` elements of type `T` if either:
 * - `N` is 0 (informative: this means the pointer may be any value, e.g. null)
 * - it is properly aligned for `T` and points to `N * sizeof(T)` initialized, readable bytes that
 *   form N valid values of type `T`
 *
 * A pointer is *writable* for `N` elements of type `T` if either:
 * - `N` is 0 (informative: this means the pointer may be any value, e.g. null)
 * - it is properly aligned for `T` and points to `N * sizeof(T)` writable bytes; the bytes need
 *   not be initialized
 *
 * Both properties must hold for the duration of the call. Writable regions must not overlap any
 * other region passed to the same call.
 *
 * A pointer with a corresponding length argument is *empty* if the length is 0.
 */

#pragma once

#include <stddef.h>
#include <stdint.h>

#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((__visibility__("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/*
 * ================================================================================================
 * types
 */
typedef int32_t HRESULT;

/*
 * The COM definition of `GUID`.
 *
 * # ABI
 *
 * As documented in MS-DTYP §2.3.4.1 and §2.3.4.2, subject to native endianness.
 */
typedef struct GUID {
	uint32_t data1;
	uint16_t data2;
	uint16_t data3;
	uint8_t data4[8];
} GUID;

typedef size_t ModuleID;

typedef uint32_t mdMethodDef;

typedef enum EventKind : int32_t {
	EVENT_KIND_MODULE_LOADED = 1,
	EVENT_KIND_MODULE_UNLOADING = 2,
	EVENT_KIND_REJIT_FAILED = 3,
	EVENT_KIND_SHUTDOWN = 4,
	EVENT_KIND_WAKEUP = 5,
} EventKind;

/*
 * One profiler event received through `prof_wait_events`.
 *
 * # ABI
 *
 * On targets with 64-bit-wide pointers, has a size of 24 bytes and an alignment of 8 bytes:
 *   1. 4 bytes (kind)
 *   2. 4 bytes of padding
 *   3. 8 bytes (module)
 *   4. 4 bytes (token)
 *   5. 4 bytes (hresult)
 *
 * On targets with 32-bit-wide pointers, has a size of 16 bytes and an alignment of 4 bytes:
 *   1. 4 bytes (kind)
 *   2. 4 bytes (module)
 *   3. 4 bytes (token)
 *   4. 4 bytes (hresult)
 */
typedef struct Event {
	EventKind kind;
	/* [+ 4 bytes of padding here on targets with 64 bit wide pointers] */
	ModuleID module;
	mdMethodDef token;
	HRESULT hresult;
} Event;

/*
 * ================================================================================================
 * attachment and events
 */

/*
 * Gets whether the profiler loaded and its worker is running, returning a boolean.
 */
EXPORT int32_t prof_is_attached(void);

/*
 * Waits for profiler events and copies as many as fit, returning the number of events written.
 * Returns 0 without waiting if `buf` is null or empty.
 *
 * # Safety
 *
 * `buf` must be writable for `capacity` events.
 */
EXPORT size_t prof_wait_events(Event *buf, size_t capacity, uint64_t timeout_ms);

/*
 * Pushes an `EVENT_KIND_WAKEUP` event to the queue, for the purpose of waking a thread blocked in
 * `prof_wait_events`.
 */
EXPORT void prof_wakeup(void);

/*
 * ================================================================================================
 * modules
 */

/*
 * Finds a loaded module by a given UTF-16 path suffix. Returns 0 if there's no such module, or if
 * `suffix` is empty.
 *
 * # Safety
 *
 * `suffix` must be readable for `len` **UTF-16 code units** (not bytes).
 */
EXPORT ModuleID prof_find_module(const uint16_t *suffix, size_t len);

/*
 * Writes a module's path into `buf`, and the required minimum `capacity` value into `needed`.
 * `buf` may be null, in which case only `needed` will be written into.
 *
 * # Safety
 *
 * `buf` must be writable for `capacity` **UTF-16 code units** (not bytes) if non-null, and
 * `needed` must be writable for one `size_t`.
 */
EXPORT HRESULT prof_module_path(ModuleID module, uint16_t *buf, size_t capacity, size_t *needed);

/*
 * Writes a module's MVID into `mvid`.
 *
 * # Safety
 *
 * `mvid` must be writable for sixteen bytes.
 */
EXPORT HRESULT prof_module_mvid(ModuleID module, uint8_t *mvid);

/*
 * Gets whether a module belongs to a collectible load context. Tri-state: returns either a
 * boolean or -1 if there is no much module.
 */
EXPORT int32_t prof_module_is_collectible(ModuleID module); /* not a bool return */

/*
 * ================================================================================================
 * il
 */

/*
 * Writes a method's original IL into `buf`, and the required minimum `capacity` value into
 * `needed`. `buf` may be null, in which case only `needed` will be written into.
 *
 * # Safety
 *
 * `buf` must be writable for `capacity` bytes if non-null, and `needed` must be writable for
 * one `size_t`.
 */
EXPORT HRESULT prof_get_il(
	ModuleID module,
	mdMethodDef token,
	uint8_t *buf,
	size_t capacity,
	size_t *needed
);

/*
 * Stores the IL to install on a method's next ReJIT. `body` is copied out, and as such need only
 * remain valid for the duration of the call. An empty `body` fails with `E_INVALIDARG`.
 *
 * # Safety
 *
 * `body` must be readable for `len` bytes.
 */
EXPORT HRESULT prof_set_prepared(ModuleID module, mdMethodDef token, const uint8_t *body, size_t len);

/*
 * Requests a ReJIT for a batch of methods.
 *
 * Asynchronous with respect to running code; already-executing frames continue on the old version.
 * If `modules`/`tokens` are empty, does nothing and succeeds.
 *
 * # Safety
 *
 * `modules` and `tokens` must each be readable for `count` **elements** (not bytes).
 */
EXPORT HRESULT prof_request_rejit(const ModuleID *modules, const mdMethodDef *tokens, size_t count);

/*
 * Reverts a batch of methods to the IL in their modules and forgets their prepared bodies. If
 * `modules`/`tokens` are empty, does nothing and succeeds.
 *
 * # Safety
 *
 * `modules` and `tokens` must each be readable for `count` **elements** (not bytes).
 */
EXPORT HRESULT prof_request_revert(const ModuleID *modules, const mdMethodDef *tokens, size_t count);

/*
 * ================================================================================================
 * metadata
 */

/*
 * Defines an `AssemblyRef`, or returns the existing row for the same identity. An empty `name`
 * fails with `E_INVALIDARG`; `public_key` may be empty.
 *
 * # Safety
 *
 * `name` must be readable for `name_len` **UTF-16 code units** (not bytes), `public_key` must
 * be readable for `public_key_len` bytes, and `token` must be writable for one `uint32_t`.
 */
EXPORT HRESULT prof_define_assembly_ref(
	ModuleID module,
	const uint16_t *name,
	size_t name_len,
	uint16_t major,
	uint16_t minor,
	uint16_t build,
	uint16_t revision,
	const uint8_t *public_key,
	size_t public_key_len,
	uint32_t flags,
	uint32_t *token
);

/*
 * Defines a `TypeRef`, or returns the existing row. An empty `name` fails with `E_INVALIDARG`;
 * `namespace` may be empty.
 *
 * # Safety
 *
 * `namespace` must be readable for `namespace_len` **UTF-16 code units** (not bytes), `name` must
 * be readable for `name_len` **UTF-16 code units** (not bytes), and `token` must be writable for
 * one `uint32_t`.
 */
EXPORT HRESULT prof_define_type_ref(
	ModuleID module,
	uint32_t scope,
	const uint16_t *namespace_, /* `namespace` is a C++ keyword; this is not an issue for the real export */
	size_t namespace_len,
	const uint16_t *name,
	size_t name_len,
	uint32_t *token
);

/*
 * Defines a `MemberRef`, or returns the existing row. An empty `name` or `signature` fails with
 * `E_INVALIDARG`.
 *
 * # Safety
 *
 * `name` must be readable for `name_len` **UTF-16 code units** (not bytes), `signature` must be
 * readable for `signature_len` bytes, and `token` must be writable for one `uint32_t`.
 */
EXPORT HRESULT prof_define_member_ref(
	ModuleID module,
	uint32_t parent,
	const uint16_t *name,
	size_t name_len,
	const uint8_t *signature,
	size_t signature_len,
	uint32_t *token
);

/*
 * Defines a `TypeSpec` from an encoded signature. An empty `signature` fails with `E_INVALIDARG`.
 *
 * # Safety
 *
 * `signature` must be readable for `signature_len` bytes, and `token` must be writable for one
 * `uint32_t`.
 */
EXPORT HRESULT prof_define_type_spec(
	ModuleID module,
	const uint8_t *signature,
	size_t signature_len,
	uint32_t *token
);

/*
 * Defines a `MethodSpec` over a method and its generic arguments. An empty `signature` fails with
 * `E_INVALIDARG`.
 *
 * # Safety
 *
 * `signature` must be readable for `signature_len` bytes, and `token` must be writable for one
 * `uint32_t`.
 */
EXPORT HRESULT prof_define_method_spec(
	ModuleID module,
	uint32_t method,
	const uint8_t *signature,
	size_t signature_len,
	uint32_t *token
);

/*
 * Defines a `StandAloneSig`. An empty `signature` fails with `E_INVALIDARG`.
 *
 * # Safety
 *
 * `signature` must be readable for `signature_len` bytes, and `token` must be writable for one
 * `uint32_t`.
 */
EXPORT HRESULT prof_define_standalone_sig(
	ModuleID module,
	const uint8_t *signature,
	size_t signature_len,
	uint32_t *token
);

/*
 * Adds a user string, returning its token (prefixed with `0x70`). `value` may be empty.
 *
 * # Safety
 *
 * `value` must be readable for `len` **UTF-16 code units** (not bytes), and `token` must be
 * writable for one `uint32_t`.
 */
EXPORT HRESULT prof_define_user_string(
	ModuleID module,
	const uint16_t *value,
	size_t len,
	uint32_t *token
);

/*
 * Publishes rows defined since the last call to this function.
 *
 * Tokens handed out by the other functions are usable in IL only after this returns.
 */
EXPORT HRESULT prof_apply_metadata(ModuleID module);

/*
 * ================================================================================================
 * com entrypoint
 */

/*
 * Called by CoreCLR to obtain the profiler's class factory.
 *
 * # Safety
 *
 * Called by the runtime with a valid CLSID, IID, and writable out pointer.
 */
EXPORT HRESULT DllGetClassObject(const GUID *clsid, const GUID *iid, void **out);

/*
 * Whether the library can be unloaded. Always `S_FALSE`, as a profiler stays for the lifetime of
 * the process, and unloading one when ReJIT'd code is still installed would be very problematic.
 */
EXPORT HRESULT DllCanUnloadNow(void);

#ifdef __cplusplus
}
#endif
