// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Input;
using static Injure.Tests.Input.Ev;

namespace Injure.Tests.Input;

public sealed class InputValueTypeTests {
	// ==========================================================================
	// state types
	[Fact]
	public static void KeyboardStateReportsHeldKeysAndModifiers() {
		KeyboardState s = new(Key.RightCtrl, Key.Z);
		Assert.True(s.IsDown(Key.Z));
		Assert.True(s.Ctrl);
		Assert.False(s.Shift);
		Assert.False(s.IsDown(Key.LeftCtrl));
		Assert.False(KeyboardState.Rest.IsDown(Key.Z));
		Assert.False(default(KeyboardState).Gui);
	}

	[Fact]
	public static void GamepadStateValidatesRanges() {
		Assert.Throws<ArgumentOutOfRangeException>(static () => new GamepadState([], 1.5f, 0, 0, 0, 0, 0));
		Assert.Throws<ArgumentOutOfRangeException>(static () => new GamepadState([], 0, 0, 0, 0, -0.1f, 0));
		GamepadState s = new([GamepadButton.DpadLeft], -1f, 1f, 0, 0, 0.5f, 1f);
		Assert.True(s.IsDown(GamepadButton.DpadLeft));
		Assert.Equal(1f, s.GetAxis(GamepadAxis.LeftY));
		Assert.Equal(0.5f, s.GetAxis(GamepadAxis.LeftTrigger));
		Assert.Equal(0f, s.GetAxis(GamepadAxis.Unknown));
	}

	[Fact]
	public static void GamepadStateSetLooksUpByIdAndRejectsDuplicates() {
		var a = GamepadId.Allocate();
		var b = GamepadId.Allocate();
		GamepadState held = new([GamepadButton.North], 0, 0, 0, 0, 0, 0);
		GamepadStateSet set = new(new GamepadStateEntry(a, held));
		Assert.True(set.TryGetState(a, out GamepadState got));
		Assert.True(got.IsDown(GamepadButton.North));
		Assert.False(set.TryGetState(b, out GamepadState missing));
		Assert.False(missing.IsDown(GamepadButton.North));
		Assert.Empty(default(GamepadStateSet));
		Assert.Throws<ArgumentException>(() => new GamepadStateSet(new GamepadStateEntry(a, held), new GamepadStateEntry(a, held)));
	}

	[Fact]
	public static void GamepadIdsAreUniqueAndDefaultIsInvalid() {
		Assert.False(default(GamepadId).IsValid);
		var a = GamepadId.Allocate();
		Assert.True(a.IsValid);
		Assert.NotEqual(a, GamepadId.Allocate());
	}

	// ==========================================================================
	// source types
	[Fact]
	public static void ButtonSourceAccessorsMatchKind() {
		var k = InputButtonSource.Key(Key.Q);
		Assert.Equal(InputButtonSourceKind.Key, k.Kind);
		Assert.Equal(Key.Q, k.KeyValue);
		Assert.True(k.TryGetKey(out Key key));
		Assert.Equal(Key.Q, key);
		Assert.False(k.TryGetGamepadButton(out _));
		Assert.Throws<InvalidOperationException>(() => k.GamepadButtonValue);
		Assert.Throws<InvalidOperationException>(() => k.PointerButtonValue);
	}

	[Fact]
	public static void SourcesCompareStructurally() {
		Assert.Equal(InputButtonSource.Key(Key.A), InputButtonSource.Key(Key.A));
		Assert.NotEqual(InputButtonSource.Key(Key.A), InputButtonSource.Key(Key.B));
		// same underlying tag value, different kind
		Assert.NotEqual(InputButtonSource.PointerButton(PointerButton.Left), InputButtonSource.GamepadButton(GamepadButton.South));
		Assert.NotEqual(
			InputStateAxisSource.DigitalPair(InputButtonSource.Key(Key.A), InputButtonSource.Key(Key.D), SocdPolicy.Last),
			InputStateAxisSource.DigitalPair(InputButtonSource.Key(Key.A), InputButtonSource.Key(Key.D), SocdPolicy.First)
		);
	}

	[Fact]
	public static void DigitalSourcesRejectIdenticalDirections() {
		var a = InputButtonSource.Key(Key.A);
		var d = InputButtonSource.Key(Key.D);
		Assert.Throws<ArgumentException>(() => new DigitalAxisSource(a, a));
		Assert.Throws<ArgumentException>(() => InputStateAxisSource.DigitalPair(a, a));
		Assert.Throws<ArgumentException>(() => new DigitalAxis2dSource(a, a, InputButtonSource.Key(Key.W), InputButtonSource.Key(Key.S)));
		Assert.Throws<ArgumentException>(() => InputStateAxis2dSource.DigitalButtons(a, d, d, d));
	}

	[Fact]
	public static void StateAxisSourceAccessorsMatchKind() {
		var ga = InputStateAxisSource.GamepadAxis(GamepadAxis.RightX);
		Assert.Equal(GamepadAxis.RightX, ga.GamepadAxisValue);
		Assert.Throws<InvalidOperationException>(() => ga.DigitalValue);
		var stick = InputStateAxis2dSource.GamepadStick(GamepadStick.Right);
		Assert.Equal(GamepadStick.Right, stick.GamepadStickValue);
		Assert.Throws<InvalidOperationException>(() => stick.PairValue);
		Assert.Throws<InvalidOperationException>(() => stick.DigitalValue);
		var wheel = InputImpulseAxisSource.PointerWheel(PointerWheelAxis.Y);
		Assert.Equal(PointerWheelAxis.Y, wheel.PointerWheelAxisValue);
	}

	// ==========================================================================
	// deadzones
	[Fact]
	public static void AxisDeadzoneNonePassesThrough() {
		Assert.Equal(2f, AxisDeadzone.None.Apply(2f));
		Assert.Equal(-0.01f, default(AxisDeadzone).Apply(-0.01f));
	}

	[Fact]
	public static void AxisDeadzoneThresholdZeroesInsideAndClampsOutside() {
		var dz = AxisDeadzone.Threshold(0.2f);
		Assert.Equal(0f, dz.Apply(0.2f));
		Assert.Equal(0f, dz.Apply(-0.1f));
		Assert.Equal(-0.5f, dz.Apply(-0.5f));
		Assert.Equal(1f, dz.Apply(1.5f));
	}

	[Fact]
	public static void AxisDeadzoneScaledRescales() {
		var dz = AxisDeadzone.Scaled(0.2f, 0.8f);
		Assert.Equal(0f, dz.Apply(0.2f));
		Near(0.5f, dz.Apply(0.5f));
		Near(-1f, dz.Apply(-0.8f));
		Assert.Equal(1f, dz.Apply(0.95f));
	}

	[Theory]
	[InlineData(-0.1f, 1f)]
	[InlineData(1f, 1f)]
	[InlineData(0.5f, 0.5f)] // outer must be above inner
	[InlineData(0.5f, 0.4f)]
	[InlineData(0.2f, 1.1f)]
	[InlineData(float.NaN, 1f)]
	[InlineData(0.2f, float.NaN)]
	[InlineData(0.2f, float.PositiveInfinity)]
	public static void DeadzoneFactoriesRejectInvalidBounds(float inner, float outer) {
		Assert.Throws<ArgumentOutOfRangeException>(() => AxisDeadzone.Threshold(inner, outer));
		Assert.Throws<ArgumentOutOfRangeException>(() => AxisDeadzone.Scaled(inner, outer));
		Assert.Throws<ArgumentOutOfRangeException>(() => Axis2dDeadzone.Radial(inner, outer));
		Assert.Throws<ArgumentOutOfRangeException>(() => Axis2dDeadzone.ScaledRadial(inner, outer));
		Assert.Throws<ArgumentOutOfRangeException>(() => Axis2dDeadzone.Axial(inner, outer));
		Assert.Throws<ArgumentOutOfRangeException>(() => Axis2dDeadzone.ScaledAxial(inner, outer));
	}

	[Fact]
	public static void DeadzoneBoundaryValuesAreAccepted() {
		Assert.Equal(0.5f, AxisDeadzone.Threshold(0f).Apply(0.5f)); // inner 0: only exact 0 is zeroed
		Assert.Equal(0f, AxisDeadzone.Scaled(0.99f).Apply(0.5f));
		Assert.Equal(AxisDeadzone.Scaled(0.2f, 0.8f), AxisDeadzone.Scaled(0.2f, 0.8f)); // value equality
	}

	[Fact]
	public static void RadialDeadzones() {
		Assert.Equal(Vector2.Zero, Axis2dDeadzone.Radial(0.2f).Apply(new Vector2(0.1f, 0.1f)));
		Assert.Equal(new Vector2(0.6f, 0f), Axis2dDeadzone.Radial(0.2f).Apply(new Vector2(0.6f, 0f)));
		Near(new Vector2(0.5f, 0f), Axis2dDeadzone.Radial(0.2f, 0.5f).Apply(new Vector2(1f, 0f)));
		Near(new Vector2(0.5f, 0f), Axis2dDeadzone.ScaledRadial(0.2f).Apply(new Vector2(0.6f, 0f)));
		// scaled radial keeps the direction
		Vector2 diag = Axis2dDeadzone.ScaledRadial(0.2f).Apply(new Vector2(0.6f, 0.6f));
		Near(diag.X, diag.Y);
	}

	[Fact]
	public static void AxialDeadzones() {
		Assert.Equal(new Vector2(0f, 0.5f), Axis2dDeadzone.Axial(0.2f).Apply(new Vector2(0.1f, 0.5f)));
		Near(new Vector2(0f, 0.5f), Axis2dDeadzone.ScaledAxial(0.2f).Apply(new Vector2(0.1f, 0.6f)));
		Assert.Equal(new Vector2(3f, -3f), Axis2dDeadzone.None.Apply(new Vector2(3f, -3f)));
	}
}
