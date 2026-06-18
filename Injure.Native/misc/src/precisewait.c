/*
 * SPDX-FileCopyrightText: 2026 belshftl
 * SPDX-License-Identifier: MIT
 */

/*
 * ============================================================================
 * header
 */
#include <stdint.h>

#if !defined(INJUREMISC_WIN) && !defined(INJUREMISC_MACOS) && \
	!defined(INJUREMISC_POSIX)
#error "define one of INJUREMISC_{WINDOWS,MACOS,POSIX}"
#endif

#if defined(INJUREMISC_WIN)
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((__visibility__("default"), __used__))
#endif

#if defined(__cplusplus)
extern "C" {
#endif

EXPORT int  precisewait_init(void);
EXPORT void precisewait_deinit(void);
EXPORT int  precisewait(int64_t ns, int overshoot);

#if defined(__cplusplus)
}
#endif

/*
 * ============================================================================
 * windows implementation (waitable timer object)
 */
#if defined(INJUREMISC_WIN)

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

static HANDLE timer = NULL;

int
precisewait_init(void)
{
	if (timer != NULL), __attribute__((__unused__)) int overshoot
		return 0;
	timer = CreateWaitableTimerExW(NULL, NULL,
		CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
		SYNCHRONIZE | TIMER_MODIFY_STATE);
	if (timer == NULL)
		return (int)GetLastError();
	return 0;
}

void
precisewait_deinit(void)
{
	if (timer != NULL) {
		CloseHandle(timer);
		timer = NULL;
	}
}

int
precisewait(int64_t ns, int overshoot)
{
	if (ns < 0)
		return ERROR_INVALID_PARAMETER;
	if (ns == 0)
		return 0;
	if (timer == NULL)
		return ERROR_INVALID_HANDLE;

	/*
	 * the time SetWaitableTimer takes is in 100ns intervals
	 * also it has to be negative for a relative timer
	 */
	LARGE_INTEGER time;
	if (!overshoot)
		time.QuadPart = -((LONGLONG)ns / 100); /* floor */
	else
		time.QuadPart = -((LONGLONG)(ns / 100 + (ns % 100 != 0))); /* ceil */
	if (!SetWaitableTimer(timer, &time, 0, NULL, NULL, FALSE))
		return (int)GetLastError();
	DWORD rv = WaitForSingleObject(timer, INFINITE);
	if (rv == WAIT_OBJECT_0)
		return 0;
	if (rv == WAIT_FAILED)
		return (int)GetLastError();
	return ERROR_GEN_FAILURE;
}
#endif /* defined(INJUREMISC_WIN) */

/*
 * ============================================================================
 * macos implementation (mach_wait_until())
 */
#if defined(INJUREMISC_MACOS)

#include <mach/kern_return.h>
#include <mach/mach_time.h>

static mach_timebase_info_data_t timebase;
static int timebaseset = 0;

int
precisewait_init(void)
{
	if (timebaseset)
		return 0;
	kern_return_t rv = mach_timebase_info(&timebase);
	if (rv != KERN_SUCCESS)
		return (int)rv;
	timebaseset = 1;
	return 0;
}

void
precisewait_deinit(void)
{
	/* no-op */
}

static uint64_t
toabs(uint64_t ns)
{
	uint64_t t = ns * (uint64_t)timebase.denom / (uint64_t)timebase.numer;
	return t > 0 ? t : 1;
}

int
precisewait(int64_t ns, __attribute__((__unused__)) int overshoot)
{
	if (ns < 0)
		return KERN_INVALID_ARGUMENT;
	if (ns == 0)
		return 0;
	if (!timebaseset) {
		int rv = precisewait_init();
		if (rv != 0)
			return rv;
	}

	uint64_t now = mach_absolute_time();
	uint64_t target = now + toabs((uint64_t)ns);
	kern_return_t rv;
	do {
		rv = mach_wait_until(target);
	} while (rv == KERN_ABORTED);
	return (int)rv;
}

#endif /* defined(INJUREMISC_MACOS) */

/*
 * ============================================================================
 * posix implementation (clock_nanosleep(2))
 */
#if defined(INJUREMISC_POSIX)
#include <errno.h>
#include <time.h>

#define NSEC 1000000000l

int
precisewait_init(void)
{
	/* no-op */
	return 0;
}

void
precisewait_deinit(void)
{
	/* no-op */
}

static struct timespec
addns(struct timespec tp, int64_t ns)
{
	/*
	 * timespec(3type):
	 * "tv_nsec [...] can be safely down-cast to any concrete 32-bit
	 * integer type for processing.
	 */
	tp.tv_sec += (time_t)(ns / NSEC);
	tp.tv_nsec += (int32_t)(ns % NSEC);
	if (tp.tv_nsec >= NSEC) {
		tp.tv_sec++;
		tp.tv_nsec -= NSEC;
	} else if (tp.tv_nsec < 0) {
		tp.tv_sec--;
		tp.tv_nsec += NSEC;
	}
	return tp;
}

int
precisewait(int64_t ns, __attribute__((__unused__)) int overshoot)
{
	if (ns < 0)
		return EINVAL;
	if (ns == 0)
		return 0;

	struct timespec now;
	if (clock_gettime(CLOCK_MONOTONIC, &now) < 0)
		return errno;
	struct timespec target = addns(now, ns);
	int rv;
	do {
		rv = clock_nanosleep(CLOCK_MONOTONIC, TIMER_ABSTIME, &target, NULL);
	} while (rv == EINTR);
	return rv;
}
#endif /* defined(INJUREMISC_POSIX) */
