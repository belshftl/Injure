// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Input;

/// <summary>
/// A physical keyboard key, identified by its position (like a USB HID scancode), not by the
/// character it produces under the current keyboard layout.
/// </summary>
/// <remarks>
/// <para>
/// Names follow the US QWERTY layout; for example, <see cref="A"/> is the key right of Caps Lock
/// regardless of layout. Keys outside this set are not reported at all.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Unknown"/>.
/// </para>
/// </remarks>
[ClosedEnum]
public readonly partial struct Key {
	/// <summary>Raw switch tag for <see cref="Key"/>.</summary>
	public enum Case {
		/// <summary>
		/// A key without an engine equivalent; can appear in host events, but
		/// <see cref="InputSystem"/> drops it.
		/// </summary>
		Unknown,

		A, B, C, D, E, F, G, H, I, J, K, L, M,
		N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

		Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Digit0,

		Enter,
		Escape,
		Backspace,
		Tab,
		Space,

		Minus,
		Equal,
		LeftBracket,
		RightBracket,

		/// <remarks>
		/// Located at the lower left of the Return key on ISO keyboards, and at the right end of the
		/// QWERTY row on ANSI keyboards.
		/// </remarks>
		Backslash,

		// skip SDL_SCANCODE_NONUSHASH: its docs (https://wiki.libsdl.org/SDL3/SDL_Scancode) say:
		// > ISO USB keyboards actually use this code instead of 49 for the same key, but all OSes I've
		//   seen treat the two codes identically. So, as an implementer, unless your keyboard generates
		//   both of those codes and your OS treats them differently, you should generate
		//   SDL_SCANCODE_BACKSLASH instead of this code. As a user, you should not rely on this code
		//   because SDL will never generate it with most (all?) keyboards.

		Semicolon,
		Apostrophe,

		/// <remarks>
		/// <para>
		/// Located in the top left corner, below the Escape key and to the left of the 1 key, on both
		/// ANSI and ISO keyboards.
		/// </para>
		/// <para>
		/// Corresponds to the Hankaku/Zenkaku key on Japanese (JIS) keyboards, which has the same
		/// location.
		/// </para>
		/// </remarks>
		Grave,

		Comma,
		Period,
		Slash,

		Capslock,

		F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

		PrintScreen,
		Scrolllock,
		Pause,

		/// <remarks>
		/// On Apple's pre-aluminum (before 2007) full-size keyboards that have a Help key where PC
		/// keyboards have Insert, this maps to said Help key.
		/// </remarks>
		Insert,

		Home,
		PageUp,
		Delete,
		End,
		PageDown,

		Right,
		Left,
		Down,
		Up,

		/// <remarks>
		/// Num Lock on PC keyboards, or the Clear key on Apple keyboards.
		/// </remarks>
		NumlockClear,

		NumpadDivide,
		NumpadMultiply,
		NumpadMinus,
		NumpadPlus,
		NumpadEnter,
		Numpad1,
		Numpad2,
		Numpad3,
		Numpad4,
		Numpad5,
		Numpad6,
		Numpad7,
		Numpad8,
		Numpad9,
		Numpad0,
		NumpadPeriod,

		/// <summary>
		/// The additional key that ISO keyboards have over ANSI ones, located between Left Shift and Z.
		/// </summary>
		NonUsBackslash,

		/// <summary>
		/// The context menu key. See <see href="https://en.wikipedia.org/wiki/Menu_key"/>.
		/// </summary>
		Application,

		/// <summary>
		/// The = key on the numpad of Apple's full-size keyboards.
		/// </summary>
		NumpadEquals,

		F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,

		/// <summary>
		/// The comma key on the numpad of Brazilian (ABNT) keyboards, as well as Apple's Japanese (JIS)
		/// keyboards.
		/// </summary>
		NumpadComma,

		/// <summary>
		/// The key left of Right Shift on Brazilian (ABNT) and Japanese (JIS) keyboards. Corresponds to
		/// the / and ? key on ABNT, and the Ro key (or \ and _ in non-kana input) on JIS.
		/// </summary>
		International1,

		/// <summary>
		/// The Katakana/Hiragana key, right of Henkan on Japanese (JIS) keyboards.
		/// </summary>
		/// <remarks>
		/// The IME usually grabs this key; it exists for consistency with the other
		/// <c>International</c> keys.
		/// </remarks>
		International2,

		/// <summary>
		/// The Yen key, left of Backspace on Japanese (JIS) keyboards.
		/// </summary>
		International3,

		/// <summary>
		/// The Henkan key, right of Space on Japanese (JIS) keyboards.
		/// </summary>
		/// <inheritdoc cref="International2" path="/remarks"/>
		International4,

		/// <summary>
		/// The Muhenkan key, left of Space on Japanese (JIS) keyboards.
		/// </summary>
		/// <inheritdoc cref="International2" path="/remarks"/>
		International5,

		/// <summary>
		/// The Hangul/English key, right of Space on Korean keyboards, or the Kana key on Apple's
		/// Japanese (JIS) keyboards (same location).
		/// </summary>
		/// <remarks>
		/// The IME usually grabs this key.
		/// </remarks>
		Lang1,

		/// <summary>
		/// The Hanja key, left of Space on Korean keyboards, or the Eisu key on Apple's Japanese (JIS)
		/// keyboards (same location).
		/// </summary>
		/// <inheritdoc cref="Lang1" path="/remarks"/>
		Lang2,

		LeftCtrl,
		LeftShift,
		LeftAlt,

		/// <summary>
		/// Left Windows / Command / Super key.
		/// </summary>
		LeftGui,

		RightCtrl,
		RightShift,
		RightAlt,

		/// <summary>
		/// Right Windows / Command / Super key.
		/// </summary>
		RightGui,
	}
}

/// <summary>
/// A gamepad axis, in standard (Xbox-style) layout.
/// </summary>
/// <remarks>
/// <para>
/// Stick axes range over [-1, 1] with +X right and +Y down; triggers range over [0, 1].
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Unknown"/>.
/// </para>
/// </remarks>
[ClosedEnum]
public readonly partial struct GamepadAxis {
	/// <summary>Raw switch tag for <see cref="GamepadAxis"/>.</summary>
	public enum Case {
		/// <summary>
		/// A axis without an engine equivalent; can appear in host events, but
		/// <see cref="InputSystem"/> drops it.
		/// </summary>
		Unknown,

		LeftX, LeftY,
		RightX, RightY,
		LeftTrigger, RightTrigger,
	}
}

/// <summary>
/// A gamepad button, in standard (Xbox-style) layout.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Unknown"/>.
/// </remarks>
[ClosedEnum]
public readonly partial struct GamepadButton {
	/// <summary>Raw switch tag for <see cref="GamepadButton"/>.</summary>
	public enum Case {
		/// <summary>
		/// A button without an engine equivalent; can appear in host events, but
		/// <see cref="InputSystem"/> drops it.
		/// </summary>
		Unknown,

		/// <summary>
		/// Bottom face button, e.g. A on Xbox and the Steam Controller, Cross on PlayStation, B on
		/// Nintendo Switch, etc.
		/// </summary>
		South,

		/// <summary>
		/// Right face button, e.g. B on Xbox and the Steam Controller, Circle on PlayStation, A on
		/// Nintendo Switch, etc.
		/// </summary>
		East,

		/// <summary>
		/// Left face button, e.g. X on Xbox and the Steam Controller, Square on PlayStation, Y on
		/// Nintendo Switch, etc.
		/// </summary>
		West,

		/// <summary>
		/// Top face button, e.g. Y on Xbox and the Steam Controller, Triangle on PlayStation, X on
		/// Nintendo Switch, etc.
		/// </summary>
		North,

		Back, Guide, Start,
		LeftStick, RightStick,
		LeftShoulder, RightShoulder,
		DpadUp, DpadDown, DpadLeft, DpadRight,

		/// <summary>
		/// Extra button, e.g. Xbox Series X share button, PS5 microphone button, Nintendo Switch Pro
		/// capture button, Steam Controller QAM button, etc.
		/// </summary>
		Misc1,

		/// <summary>
		/// Upper/primary paddle under the user's right hand, e.g. Xbox Elite paddle P1, DualSense Edge RB
		/// button, Right Joy-Con SR button, Steam Controller R4 button, etc.
		/// </summary>
		RightPaddle1,

		/// <summary>
		/// Upper/primary paddle under the user's left hand, e.g. Xbox Elite paddle P3, DualSense Edge LB
		/// button, Left Joy-Con SL button, Steam Controller L4 button, etc.
		/// </summary>
		LeftPaddle1,

		/// <summary>
		/// Lower/secondary paddle under the user's right hand, e.g. Xbox Elite paddle P2, DualSense Edge
		/// right Fn button, Right Joy-Con SL button, Steam Controller R5 button, etc.
		/// </summary>
		RightPaddle2,

		/// <summary>
		/// Lower/secondary paddle under the user's left hand, e.g. Xbox Elite paddle P4, DualSense Edge
		/// left Fn button, Left Joy-Con SR button, Steam Controller L5 button, etc.
		/// </summary>
		LeftPaddle2,

		/// <summary>
		/// PS4/PS5 touchpad button.
		/// </summary>
		Touchpad,
	}
}

/// <summary>
/// A pointer (mouse) button.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="Key"/>, buttons are logical rather than physical: they are reported after the
/// OS applies its own button configuration. For example, with the OS's left-handed / swapped
/// buttons setting enabled, <see cref="Left"/> is the primary button, which is the physical right
/// button. Vertical mice need no special handling, as they report the index finger button as the
/// primary button, same as regular mice.
/// </para>
/// <para>
/// Mouse buttons past five are poorly standardized and usually vendor-specific; this only goes up
/// to five. macOS/evdev/X11/Wayland can represent them, but it's obscure as such extra mouse
/// buttons are nearly always bound to keys to be utilized as buttons, and Windows is incapable of
/// representing them in the first place (see
/// <see href="https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/keyboard-and-mouse-hid-client-drivers"/>
/// and, as a supplementary,
/// <see href="https://learn.microsoft.com/en-us/answers/questions/318599/mouse-with-more-than-5-buttons-is-hid-compilant"/>).
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Unknown"/>.
/// </para>
/// </remarks>
[ClosedEnum]
public readonly partial struct PointerButton {
	/// <summary>Raw switch tag for <see cref="PointerButton"/>.</summary>
	public enum Case {
		/// <summary>
		/// A button without an engine equivalent; can appear in host events, but
		/// <see cref="InputSystem"/> drops it.
		/// </summary>
		Unknown,

		/// <summary>
		/// The primary button; the physical left button unless the OS has the buttons swapped.
		/// </summary>
		Left,

		/// <summary>
		/// The secondary button; the physical right button unless the OS has the buttons swapped.
		/// </summary>
		Right,

		/// <summary>
		/// The middle button, usually a click of the scroll wheel.
		/// </summary>
		Middle,

		/// <summary>
		/// First extra button; nearly always the back side button / Mouse4.
		/// </summary>
		X1,

		/// <summary>
		/// Second extra button; nearly always the front side button / Mouse5.
		/// </summary>
		X2,
	}
}
