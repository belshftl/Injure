// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Hexa.NET.SDL3;
using Injure.Input;

namespace Injure.Sdl;

/// <summary>
/// SDL to engine input value mapping, used by <see cref="SdlEventSource"/>.
/// </summary>
internal static class SdlInputTranslation {
	public static Key TranslateScancode(SDLScancode scancode) => scancode switch {
		SDLScancode.A => Key.A,
		SDLScancode.B => Key.B,
		SDLScancode.C => Key.C,
		SDLScancode.D => Key.D,
		SDLScancode.E => Key.E,
		SDLScancode.F => Key.F,
		SDLScancode.G => Key.G,
		SDLScancode.H => Key.H,
		SDLScancode.I => Key.I,
		SDLScancode.J => Key.J,
		SDLScancode.K => Key.K,
		SDLScancode.L => Key.L,
		SDLScancode.M => Key.M,
		SDLScancode.N => Key.N,
		SDLScancode.O => Key.O,
		SDLScancode.P => Key.P,
		SDLScancode.Q => Key.Q,
		SDLScancode.R => Key.R,
		SDLScancode.S => Key.S,
		SDLScancode.T => Key.T,
		SDLScancode.U => Key.U,
		SDLScancode.V => Key.V,
		SDLScancode.W => Key.W,
		SDLScancode.X => Key.X,
		SDLScancode.Y => Key.Y,
		SDLScancode.Z => Key.Z,

		SDLScancode.Scancode1 => Key.Digit1,
		SDLScancode.Scancode2 => Key.Digit2,
		SDLScancode.Scancode3 => Key.Digit3,
		SDLScancode.Scancode4 => Key.Digit4,
		SDLScancode.Scancode5 => Key.Digit5,
		SDLScancode.Scancode6 => Key.Digit6,
		SDLScancode.Scancode7 => Key.Digit7,
		SDLScancode.Scancode8 => Key.Digit8,
		SDLScancode.Scancode9 => Key.Digit9,
		SDLScancode.Scancode0 => Key.Digit0,

		SDLScancode.Return => Key.Enter,
		SDLScancode.Escape => Key.Escape,
		SDLScancode.Backspace => Key.Backspace,
		SDLScancode.Tab => Key.Tab,
		SDLScancode.Space => Key.Space,

		SDLScancode.Minus => Key.Minus,
		SDLScancode.Equals => Key.Equal,
		SDLScancode.Leftbracket => Key.LeftBracket,
		SDLScancode.Rightbracket => Key.RightBracket,
		SDLScancode.Backslash => Key.Backslash,
		SDLScancode.Semicolon => Key.Semicolon,
		SDLScancode.Apostrophe => Key.Apostrophe,
		SDLScancode.Grave => Key.Grave,
		SDLScancode.Comma => Key.Comma,
		SDLScancode.Period => Key.Period,
		SDLScancode.Slash => Key.Slash,

		SDLScancode.Capslock => Key.Capslock,

		SDLScancode.F1 => Key.F1,
		SDLScancode.F2 => Key.F2,
		SDLScancode.F3 => Key.F3,
		SDLScancode.F4 => Key.F4,
		SDLScancode.F5 => Key.F5,
		SDLScancode.F6 => Key.F6,
		SDLScancode.F7 => Key.F7,
		SDLScancode.F8 => Key.F8,
		SDLScancode.F9 => Key.F9,
		SDLScancode.F10 => Key.F10,
		SDLScancode.F11 => Key.F11,
		SDLScancode.F12 => Key.F12,

		SDLScancode.Printscreen => Key.PrintScreen,
		SDLScancode.Scrolllock => Key.Scrolllock,
		SDLScancode.Pause => Key.Pause,

		SDLScancode.Insert => Key.Insert,
		SDLScancode.Home => Key.Home,
		SDLScancode.Pageup => Key.PageUp,
		SDLScancode.Delete => Key.Delete,
		SDLScancode.End => Key.End,
		SDLScancode.Pagedown => Key.PageDown,

		SDLScancode.Right => Key.Right,
		SDLScancode.Left => Key.Left,
		SDLScancode.Down => Key.Down,
		SDLScancode.Up => Key.Up,

		SDLScancode.Numlockclear => Key.NumlockClear,

		SDLScancode.KpDivide => Key.NumpadDivide,
		SDLScancode.KpMultiply => Key.NumpadMultiply,
		SDLScancode.KpMinus => Key.NumpadMinus,
		SDLScancode.KpPlus => Key.NumpadPlus,
		SDLScancode.KpEnter => Key.NumpadEnter,
		SDLScancode.Kp1 => Key.Numpad1,
		SDLScancode.Kp2 => Key.Numpad2,
		SDLScancode.Kp3 => Key.Numpad3,
		SDLScancode.Kp4 => Key.Numpad4,
		SDLScancode.Kp5 => Key.Numpad5,
		SDLScancode.Kp6 => Key.Numpad6,
		SDLScancode.Kp7 => Key.Numpad7,
		SDLScancode.Kp8 => Key.Numpad8,
		SDLScancode.Kp9 => Key.Numpad9,
		SDLScancode.Kp0 => Key.Numpad0,
		SDLScancode.KpPeriod => Key.NumpadPeriod,

		SDLScancode.Nonusbackslash => Key.NonUsBackslash,
		SDLScancode.Application => Key.Application,
		SDLScancode.KpEquals => Key.NumpadEquals,

		SDLScancode.F13 => Key.F13,
		SDLScancode.F14 => Key.F14,
		SDLScancode.F15 => Key.F15,
		SDLScancode.F16 => Key.F16,
		SDLScancode.F17 => Key.F17,
		SDLScancode.F18 => Key.F18,
		SDLScancode.F19 => Key.F19,
		SDLScancode.F20 => Key.F20,
		SDLScancode.F21 => Key.F21,
		SDLScancode.F22 => Key.F22,
		SDLScancode.F23 => Key.F23,
		SDLScancode.F24 => Key.F24,

		SDLScancode.KpComma => Key.NumpadComma,
		SDLScancode.International1 => Key.International1,
		SDLScancode.International2 => Key.International2,
		SDLScancode.International3 => Key.International3,
		SDLScancode.International4 => Key.International4,
		SDLScancode.International5 => Key.International5,
		SDLScancode.Lang1 => Key.Lang1,
		SDLScancode.Lang2 => Key.Lang2,

		SDLScancode.Lctrl => Key.LeftCtrl,
		SDLScancode.Lshift => Key.LeftShift,
		SDLScancode.Lalt => Key.LeftAlt,
		SDLScancode.Lgui => Key.LeftGui,
		SDLScancode.Rctrl => Key.RightCtrl,
		SDLScancode.Rshift => Key.RightShift,
		SDLScancode.Ralt => Key.RightAlt,
		SDLScancode.Rgui => Key.RightGui,

		_ => Key.Unknown,
	};

	public static GamepadAxis TranslateGamepadAxis(SDLGamepadAxis axis) => axis switch {
		SDLGamepadAxis.Leftx => GamepadAxis.LeftX,
		SDLGamepadAxis.Lefty => GamepadAxis.LeftY,
		SDLGamepadAxis.Rightx => GamepadAxis.RightX,
		SDLGamepadAxis.Righty => GamepadAxis.RightY,
		SDLGamepadAxis.LeftTrigger => GamepadAxis.LeftTrigger,
		SDLGamepadAxis.RightTrigger => GamepadAxis.RightTrigger,
		_ => GamepadAxis.Unknown,
	};

	public static GamepadButton TranslateGamepadButton(SDLGamepadButton button) => button switch {
		SDLGamepadButton.South => GamepadButton.South,
		SDLGamepadButton.East => GamepadButton.East,
		SDLGamepadButton.West => GamepadButton.West,
		SDLGamepadButton.North => GamepadButton.North,
		SDLGamepadButton.Back => GamepadButton.Back,
		SDLGamepadButton.Guide => GamepadButton.Guide,
		SDLGamepadButton.Start => GamepadButton.Start,
		SDLGamepadButton.LeftStick => GamepadButton.LeftStick,
		SDLGamepadButton.RightStick => GamepadButton.RightStick,
		SDLGamepadButton.LeftShoulder => GamepadButton.LeftShoulder,
		SDLGamepadButton.RightShoulder => GamepadButton.RightShoulder,
		SDLGamepadButton.DpadUp => GamepadButton.DpadUp,
		SDLGamepadButton.DpadDown => GamepadButton.DpadDown,
		SDLGamepadButton.DpadLeft => GamepadButton.DpadLeft,
		SDLGamepadButton.DpadRight => GamepadButton.DpadRight,
		SDLGamepadButton.Misc1 => GamepadButton.Misc1,
		SDLGamepadButton.RightPaddle1 => GamepadButton.RightPaddle1,
		SDLGamepadButton.RightPaddle2 => GamepadButton.RightPaddle2,
		SDLGamepadButton.LeftPaddle1 => GamepadButton.LeftPaddle1,
		SDLGamepadButton.LeftPaddle2 => GamepadButton.LeftPaddle2,
		SDLGamepadButton.Touchpad => GamepadButton.Touchpad,
		_ => GamepadButton.Unknown,
	};

	public static PointerButton TranslatePointerButton(byte button) => button switch {
		SDL.SDL_BUTTON_LEFT => PointerButton.Left,
		SDL.SDL_BUTTON_RIGHT => PointerButton.Right,
		SDL.SDL_BUTTON_MIDDLE => PointerButton.Middle,
		SDL.SDL_BUTTON_X1 => PointerButton.X1,
		SDL.SDL_BUTTON_X2 => PointerButton.X2,
		_ => PointerButton.Unknown,
	};

	public static float NormalizeGamepadAxis(GamepadAxis axis, short raw) => axis.Tag switch {
		GamepadAxis.Case.Unknown => throw new ArgumentException("unknown gamepad axis", nameof(axis)),
		GamepadAxis.Case.LeftX or GamepadAxis.Case.LeftY or GamepadAxis.Case.RightX or GamepadAxis.Case.RightY =>
			raw < 0 ? raw / 32768f : raw / 32767f,
		GamepadAxis.Case.LeftTrigger or GamepadAxis.Case.RightTrigger =>
			Math.Clamp(raw / 32767f, 0f, 1f),
		_ => throw new UnreachableException(),
	};
}
