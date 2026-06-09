// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

namespace Injure.Native;

#pragma warning disable CA1401 // p/invoke method should not be visible
#pragma warning disable IDE1006 // naming rule violation

public static partial class Audio {
	public enum ae_result : int {
		AE_OK = 0,
		AE_ERR_NULL = -1,
		AE_ERR_BAD_STATE = -2,
		AE_ERR_JACK_OPEN_FAILED = -3,
		AE_ERR_JACK_PORT_FAILED = -4,
		AE_ERR_JACK_ACTIVATE_FAILED = -5,
		AE_ERR_NO_OUTPUT_PORTS = -6,
		AE_ERR_COMMAND_QUEUE_FULL = -7,
		AE_ERR_LOCK_POISONED = -8,
		AE_ERR_BAD_COMMAND = -9,
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct ae_config {
		public int channels;
		public int command_capacity;
		public int max_voices;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct ae_stats {
		public long frame_cursor;
		public int sample_rate;
		public int quantum_frames;
		public int channel_count;
		public long xrun_count;
		public int running;
		public float cpu_load;
		public long dropped_command_count;
	}

	public enum ae_command_kind : uint {
		AE_COMMAND_NONE = 0,
		AE_COMMAND_SET_TEST_TONE = 1,
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct ae_set_test_tone_command {
		public int enabled;
		public float hz;
		public float gain;
		public int _reserved0;
	}

	[StructLayout(LayoutKind.Explicit)]
	public struct ae_command_data {
		[FieldOffset(0)] public ae_set_test_tone_command set_test_tone;
		[FieldOffset(0)] public unsafe fixed ulong raw[8];
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct ae_command {
		public ae_command_kind kind;
		public uint size;
		public long target_frame;
		public ae_command_data data;
	}

	public const long AE_COMMAND_IMMEDIATE = long.MinValue;

	// ae_engine *ae_create(const ae_config *config)
	// #[no_mangle] fn ae_create(config: *const AeConfig) -> *mut ae_engine
	[LibraryImport("injureaudio")]
	public static partial IntPtr ae_create(in ae_config config);

	// void ae_destroy(ae_engine *engine);
	// #[no_mangle] fn ae_destroy(engine: *mut ae_engine)
	[LibraryImport("injureaudio")]
	public static partial void ae_destroy(nint engine);

	// ae_result ae_start_jack(ae_engine *engine, const char *client_name, int32_t autoconnect)
	// #[no_mangle] fn ae_start_jack(wrapper: *mut ae_engine, client_name: *const c_char, autoconnect: i32) -> i32
	[LibraryImport("injureaudio", StringMarshalling = StringMarshalling.Utf8)]
	public static partial ae_result ae_start_jack(IntPtr engine, string client_name, int autoconnect);

	// ae_result ae_stop(ae_engine *engine)
	// #[no_mangle] fn ae_stop(wrapper: *mut ae_engine) -> i32
	[LibraryImport("injureaudio")]
	public static partial ae_result ae_stop(IntPtr engine);

	// ae_result ae_get_stats(ae_engine *engine, ae_stats *out_stats)
	// #[no_mangle] fn ae_get_stats(wrapper: *const ae_engine, out_stats: *mut AeStats) -> i32
	[LibraryImport("injureaudio")]
	public static partial ae_result ae_get_stats(IntPtr engine, out ae_stats out_stats);

	// ae_result ae_enqueue_command(ae_engine *engine, const ae_command *command)
	// fn ae_enqueue_command(wrapper: *mut ae_engine, command: *const AeCommand) -> i32
	[LibraryImport("injureaudio")]
	public static partial ae_result ae_enqueue_command(IntPtr engine, in ae_command command);
}

public static partial class PreciseWait {
	// int precisewait_init(void)
	[LibraryImport("injuremisc")]
	public static partial int precisewait_init();

	// void precisewait_deinit(void)
	[LibraryImport("injuremisc")]
	public static partial void precisewait_deinit();

	// int precisewait(int64_t ns, int overshoot)
	[LibraryImport("injuremisc")]
	public static partial int precisewait(long ns, [MarshalAs(UnmanagedType.Bool)] bool overshoot);
}

public static partial class Unibreak {
	// void set_linebreaks_utf16(const utf16_t *s, size_t len, const char *lang, char *brks)
	[LibraryImport("injuremisc", StringMarshalling = StringMarshalling.Utf8)]
	public static unsafe partial void set_linebreaks_utf16(char* s, nuint len, string? lang, byte* brks);
}

#pragma warning restore IDE1006 // naming rule violation
#pragma warning restore CA1401 // p/invoke method should not be visible
