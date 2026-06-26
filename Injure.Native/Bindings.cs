// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Native;

#pragma warning disable CA1401 // p/invoke method should not be visible
#pragma warning disable IDE1006 // naming rule violation

public static partial class Audio {
	[StronglyTypedInt(typeof(IntPtr))]
	[NativeMarshalling(typeof(AeEnginePtrMarshaller))]
	public readonly partial struct ae_engine_ptr {
	}

	[StronglyTypedInt(typeof(ulong))]
	[NativeMarshalling(typeof(AeSoundIdMarshaller))]
	public readonly partial struct ae_sound_id {
	}

	[StronglyTypedInt(typeof(ulong))]
	[NativeMarshalling(typeof(AeVoiceIdMarshaller))]
	public readonly partial struct ae_voice_id {
	}

	[StructLayout(LayoutKind.Sequential)]
	public readonly struct ae_mix_frame :
		IEquatable<ae_mix_frame>,
		IEquatable<ae_optional_mix_frame>,
		IComparable<ae_mix_frame>,
		ISpanFormattable {
		private readonly ulong backing;
		public ulong Value => backing != 0
			? backing
			: throw new InvalidOperationException("badly constructed ae_mix_frame value; if you got this from an API, this could be memory corruption");

		public static explicit operator ulong(ae_mix_frame val) => val.Value;
		public static implicit operator ae_optional_mix_frame(ae_mix_frame val) => new(val.Value);

		public bool Equals(ae_mix_frame other) => Value == other.Value;
		public bool Equals(ae_optional_mix_frame other) => other.Value != 0 && Value == other.Value;
		public override bool Equals([NotNullWhen(true)] object? obj) => obj is ae_mix_frame mx && Equals(mx) || obj is ae_optional_mix_frame opt && Equals(opt);
		public override int GetHashCode() => Value.GetHashCode();
		public static bool operator ==(ae_mix_frame left, ae_mix_frame right) => left.Equals(right);
		public static bool operator !=(ae_mix_frame left, ae_mix_frame right) => !left.Equals(right);
		public static bool operator ==(ae_mix_frame left, ae_optional_mix_frame right) => left.Equals(right);
		public static bool operator !=(ae_mix_frame left, ae_optional_mix_frame right) => !left.Equals(right);
		public static bool operator ==(ae_optional_mix_frame left, ae_mix_frame right) => right.Equals(left);
		public static bool operator !=(ae_optional_mix_frame left, ae_mix_frame right) => !right.Equals(left);

		public int CompareTo(ae_mix_frame other) => Value.CompareTo(other.Value);
		public static bool operator <(ae_mix_frame left, ae_mix_frame right) => left.Value < right.Value;
		public static bool operator >(ae_mix_frame left, ae_mix_frame right) => left.Value > right.Value;
		public static bool operator <=(ae_mix_frame left, ae_mix_frame right) => left.Value <= right.Value;
		public static bool operator >=(ae_mix_frame left, ae_mix_frame right) => left.Value >= right.Value;

		public override string ToString() => Value.ToString();

		public string ToString(string? format, IFormatProvider? provider) =>
			Value.ToString(format, provider);
		public bool TryFormat(Span<char> dst, out int written, ReadOnlySpan<char> format, IFormatProvider? provider) =>
			Value.TryFormat(dst, out written, format, provider);
	}

	[StronglyTypedInt(typeof(ulong))]
	[NativeMarshalling(typeof(AeOptionalMixFrameMarshaller))]
	public readonly partial struct ae_optional_mix_frame {
		public static readonly ae_optional_mix_frame IMMEDIATE = Zero;
	}

	[CustomMarshaller(typeof(ae_engine_ptr), MarshalMode.Default, typeof(AeEnginePtrMarshaller))]
	internal static class AeEnginePtrMarshaller {
		public static IntPtr ConvertToUnmanaged(ae_engine_ptr value) => value.Value;
		public static ae_engine_ptr ConvertToManaged(IntPtr value) => new(value);
	}

	[CustomMarshaller(typeof(ae_sound_id), MarshalMode.Default, typeof(AeSoundIdMarshaller))]
	internal static class AeSoundIdMarshaller {
		public static ulong ConvertToUnmanaged(ae_sound_id value) => value.Value;
		public static ae_sound_id ConvertToManaged(ulong value) => new(value);
	}

	[CustomMarshaller(typeof(ae_voice_id), MarshalMode.Default, typeof(AeVoiceIdMarshaller))]
	internal static class AeVoiceIdMarshaller {
		public static ulong ConvertToUnmanaged(ae_voice_id value) => value.Value;
		public static ae_voice_id ConvertToManaged(ulong value) => new(value);
	}

	[CustomMarshaller(typeof(ae_optional_mix_frame), MarshalMode.Default, typeof(AeOptionalMixFrameMarshaller))]
	internal static class AeOptionalMixFrameMarshaller {
		public static ulong ConvertToUnmanaged(ae_optional_mix_frame value) => value.Value;
		public static ae_optional_mix_frame ConvertToManaged(ulong value) => new(value);
	}

	// @formatter:off
	public enum ae_result : uint {
		AE_OK = 0,

		AE_ERR_NULL                 = 0x80000001,
		AE_ERR_BAD_STATE            = 0x80000002,
		AE_ERR_MESSAGE_QUEUE_FULL   = 0x80000003,
		AE_ERR_MUTEX_POISONED       = 0x80000004,
		AE_ERR_BAD_SOUND_DESC       = 0x80000005,
		AE_ERR_SOUND_NOT_FOUND      = 0x80000006,
		AE_ERR_SOUND_BAD_STATE      = 0x80000007,
		AE_ERR_SOUND_IN_USE         = 0x80000008,
		AE_ERR_BAD_VOICE_DESC       = 0x80000009,
		AE_ERR_VOICE_NOT_FOUND      = 0x8000000a,
		AE_ERR_UNSUPPORTED_PLAYBACK = 0x8000000b,
		AE_ERR_ID_EXHAUSTED         = 0x8000000c,
		AE_ERR_FRAME_EXHAUSTED      = 0x8000000d,

		AE_ERR_JACK_OPEN_FAILED     = 0x81000001,
		AE_ERR_JACK_PORT_FAILED     = 0x81000002,
		AE_ERR_JACK_ACTIVATE_FAILED = 0x81000003,

		AE_ERR_OUT_OF_MEMORY        = 0xffffffff,
	}
	// @formatter:on

	public enum ae_sample_format : uint {
		AE_SAMPLE_FORMAT_F32 = 1,
	}

	public enum ae_voice_flags : uint {
		AE_VOICE_FLAG_NONE = 0,
		AE_VOICE_FLAG_LOOP = 1 << 0,
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct ae_config {
		public uint channels;
		public nuint message_queue_capacity;
		public nuint maintenance_capacity;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct ae_stats {
		public ae_mix_frame mix_frame;
		public uint sample_rate;
		public uint quantum_frames;
		public uint channel_count;
		public uint running;
		public long xrun_count;
		public long active_voice_count;
		public long voice_block_count;
		public long failed_voice_start_count;
		public long allocated_sound_count;
		public long committed_sound_count;
		public float cpu_load;
		public ae_result rt_error;
	}

	public struct ae_sound_desc {
		public uint channels;
		public uint sample_rate;
		public ulong frame_count;
		public ae_sample_format format;
		public uint flags;
	}

	public struct ae_sound_mapping {
		public unsafe void* data;
		public nuint byte_length;
		public nuint frame_stride;
	}

	public struct ae_play_voice_desc {
		public ae_sound_id sound;
		public ae_optional_mix_frame start_frame;
		public ulong source_frame;
		public float gain;
		public float playback_rate;
		public ae_voice_flags flags;
		public uint reserved0;
	}

	// ae_result ae_create(const ae_config *config, ae_engine **out_engine);
	// #[no_mangle] fn ae_create(config: *const AeConfig, out_wrapper: *mut *mut AeEngineWrapper) -> AeResult
	[LibraryImport("injureaudio")]
	public static partial ae_result ae_create(in ae_config config, out ae_engine_ptr out_engine);

	// void ae_destroy(ae_engine *engine);
	// #[no_mangle] fn ae_destroy(wrapper: *mut AeEngineWrapper)
	[LibraryImport("injureaudio")]
	public static partial void ae_destroy(ae_engine_ptr engine);

	// ae_result ae_start_jack(ae_engine *engine, const char *client_name, int32_t autoconnect);
	// #[no_mangle] fn ae_start_jack(wrapper: *mut AeEngineWrapper, client_name: *const c_char, autoconnect: i32) -> AeResult
	[LibraryImport("injureaudio", StringMarshalling = StringMarshalling.Utf8)]
	public static partial ae_result ae_start_jack(ae_engine_ptr engine, string client_name, int autoconnect);

	// ae_result ae_stop(ae_engine *engine);
	// #[no_mangle] fn ae_stop(wrapper: *mut AeEngineWrapper) -> AeResult
	[LibraryImport("injureaudio")]
	public static partial ae_result ae_stop(ae_engine_ptr engine);

	// ae_result ae_get_stats(ae_engine *engine, ae_stats *out_stats);
	// #[no_mangle] fn ae_get_stats(wrapper: *mut AeEngineWrapper, out_stats: *mut AeStats) -> AeResult
	[LibraryImport("injureaudio")]
	public static partial ae_result ae_get_stats(ae_engine_ptr engine, out ae_stats out_stats);

	// ae_result ae_collect_garbage(ae_engine *engine);
	// #[no_mangle] fn ae_collect_garbage(wrapper: *mut AeEngineWrapper) -> AeResult
	[LibraryImport("injureaudio")]
	public static partial ae_result ae_collect_garbage(ae_engine_ptr engine);

	// ae_result ae_sound_alloc(ae_engine *engine, const ae_sound_desc *desc, ae_sound_id *out_sound);
	// #[no_mangle] fn ae_sound_alloc(wrapper: *mut AeEngineWrapper, desc: *const AeSoundDesc, out_sound: *mut AeSoundId) -> AeResult
	[LibraryImport("injureaudio")]
	public static partial ae_result ae_sound_alloc(ae_engine_ptr engine, in ae_sound_desc desc, out ae_sound_id out_sound);

	// ae_result ae_sound_get_buffer(ae_engine *engine, ae_sound_id sound, ae_sound_mapping *out_mapping);
	// #[no_mangle] fn ae_sound_get_buffer(wrapper: *mut AeEngineWrapper, sound: AeSoundId, out_mapping: *mut AeSoundMapping) -> AeResult
	[LibraryImport("injureaudio")]
	public static partial ae_result ae_sound_get_buffer(ae_engine_ptr engine, ae_sound_id sound, out ae_sound_mapping out_mapping);

	// ae_result ae_sound_commit(ae_engine *engine, ae_sound_id sound);
	// #[no_mangle] fn ae_sound_commit(wrapper: *mut AeEngineWrapper, sound: AeSoundId) -> AeResult
	[LibraryImport("injureaudio")]
	public static partial ae_result ae_sound_commit(ae_engine_ptr engine, ae_sound_id sound);

	// ae_result ae_sound_free(ae_engine *engine, ae_sound_id sound);
	// #[no_mangle] fn ae_sound_free(wrapper: *mut AeEngineWrapper, sound: AeSoundId) -> AeResult
	[LibraryImport("injureaudio")]
	public static partial ae_result ae_sound_free(ae_engine_ptr engine, ae_sound_id sound);

	// ae_result ae_voice_play(ae_engine *engine, const ae_play_voice_desc *desc, ae_voice_id *out_voice);
	// #[no_mangle] fn ae_voice_play(wrapper: *mut AeEngineWrapper, desc: *const AePlayVoiceDesc, out_voice: *mut AeVoiceId) -> AeResult
	[LibraryImport("injureaudio")]
	public static partial ae_result ae_voice_play(ae_engine_ptr engine, in ae_play_voice_desc desc, out ae_voice_id out_voice);

	// ae_result ae_voice_stop(ae_engine *engine, ae_voice_id voice);
	// #[no_mangle] fn ae_voice_stop(wrapper: *mut AeEngineWrapper, voice: AeVoiceId) -> AeResult
	[LibraryImport("injureaudio")]
	public static partial ae_result ae_voice_stop(ae_engine_ptr engine, ae_voice_id voice);
}

public static partial class PreciseWait {
	// int precisewait_init(void);
	[LibraryImport("injuremisc")]
	public static partial int precisewait_init();

	// void precisewait_deinit(void);
	[LibraryImport("injuremisc")]
	public static partial void precisewait_deinit();

	// int precisewait(int64_t ns, int overshoot);
	[LibraryImport("injuremisc")]
	public static partial int precisewait(long ns, [MarshalAs(UnmanagedType.Bool)] bool overshoot);
}

public static partial class Unibreak {
	// void set_linebreaks_utf16(const utf16_t *s, size_t len, const char *lang, char *brks);
	[LibraryImport("injuremisc", StringMarshalling = StringMarshalling.Utf8)]
	public static unsafe partial void set_linebreaks_utf16(char* s, nuint len, string? lang, byte* brks);
}

#pragma warning restore IDE1006 // naming rule violation
#pragma warning restore CA1401 // p/invoke method should not be visible
