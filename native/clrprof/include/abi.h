/*
 * SPDX-FileCopyrightText: 2026 belshftl
 * SPDX-License-Identifier: MIT
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
 * ============================================================================
 * types
 */
typedef int32_t HRESULT;

typedef struct {
	uint32_t data1;
	uint16_t data2;
	uint16_t data3;
	uint8_t data4[8];
} GUID;

typedef size_t ModuleID;

typedef uint32_t mdMethodDef;

enum EventKind : int32_t {
	EVENT_KIND_MODULE_LOADED = 1,
	EVENT_KIND_MODULE_UNLOADING = 2,
	EVENT_KIND_REJIT_FAILED = 3,
	EVENT_KIND_SHUTDOWN = 4,
	EVENT_KIND_WAKEUP = 5,
};

typedef struct {
	enum EventKind kind;
	/* [+ 4 bytes of padding here on targets with 64 bit wide pointers] */
	ModuleID module;
	mdMethodDef token;
	HRESULT hresult;
} Event; // aligned to 8 bytes on 64 bit and 4 bytes on 32 bit

/*
 * ============================================================================
 * attachment and events
 */
EXPORT int32_t prof_is_attached(void);
EXPORT size_t prof_wait_events(Event *buf, size_t capacity, uint64_t timeout_ms);
EXPORT void prof_wakeup(void);

/*
 * ============================================================================
 * modules
 */
EXPORT ModuleID prof_find_module(const uint16_t *suffix, size_t len);
EXPORT HRESULT prof_module_path(ModuleID module, uint16_t *buf, size_t capacity, size_t *needed);
EXPORT HRESULT prof_module_mvid(ModuleID module, uint8_t *mvid);
EXPORT int32_t prof_module_is_collectible(ModuleID module); /* not a bool return */

/*
 * ============================================================================
 * il
 */
EXPORT HRESULT prof_get_il(
	ModuleID module,
	mdMethodDef token,
	uint8_t *buf,
	size_t capacity,
	size_t *needed
);
EXPORT HRESULT prof_set_prepared(ModuleID module, mdMethodDef token, const uint8_t *body, size_t len);
EXPORT HRESULT prof_request_rejit(const ModuleID *modules, const mdMethodDef *tokens, size_t count);
EXPORT HRESULT prof_request_revert(const ModuleID *modules, const mdMethodDef *tokens, size_t count);

/*
 * ============================================================================
 * metadata
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
EXPORT HRESULT prof_define_type_ref(
	ModuleID module,
	uint32_t scope,
	const uint16_t *namespace_, /* `namespace` is a C++ keyword; this is not an issue for the real export */
	size_t namespace_len,
	const uint16_t *name,
	size_t name_len,
	uint32_t *token
);
EXPORT HRESULT prof_define_member_ref(
	ModuleID module,
	uint32_t parent,
	const uint16_t *name,
	size_t name_len,
	const uint8_t *signature,
	size_t signature_len,
	uint32_t *token
);
EXPORT HRESULT prof_define_type_spec(
	ModuleID module,
	const uint8_t *signature,
	size_t signature_len,
	uint32_t *token
);
EXPORT HRESULT prof_define_method_spec(
	ModuleID module,
	uint32_t method,
	const uint8_t *signature,
	size_t signature_len,
	uint32_t *token
);
EXPORT HRESULT prof_define_standalone_sig(
	ModuleID module,
	const uint8_t *signature,
	size_t signature_len,
	uint32_t *token
);
EXPORT HRESULT prof_define_user_string(
	ModuleID module,
	const uint16_t *value,
	size_t len,
	uint32_t *token
);
EXPORT HRESULT prof_apply_metadata(ModuleID module);

/*
 * ============================================================================
 * com entrypoint
 */
EXPORT HRESULT DllGetClassObject(const GUID *clsid, const GUID *iid, void **out);
EXPORT HRESULT DllCanUnloadNow(void);

#ifdef __cplusplus
}
#endif
