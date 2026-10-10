/*
 * SPDX-FileCopyrightText: 2026 belshftl
 * SPDX-License-Identifier: MIT
 */

/*
 * creates a wl_subsurface of an external wl_surface, akin to what a UI framework embedding a
 * native child surface would do. uses its own event queue so it doesn't interfere with the
 * dispatching owner of the wl_display does on the default queue
 */

#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <wayland-client.h>

#define EXPORT __attribute__((__visibility__("default")))
#define UNUSED __attribute__((__unused__))

struct wlembed_sub {
	struct wl_display *display;
	struct wl_event_queue *queue;
	struct wl_display *wrapper;
	struct wl_registry *registry;
	struct wl_compositor *compositor;
	struct wl_subcompositor *subcompositor;
	struct wl_surface *surface;
	struct wl_subsurface *subsurface;
};

static void registry_global(
	void *data,
	struct wl_registry *registry,
	uint32_t name,
	const char *iface,
	uint32_t version
) {
	struct wlembed_sub *s = data;
	/* wl_surface.set_buffer_scale needs version 3 */
	if (strcmp(iface, wl_compositor_interface.name) == 0 && version >= 3)
		s->compositor = wl_registry_bind(registry, name, &wl_compositor_interface, version < 4 ? version : 4);
	else if (strcmp(iface, wl_subcompositor_interface.name) == 0)
		s->subcompositor = wl_registry_bind(registry, name, &wl_subcompositor_interface, 1);
}

static void registry_global_remove(
	UNUSED void *data,
	UNUSED struct wl_registry *registry,
	UNUSED uint32_t name
) {
}

static const struct wl_registry_listener registry_listener = {
	.global = registry_global,
	.global_remove = registry_global_remove,
};

EXPORT void wlembed_sub_destroy(struct wlembed_sub *s) {
	if (!s)
		return;
	if (s->subsurface)
		wl_subsurface_destroy(s->subsurface);
	if (s->surface)
		wl_surface_destroy(s->surface);
	if (s->subcompositor)
		wl_subcompositor_destroy(s->subcompositor);
	if (s->compositor)
		wl_compositor_destroy(s->compositor);
	if (s->registry)
		wl_registry_destroy(s->registry);
	if (s->wrapper)
		wl_proxy_wrapper_destroy(s->wrapper);
	wl_display_flush(s->display);
	/* every proxy on the queue is gone by now */
	if (s->queue)
		wl_event_queue_destroy(s->queue);
	free(s);
}

/* returns NULL on failure */
EXPORT struct wlembed_sub *wlembed_sub_create(struct wl_display *display, struct wl_surface *parent) {
	struct wlembed_sub *s = calloc(1, sizeof(struct wlembed_sub));
	if (!s)
		return NULL;
	s->display = display;
	s->queue = wl_display_create_queue(display);
	s->wrapper = wl_proxy_create_wrapper(display);
	if (!s->queue || !s->wrapper)
		goto fail;
	wl_proxy_set_queue((struct wl_proxy *)s->wrapper, s->queue);
	s->registry = wl_display_get_registry(s->wrapper);
	if (!s->registry)
		goto fail;
	wl_registry_add_listener(s->registry, &registry_listener, s);
	if (wl_display_roundtrip_queue(display, s->queue) < 0 || !s->compositor || !s->subcompositor)
		goto fail;

	s->surface = wl_compositor_create_surface(s->compositor);
	if (!s->surface)
		goto fail;
	s->subsurface = wl_subcompositor_get_subsurface(s->subcompositor, s->surface, parent);
	if (!s->subsurface)
		goto fail;
	/* presents independently of the parent's commits */
	wl_subsurface_set_desync(s->subsurface);
	/* let pointer input fall through to the parent */
	struct wl_region *empty = wl_compositor_create_region(s->compositor);
	wl_surface_set_input_region(s->surface, empty);
	wl_region_destroy(empty);
	wl_surface_commit(s->surface);
	if (wl_display_flush(display) < 0)
		goto fail;
	return s;

fail:
	wlembed_sub_destroy(s);
	return NULL;
}

EXPORT struct wl_surface *wlembed_sub_surface(struct wlembed_sub *s) {
	return s->surface;
}

/* the position applies on the parent's next commit, the scale on the subsurface's next commit */
EXPORT void wlembed_sub_configure(struct wlembed_sub *s, int32_t x, int32_t y, int32_t scale) {
	wl_subsurface_set_position(s->subsurface, x, y);
	wl_surface_set_buffer_scale(s->surface, scale);
	wl_display_flush(s->display);
}

/* dispatches events for the subsurface's own proxies (e.g. wl_surface.enter), returns -1 on error */
EXPORT int wlembed_sub_dispatch(struct wlembed_sub *s) {
	return wl_display_dispatch_queue_pending(s->display, s->queue);
}
