// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Injure.DevAnalyzers.Attributes;

namespace Injure.Input;

/// <summary>
/// The kind of an <see cref="InputButtonSource"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct InputButtonSourceKind {
	/// <summary>Raw switch tag for <see cref="InputButtonSourceKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// A keyboard key.
		/// </summary>
		Key = 1,

		/// <summary>
		/// A gamepad button, on any gamepad.
		/// </summary>
		GamepadButton,

		/// <summary>
		/// A pointer button.
		/// </summary>
		PointerButton,
	}
}

/// <summary>
/// A button-like input: a key, a gamepad button (on any gamepad), or a pointer button.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly struct InputButtonSource : IEquatable<InputButtonSource> {
	private readonly Key key;
	private readonly GamepadButton gamepadButton;
	private readonly PointerButton pointerButton;

	/// <summary>Which kind of input this is.</summary>
	public InputButtonSourceKind Kind { get; }

	/// <summary>
	/// The key, if <see cref="Kind"/> is <see cref="InputButtonSourceKind.Key"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="InputButtonSourceKind.Key"/>.
	/// </exception>
	public Key KeyValue => Kind == InputButtonSourceKind.Key
		? key
		: throw new InvalidOperationException("input button source does not contain a key");

	/// <summary>
	/// The gamepad button, if <see cref="Kind"/> is <see cref="InputButtonSourceKind.GamepadButton"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="InputButtonSourceKind.GamepadButton"/>.
	/// </exception>
	public GamepadButton GamepadButtonValue => Kind == InputButtonSourceKind.GamepadButton
		? gamepadButton
		: throw new InvalidOperationException("input button source does not contain a gamepad button");

	/// <summary>
	/// The pointer button, if <see cref="Kind"/> is <see cref="InputButtonSourceKind.PointerButton"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="InputButtonSourceKind.PointerButton"/>.
	/// </exception>
	public PointerButton PointerButtonValue => Kind == InputButtonSourceKind.PointerButton
		? pointerButton
		: throw new InvalidOperationException("input button source does not contain a pointer button");

	private InputButtonSource(InputButtonSourceKind kind, Key key, PointerButton pointerButton, GamepadButton gamepadButton) {
		Kind = kind;
		this.key = key;
		this.pointerButton = pointerButton;
		this.gamepadButton = gamepadButton;
	}

	/// <summary>
	/// Creates a source for a keyboard key.
	/// </summary>
	public static InputButtonSource Key(Key key) =>
		new(InputButtonSourceKind.Key, key, default, default);

	/// <summary>
	/// Creates a source for a gamepad button, matching any gamepad.
	/// </summary>
	public static InputButtonSource GamepadButton(GamepadButton button) =>
		new(InputButtonSourceKind.GamepadButton, default, default, button);

	/// <summary>
	/// Creates a source for a pointer button.
	/// </summary>
	public static InputButtonSource PointerButton(PointerButton button) =>
		new(InputButtonSourceKind.PointerButton, default, button, default);

	/// <summary>
	/// Gets the key, if <see cref="Kind"/> is <see cref="InputButtonSourceKind.Key"/>.
	/// </summary>
	public bool TryGetKey(out Key value) {
		if (Kind == InputButtonSourceKind.Key) {
			value = key;
			return true;
		}
		value = default;
		return false;
	}

	/// <summary>
	/// Gets the pointer button, if <see cref="Kind"/> is
	/// <see cref="InputButtonSourceKind.PointerButton"/>.
	/// </summary>
	public bool TryGetPointerButton(out PointerButton value) {
		if (Kind == InputButtonSourceKind.PointerButton) {
			value = pointerButton;
			return true;
		}
		value = default;
		return false;
	}

	/// <summary>
	/// Gets the gamepad button, if <see cref="Kind"/> is
	/// <see cref="InputButtonSourceKind.GamepadButton"/>.
	/// </summary>
	public bool TryGetGamepadButton(out GamepadButton value) {
		if (Kind == InputButtonSourceKind.GamepadButton) {
			value = gamepadButton;
			return true;
		}
		value = default;
		return false;
	}

	/// <summary>Whether this and <paramref name="other"/> are the same value.</summary>
	public bool Equals(InputButtonSource other) => Kind == other.Kind && key == other.key && pointerButton == other.pointerButton && gamepadButton == other.gamepadButton;
	/// <summary>
	/// Equivalent to <see cref="Equals(InputButtonSource)"/> if <paramref name="obj"/> is a
	/// <see cref="InputButtonSource"/>; otherwise, <see langword="false"/>.
	/// </summary>
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is InputButtonSource other && Equals(other);
	/// <summary>Computes a hash consistent with <see cref="Equals(InputButtonSource)"/>.</summary>
	public override int GetHashCode() => HashCode.Combine(Kind, key, pointerButton, gamepadButton);
	/// <summary>Equivalent to <see cref="Equals(InputButtonSource)"/>.</summary>
	public static bool operator ==(InputButtonSource left, InputButtonSource right) => left.Equals(right);
	/// <summary>Equivalent to the negation of <see cref="Equals(InputButtonSource)"/>.</summary>
	public static bool operator !=(InputButtonSource left, InputButtonSource right) => !left.Equals(right);
}

/// <summary>
/// A 1D axis made of two button-like inputs: -1 while only <see cref="Negative"/> is held, +1 while
/// only <see cref="Positive"/> is held, 0 while neither is, and decided by <see cref="Socd"/> while
/// both are.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly struct DigitalAxisSource : IEquatable<DigitalAxisSource> {
	/// <summary>
	/// The input that drives the axis to -1.
	/// </summary>
	public InputButtonSource Negative { get; }

	/// <summary>
	/// The input that drives the axis to +1.
	/// </summary>
	public InputButtonSource Positive { get; }

	/// <summary>
	/// How both inputs being held at once is resolved.
	/// </summary>
	public SocdPolicy Socd { get; }

	/// <summary>
	/// Creates a digital axis that resolves both directions being held with
	/// <see cref="SocdPolicy.Last"/>.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="negative"/> and <paramref name="positive"/> are the same input.
	/// </exception>
	public DigitalAxisSource(InputButtonSource negative, InputButtonSource positive) : this(negative, positive, SocdPolicy.Last) {
	}

	/// <summary>
	/// Creates a digital axis.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="negative"/> and <paramref name="positive"/> are the same input.
	/// </exception>
	public DigitalAxisSource(InputButtonSource negative, InputButtonSource positive, SocdPolicy socd) {
		if (negative == positive)
			throw new ArgumentException("negative and positive sources must be different");
		Negative = negative;
		Positive = positive;
		Socd = socd;
	}

	/// <summary>Whether this and <paramref name="other"/> are the same value.</summary>
	public bool Equals(DigitalAxisSource other) => Negative == other.Negative && Positive == other.Positive && Socd == other.Socd;
	/// <summary>
	/// Equivalent to <see cref="Equals(DigitalAxisSource)"/> if <paramref name="obj"/> is a
	/// <see cref="DigitalAxisSource"/>; otherwise, <see langword="false"/>.
	/// </summary>
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is DigitalAxisSource other && Equals(other);
	/// <summary>Computes a hash consistent with <see cref="Equals(DigitalAxisSource)"/>.</summary>
	public override int GetHashCode() => HashCode.Combine(Negative, Positive, Socd);
	/// <summary>Equivalent to <see cref="Equals(DigitalAxisSource)"/>.</summary>
	public static bool operator ==(DigitalAxisSource left, DigitalAxisSource right) => left.Equals(right);
	/// <summary>Equivalent to the negation of <see cref="Equals(DigitalAxisSource)"/>.</summary>
	public static bool operator !=(DigitalAxisSource left, DigitalAxisSource right) => !left.Equals(right);
}

/// <summary>
/// The kind of an <see cref="InputStateAxisSource"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct InputStateAxisSourceKind {
	/// <summary>Raw switch tag for <see cref="InputStateAxisSourceKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// A gamepad axis, on whichever gamepad has it most deflected.
		/// </summary>
		GamepadAxis = 1,

		/// <summary>
		/// A <see cref="DigitalAxisSource"/>.
		/// </summary>
		DigitalPair,
	}
}

/// <summary>
/// An input with a continuous 1D value in [-1, 1]: a gamepad axis or a pair of button-like inputs.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly struct InputStateAxisSource : IEquatable<InputStateAxisSource> {
	private readonly GamepadAxis gamepadAxis;
	private readonly DigitalAxisSource digital;

	/// <summary>Which kind of input this is.</summary>
	public InputStateAxisSourceKind Kind { get; }

	/// <summary>
	/// The gamepad axis, if <see cref="Kind"/> is <see cref="InputStateAxisSourceKind.GamepadAxis"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="InputStateAxisSourceKind.GamepadAxis"/>.
	/// </exception>
	public GamepadAxis GamepadAxisValue => Kind == InputStateAxisSourceKind.GamepadAxis
		? gamepadAxis
		: throw new InvalidOperationException("input state-axis source does not contain a gamepad axis");

	/// <summary>
	/// The digital axis, if <see cref="Kind"/> is <see cref="InputStateAxisSourceKind.DigitalPair"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="InputStateAxisSourceKind.DigitalPair"/>.
	/// </exception>
	public DigitalAxisSource DigitalValue => Kind == InputStateAxisSourceKind.DigitalPair
		? digital
		: throw new InvalidOperationException("input state-axis source does not contain a digital pair");

	private InputStateAxisSource(InputStateAxisSourceKind kind, GamepadAxis gamepadAxis, DigitalAxisSource digital) {
		Kind = kind;
		this.gamepadAxis = gamepadAxis;
		this.digital = digital;
	}

	/// <summary>
	/// Creates a source for a gamepad axis, reading whichever gamepad has it most deflected.
	/// </summary>
	public static InputStateAxisSource GamepadAxis(GamepadAxis axis) =>
		new(InputStateAxisSourceKind.GamepadAxis, axis, default);

	/// <summary>
	/// Creates a source for a pair of button-like inputs; see <see cref="DigitalAxisSource"/>.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="negative"/> and <paramref name="positive"/> are the same input.
	/// </exception>
	public static InputStateAxisSource DigitalPair(InputButtonSource negative, InputButtonSource positive) =>
		DigitalPair(negative, positive, SocdPolicy.Last);

	/// <inheritdoc cref="DigitalPair(InputButtonSource, InputButtonSource)"/>
	public static InputStateAxisSource DigitalPair(InputButtonSource negative, InputButtonSource positive, SocdPolicy socd) =>
		new(InputStateAxisSourceKind.DigitalPair, default, new DigitalAxisSource(negative, positive, socd));

	/// <summary>Whether this and <paramref name="other"/> are the same value.</summary>
	public bool Equals(InputStateAxisSource other) => Kind == other.Kind && gamepadAxis == other.gamepadAxis && digital.Equals(other.digital);
	/// <summary>
	/// Equivalent to <see cref="Equals(InputStateAxisSource)"/> if <paramref name="obj"/> is a
	/// <see cref="InputStateAxisSource"/>; otherwise, <see langword="false"/>.
	/// </summary>
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is InputStateAxisSource other && Equals(other);
	/// <summary>Computes a hash consistent with <see cref="Equals(InputStateAxisSource)"/>.</summary>
	public override int GetHashCode() => HashCode.Combine(Kind, gamepadAxis, digital);
	/// <summary>Equivalent to <see cref="Equals(InputStateAxisSource)"/>.</summary>
	public static bool operator ==(InputStateAxisSource left, InputStateAxisSource right) => left.Equals(right);
	/// <summary>Equivalent to the negation of <see cref="Equals(InputStateAxisSource)"/>.</summary>
	public static bool operator !=(InputStateAxisSource left, InputStateAxisSource right) => !left.Equals(right);
}

/// <summary>
/// The kind of an <see cref="InputStateAxis2dSource"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct InputStateAxis2dSourceKind {
	/// <summary>Raw switch tag for <see cref="InputStateAxis2dSourceKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// A gamepad stick, on whichever gamepad has it most deflected.
		/// </summary>
		GamepadStick = 1,

		/// <summary>
		/// A <see cref="DigitalAxis2dSource"/>.
		/// </summary>
		DigitalButtons,

		/// <summary>
		/// A <see cref="StateAxis2dPairSource"/>.
		/// </summary>
		Pair,
	}
}

/// <summary>
/// A gamepad stick.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct GamepadStick {
	/// <summary>Raw switch tag for <see cref="GamepadStick"/>.</summary>
	public enum Case {
		/// <summary>The left stick.</summary>
		Left = 1,

		/// <summary>The right stick.</summary>
		Right,
	}
}

/// <summary>
/// A 2D axis made of four button-like inputs, like a D-pad or WASD. Each dimension works like a
/// <see cref="DigitalAxisSource"/>, with <see cref="Up"/> being -Y; the result is clamped to a
/// magnitude of at most 1, so diagonals are normalized.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly struct DigitalAxis2dSource : IEquatable<DigitalAxis2dSource> {
	/// <summary>
	/// The input that drives X to -1.
	/// </summary>
	public InputButtonSource Left { get; }

	/// <summary>
	/// The input that drives X to +1.
	/// </summary>
	public InputButtonSource Right { get; }

	/// <summary>
	/// The input that drives Y to -1.
	/// </summary>
	public InputButtonSource Up { get; }

	/// <summary>
	/// The input that drives Y to +1.
	/// </summary>
	public InputButtonSource Down { get; }

	/// <summary>
	/// How <see cref="Left"/> and <see cref="Right"/> being held at once is resolved.
	/// </summary>
	public SocdPolicy XSocd { get; }

	/// <summary>
	/// How <see cref="Up"/> and <see cref="Down"/> being held at once is resolved.
	/// </summary>
	public SocdPolicy YSocd { get; }

	/// <summary>
	/// Creates a digital 2D axis that resolves opposing directions with <see cref="SocdPolicy.Last"/>.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="left"/> and <paramref name="right"/>, or <paramref name="up"/> and
	/// <paramref name="down"/>, are the same input.
	/// </exception>
	public DigitalAxis2dSource(InputButtonSource left, InputButtonSource right, InputButtonSource up, InputButtonSource down) :
		this(left, right, up, down, SocdPolicy.Last, SocdPolicy.Last) {
	}

	/// <summary>
	/// Creates a digital 2D axis.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="left"/> and <paramref name="right"/>, or <paramref name="up"/> and
	/// <paramref name="down"/>, are the same input.
	/// </exception>
	public DigitalAxis2dSource(
		InputButtonSource left,
		InputButtonSource right,
		InputButtonSource up,
		InputButtonSource down,
		SocdPolicy xSocd,
		SocdPolicy ySocd
	) {
		if (left == right)
			throw new ArgumentException("left and right sources must be different");
		if (up == down)
			throw new ArgumentException("up and down sources must be different");
		Left = left;
		Right = right;
		Up = up;
		Down = down;
		XSocd = xSocd;
		YSocd = ySocd;
	}

	/// <summary>Whether this and <paramref name="other"/> are the same value.</summary>
	public bool Equals(DigitalAxis2dSource other) =>
		Left == other.Left && Right == other.Right && Up == other.Up && Down == other.Down &&
		XSocd == other.XSocd && YSocd == other.YSocd;
	/// <summary>
	/// Equivalent to <see cref="Equals(DigitalAxis2dSource)"/> if <paramref name="obj"/> is a
	/// <see cref="DigitalAxis2dSource"/>; otherwise, <see langword="false"/>.
	/// </summary>
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is DigitalAxis2dSource other && Equals(other);
	/// <summary>Computes a hash consistent with <see cref="Equals(DigitalAxis2dSource)"/>.</summary>
	public override int GetHashCode() => HashCode.Combine(Left, Right, Up, Down, XSocd, YSocd);
	/// <summary>Equivalent to <see cref="Equals(DigitalAxis2dSource)"/>.</summary>
	public static bool operator ==(DigitalAxis2dSource left, DigitalAxis2dSource right) => left.Equals(right);
	/// <summary>Equivalent to the negation of <see cref="Equals(DigitalAxis2dSource)"/>.</summary>
	public static bool operator !=(DigitalAxis2dSource left, DigitalAxis2dSource right) => !left.Equals(right);
}

/// <summary>
/// A 2D axis made of two independent 1D inputs, one per dimension.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly struct StateAxis2dPairSource(InputStateAxisSource x, InputStateAxisSource y) : IEquatable<StateAxis2dPairSource> {
	/// <summary>
	/// The input for the X dimension.
	/// </summary>
	public InputStateAxisSource X { get; } = x;

	/// <summary>
	/// The input for the Y dimension.
	/// </summary>
	public InputStateAxisSource Y { get; } = y;

	/// <summary>Whether this and <paramref name="other"/> are the same value.</summary>
	public bool Equals(StateAxis2dPairSource other) => X == other.X && Y == other.Y;
	/// <summary>
	/// Equivalent to <see cref="Equals(StateAxis2dPairSource)"/> if <paramref name="obj"/> is a
	/// <see cref="StateAxis2dPairSource"/>; otherwise, <see langword="false"/>.
	/// </summary>
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is StateAxis2dPairSource other && Equals(other);
	/// <summary>Computes a hash consistent with <see cref="Equals(StateAxis2dPairSource)"/>.</summary>
	public override int GetHashCode() => HashCode.Combine(X, Y);
	/// <summary>Equivalent to <see cref="Equals(StateAxis2dPairSource)"/>.</summary>
	public static bool operator ==(StateAxis2dPairSource left, StateAxis2dPairSource right) => left.Equals(right);
	/// <summary>Equivalent to the negation of <see cref="Equals(StateAxis2dPairSource)"/>.</summary>
	public static bool operator !=(StateAxis2dPairSource left, StateAxis2dPairSource right) => !left.Equals(right);
}

/// <summary>
/// An input with a continuous 2D value of magnitude at most 1: a gamepad stick, four button-like
/// inputs, or two 1D inputs.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly struct InputStateAxis2dSource : IEquatable<InputStateAxis2dSource> {
	private readonly GamepadStick stick;
	private readonly DigitalAxis2dSource digital;
	private readonly StateAxis2dPairSource pair;

	/// <summary>Which kind of input this is.</summary>
	public InputStateAxis2dSourceKind Kind { get; }

	/// <summary>
	/// The stick, if <see cref="Kind"/> is <see cref="InputStateAxis2dSourceKind.GamepadStick"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="InputStateAxis2dSourceKind.GamepadStick"/>.
	/// </exception>
	public GamepadStick GamepadStickValue => Kind == InputStateAxis2dSourceKind.GamepadStick
		? stick
		: throw new InvalidOperationException("2D state-axis source does not contain a gamepad stick");

	/// <summary>
	/// The digital axis, if <see cref="Kind"/> is
	/// <see cref="InputStateAxis2dSourceKind.DigitalButtons"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="InputStateAxis2dSourceKind.DigitalButtons"/>.
	/// </exception>
	public DigitalAxis2dSource DigitalValue => Kind == InputStateAxis2dSourceKind.DigitalButtons
		? digital
		: throw new InvalidOperationException("2D state-axis source does not contain digital buttons");

	/// <summary>
	/// The axis pair, if <see cref="Kind"/> is <see cref="InputStateAxis2dSourceKind.Pair"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="InputStateAxis2dSourceKind.Pair"/>.
	/// </exception>
	public StateAxis2dPairSource PairValue => Kind == InputStateAxis2dSourceKind.Pair
		? pair
		: throw new InvalidOperationException("2D state-axis source does not contain an axis pair");

	private InputStateAxis2dSource(
		InputStateAxis2dSourceKind kind,
		GamepadStick stick,
		DigitalAxis2dSource digital,
		StateAxis2dPairSource pair
	) {
		Kind = kind;
		this.stick = stick;
		this.digital = digital;
		this.pair = pair;
	}

	/// <summary>
	/// Creates a source for a gamepad stick, reading whichever gamepad has it most deflected.
	/// </summary>
	public static InputStateAxis2dSource GamepadStick(GamepadStick stick) =>
		new(InputStateAxis2dSourceKind.GamepadStick, stick, default, default);

	/// <summary>
	/// Creates a source for four button-like inputs; see <see cref="DigitalAxis2dSource"/>.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="left"/> and <paramref name="right"/>, or <paramref name="up"/> and
	/// <paramref name="down"/>, are the same input.
	/// </exception>
	public static InputStateAxis2dSource DigitalButtons(
		InputButtonSource left,
		InputButtonSource right,
		InputButtonSource up,
		InputButtonSource down
	) => DigitalButtons(left, right, up, down, SocdPolicy.Last, SocdPolicy.Last);

	/// <inheritdoc cref="DigitalButtons(InputButtonSource, InputButtonSource, InputButtonSource, InputButtonSource)"/>
	public static InputStateAxis2dSource DigitalButtons(
		InputButtonSource left,
		InputButtonSource right,
		InputButtonSource up,
		InputButtonSource down,
		SocdPolicy xSocd,
		SocdPolicy ySocd
	) => new(InputStateAxis2dSourceKind.DigitalButtons, default, new DigitalAxis2dSource(left, right, up, down, xSocd, ySocd), default);

	/// <summary>
	/// Creates a source from two 1D inputs; see <see cref="StateAxis2dPairSource"/>.
	/// </summary>
	public static InputStateAxis2dSource Pair(InputStateAxisSource x, InputStateAxisSource y) =>
		new(InputStateAxis2dSourceKind.Pair, default, default, new StateAxis2dPairSource(x, y));

	/// <summary>Whether this and <paramref name="other"/> are the same value.</summary>
	public bool Equals(InputStateAxis2dSource other) => Kind == other.Kind && stick == other.stick && digital.Equals(other.digital) && pair.Equals(other.pair);
	/// <summary>
	/// Equivalent to <see cref="Equals(InputStateAxis2dSource)"/> if <paramref name="obj"/> is a
	/// <see cref="InputStateAxis2dSource"/>; otherwise, <see langword="false"/>.
	/// </summary>
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is InputStateAxis2dSource other && Equals(other);
	/// <summary>
	/// Computes a hash consistent with <see cref="Equals(InputStateAxis2dSource)"/>.
	/// </summary>
	public override int GetHashCode() => HashCode.Combine(Kind, stick, digital, pair);
	/// <summary>Equivalent to <see cref="Equals(InputStateAxis2dSource)"/>.</summary>
	public static bool operator ==(InputStateAxis2dSource left, InputStateAxis2dSource right) => left.Equals(right);
	/// <summary>Equivalent to the negation of <see cref="Equals(InputStateAxis2dSource)"/>.</summary>
	public static bool operator !=(InputStateAxis2dSource left, InputStateAxis2dSource right) => !left.Equals(right);
}

/// <summary>
/// A pointer wheel dimension.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct PointerWheelAxis {
	/// <summary>Raw switch tag for <see cref="PointerWheelAxis"/>.</summary>
	public enum Case {
		/// <summary>
		/// Horizontal scrolling; positive is to the right.
		/// </summary>
		X = 1,

		/// <summary>
		/// Vertical scrolling; positive is away from the user.
		/// </summary>
		Y,
	}
}

/// <summary>
/// The kind of an <see cref="InputImpulseAxisSource"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct InputImpulseAxisSourceKind {
	/// <summary>Raw switch tag for <see cref="InputImpulseAxisSourceKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// A pointer wheel dimension.
		/// </summary>
		PointerWheel = 1,
	}
}

/// <summary>
/// An input that produces discrete amounts instead of a held value, such as pointer wheel scrolling.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly struct InputImpulseAxisSource : IEquatable<InputImpulseAxisSource> {
	private readonly PointerWheelAxis wheelAxis;

	/// <summary>Which kind of input this is.</summary>
	public InputImpulseAxisSourceKind Kind { get; }

	/// <summary>
	/// The wheel dimension, if <see cref="Kind"/> is
	/// <see cref="InputImpulseAxisSourceKind.PointerWheel"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="InputImpulseAxisSourceKind.PointerWheel"/>.
	/// </exception>
	public PointerWheelAxis PointerWheelAxisValue => Kind == InputImpulseAxisSourceKind.PointerWheel
		? wheelAxis
		: throw new InvalidOperationException("input impulse-axis source does not contain a pointer wheel axis");

	private InputImpulseAxisSource(InputImpulseAxisSourceKind kind, PointerWheelAxis wheelAxis) {
		Kind = kind;
		this.wheelAxis = wheelAxis;
	}

	/// <summary>
	/// Creates a source for a pointer wheel dimension.
	/// </summary>
	public static InputImpulseAxisSource PointerWheel(PointerWheelAxis axis) =>
		new(InputImpulseAxisSourceKind.PointerWheel, axis);

	/// <summary>Whether this and <paramref name="other"/> are the same value.</summary>
	public bool Equals(InputImpulseAxisSource other) => Kind == other.Kind && wheelAxis == other.wheelAxis;
	/// <summary>
	/// Equivalent to <see cref="Equals(InputImpulseAxisSource)"/> if <paramref name="obj"/> is a
	/// <see cref="InputImpulseAxisSource"/>; otherwise, <see langword="false"/>.
	/// </summary>
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is InputImpulseAxisSource other && Equals(other);
	/// <summary>
	/// Computes a hash consistent with <see cref="Equals(InputImpulseAxisSource)"/>.
	/// </summary>
	public override int GetHashCode() => HashCode.Combine(Kind, wheelAxis);
	/// <summary>Equivalent to <see cref="Equals(InputImpulseAxisSource)"/>.</summary>
	public static bool operator ==(InputImpulseAxisSource left, InputImpulseAxisSource right) => left.Equals(right);
	/// <summary>Equivalent to the negation of <see cref="Equals(InputImpulseAxisSource)"/>.</summary>
	public static bool operator !=(InputImpulseAxisSource left, InputImpulseAxisSource right) => !left.Equals(right);
}
