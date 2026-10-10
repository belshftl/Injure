// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using SDL3;
using Injure.Input;

namespace Injure.Sdl;

/// <summary>
/// SDL to engine input value mapping, used by <see cref="SdlEventSource"/>.
/// </summary>
internal static class SdlInputTranslation {
	public static Key TranslateScancode(SDL.Scancode scancode) => scancode switch {
		SDL.Scancode.A => Key.A,
		SDL.Scancode.B => Key.B,
		SDL.Scancode.C => Key.C,
		SDL.Scancode.D => Key.D,
		SDL.Scancode.E => Key.E,
		SDL.Scancode.F => Key.F,
		SDL.Scancode.G => Key.G,
		SDL.Scancode.H => Key.H,
		SDL.Scancode.I => Key.I,
		SDL.Scancode.J => Key.J,
		SDL.Scancode.K => Key.K,
		SDL.Scancode.L => Key.L,
		SDL.Scancode.M => Key.M,
		SDL.Scancode.N => Key.N,
		SDL.Scancode.O => Key.O,
		SDL.Scancode.P => Key.P,
		SDL.Scancode.Q => Key.Q,
		SDL.Scancode.R => Key.R,
		SDL.Scancode.S => Key.S,
		SDL.Scancode.T => Key.T,
		SDL.Scancode.U => Key.U,
		SDL.Scancode.V => Key.V,
		SDL.Scancode.W => Key.W,
		SDL.Scancode.X => Key.X,
		SDL.Scancode.Y => Key.Y,
		SDL.Scancode.Z => Key.Z,

		SDL.Scancode.Alpha1 => Key.Digit1,
		SDL.Scancode.Alpha2 => Key.Digit2,
		SDL.Scancode.Alpha3 => Key.Digit3,
		SDL.Scancode.Alpha4 => Key.Digit4,
		SDL.Scancode.Alpha5 => Key.Digit5,
		SDL.Scancode.Alpha6 => Key.Digit6,
		SDL.Scancode.Alpha7 => Key.Digit7,
		SDL.Scancode.Alpha8 => Key.Digit8,
		SDL.Scancode.Alpha9 => Key.Digit9,
		SDL.Scancode.Alpha0 => Key.Digit0,

		SDL.Scancode.Return => Key.Enter,
		SDL.Scancode.Escape => Key.Escape,
		SDL.Scancode.Backspace => Key.Backspace,
		SDL.Scancode.Tab => Key.Tab,
		SDL.Scancode.Space => Key.Space,

		SDL.Scancode.Minus => Key.Minus,
		SDL.Scancode.Equals => Key.Equal,
		SDL.Scancode.Leftbracket => Key.LeftBracket,
		SDL.Scancode.Rightbracket => Key.RightBracket,
		SDL.Scancode.Backslash => Key.Backslash,
		SDL.Scancode.Semicolon => Key.Semicolon,
		SDL.Scancode.Apostrophe => Key.Apostrophe,
		SDL.Scancode.Grave => Key.Grave,
		SDL.Scancode.Comma => Key.Comma,
		SDL.Scancode.Period => Key.Period,
		SDL.Scancode.Slash => Key.Slash,

		SDL.Scancode.Capslock => Key.Capslock,

		SDL.Scancode.F1 => Key.F1,
		SDL.Scancode.F2 => Key.F2,
		SDL.Scancode.F3 => Key.F3,
		SDL.Scancode.F4 => Key.F4,
		SDL.Scancode.F5 => Key.F5,
		SDL.Scancode.F6 => Key.F6,
		SDL.Scancode.F7 => Key.F7,
		SDL.Scancode.F8 => Key.F8,
		SDL.Scancode.F9 => Key.F9,
		SDL.Scancode.F10 => Key.F10,
		SDL.Scancode.F11 => Key.F11,
		SDL.Scancode.F12 => Key.F12,

		SDL.Scancode.Printscreen => Key.PrintScreen,
		SDL.Scancode.Scrolllock => Key.Scrolllock,
		SDL.Scancode.Pause => Key.Pause,

		SDL.Scancode.Insert => Key.Insert,
		SDL.Scancode.Home => Key.Home,
		SDL.Scancode.Pageup => Key.PageUp,
		SDL.Scancode.Delete => Key.Delete,
		SDL.Scancode.End => Key.End,
		SDL.Scancode.Pagedown => Key.PageDown,

		SDL.Scancode.Right => Key.Right,
		SDL.Scancode.Left => Key.Left,
		SDL.Scancode.Down => Key.Down,
		SDL.Scancode.Up => Key.Up,

		SDL.Scancode.NumLockClear => Key.NumlockClear,

		SDL.Scancode.KpDivide => Key.NumpadDivide,
		SDL.Scancode.KpMultiply => Key.NumpadMultiply,
		SDL.Scancode.KpMinus => Key.NumpadMinus,
		SDL.Scancode.KpPlus => Key.NumpadPlus,
		SDL.Scancode.KpEnter => Key.NumpadEnter,
		SDL.Scancode.Kp1 => Key.Numpad1,
		SDL.Scancode.Kp2 => Key.Numpad2,
		SDL.Scancode.Kp3 => Key.Numpad3,
		SDL.Scancode.Kp4 => Key.Numpad4,
		SDL.Scancode.Kp5 => Key.Numpad5,
		SDL.Scancode.Kp6 => Key.Numpad6,
		SDL.Scancode.Kp7 => Key.Numpad7,
		SDL.Scancode.Kp8 => Key.Numpad8,
		SDL.Scancode.Kp9 => Key.Numpad9,
		SDL.Scancode.Kp0 => Key.Numpad0,
		SDL.Scancode.KpPeriod => Key.NumpadPeriod,

		SDL.Scancode.NonUsBackSlash => Key.NonUsBackslash,
		SDL.Scancode.Application => Key.Application,
		SDL.Scancode.KpEquals => Key.NumpadEquals,

		SDL.Scancode.F13 => Key.F13,
		SDL.Scancode.F14 => Key.F14,
		SDL.Scancode.F15 => Key.F15,
		SDL.Scancode.F16 => Key.F16,
		SDL.Scancode.F17 => Key.F17,
		SDL.Scancode.F18 => Key.F18,
		SDL.Scancode.F19 => Key.F19,
		SDL.Scancode.F20 => Key.F20,
		SDL.Scancode.F21 => Key.F21,
		SDL.Scancode.F22 => Key.F22,
		SDL.Scancode.F23 => Key.F23,
		SDL.Scancode.F24 => Key.F24,

		SDL.Scancode.KpComma => Key.NumpadComma,
		SDL.Scancode.International1 => Key.International1,
		SDL.Scancode.International2 => Key.International2,
		SDL.Scancode.International3 => Key.International3,
		SDL.Scancode.International4 => Key.International4,
		SDL.Scancode.International5 => Key.International5,
		SDL.Scancode.Lang1 => Key.Lang1,
		SDL.Scancode.Lang2 => Key.Lang2,

		SDL.Scancode.LCtrl => Key.LeftCtrl,
		SDL.Scancode.LShift => Key.LeftShift,
		SDL.Scancode.LAlt => Key.LeftAlt,
		SDL.Scancode.LGUI => Key.LeftGui,
		SDL.Scancode.RCtrl => Key.RightCtrl,
		SDL.Scancode.RShift => Key.RightShift,
		SDL.Scancode.RAlt => Key.RightAlt,
		SDL.Scancode.RGUI => Key.RightGui,

		_ => Key.Unknown,
	};

	public static GamepadAxis TranslateGamepadAxis(SDL.GamepadAxis axis) => axis switch {
		SDL.GamepadAxis.LeftX => GamepadAxis.LeftX,
		SDL.GamepadAxis.LeftY => GamepadAxis.LeftY,
		SDL.GamepadAxis.RightX => GamepadAxis.RightX,
		SDL.GamepadAxis.RightY => GamepadAxis.RightY,
		SDL.GamepadAxis.LeftTrigger => GamepadAxis.LeftTrigger,
		SDL.GamepadAxis.RightTrigger => GamepadAxis.RightTrigger,
		_ => GamepadAxis.Unknown,
	};

	public static GamepadButton TranslateGamepadButton(SDL.GamepadButton button) => button switch {
		SDL.GamepadButton.South => GamepadButton.South,
		SDL.GamepadButton.East => GamepadButton.East,
		SDL.GamepadButton.West => GamepadButton.West,
		SDL.GamepadButton.North => GamepadButton.North,
		SDL.GamepadButton.Back => GamepadButton.Back,
		SDL.GamepadButton.Guide => GamepadButton.Guide,
		SDL.GamepadButton.Start => GamepadButton.Start,
		SDL.GamepadButton.LeftStick => GamepadButton.LeftStick,
		SDL.GamepadButton.RightStick => GamepadButton.RightStick,
		SDL.GamepadButton.LeftShoulder => GamepadButton.LeftShoulder,
		SDL.GamepadButton.RightShoulder => GamepadButton.RightShoulder,
		SDL.GamepadButton.DPadUp => GamepadButton.DpadUp,
		SDL.GamepadButton.DPadDown => GamepadButton.DpadDown,
		SDL.GamepadButton.DPadLeft => GamepadButton.DpadLeft,
		SDL.GamepadButton.DPadRight => GamepadButton.DpadRight,
		SDL.GamepadButton.Misc1 => GamepadButton.Misc1,
		SDL.GamepadButton.RightPaddle1 => GamepadButton.RightPaddle1,
		SDL.GamepadButton.RightPaddle2 => GamepadButton.RightPaddle2,
		SDL.GamepadButton.LeftPaddle1 => GamepadButton.LeftPaddle1,
		SDL.GamepadButton.LeftPaddle2 => GamepadButton.LeftPaddle2,
		SDL.GamepadButton.Touchpad => GamepadButton.Touchpad,
		_ => GamepadButton.Unknown,
	};

	public static PointerButton TranslatePointerButton(byte button) => button switch {
		SDL.ButtonLeft => PointerButton.Left,
		SDL.ButtonRight => PointerButton.Right,
		SDL.ButtonMiddle => PointerButton.Middle,
		SDL.ButtonX1 => PointerButton.X1,
		SDL.ButtonX2 => PointerButton.X2,
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
