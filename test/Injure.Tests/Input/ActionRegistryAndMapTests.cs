// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Input;

namespace Injure.Tests.Input;

public sealed class ActionRegistryAndMapTests {
	// ==========================================================================
	// registry
	[Theory]
	[InlineData("game::jump")]
	[InlineData("9lives::a")]
	[InlineData("my-game.v2::walk_fast")]
	[InlineData("game::ui/menu@main#confirm+alt")]
	public static void ValidSidsAreAccepted(string sid) {
		Assert.True(ActionRegistry.ValidateSid(sid, out string? err), err);
	}

	[Theory]
	[InlineData("")]
	[InlineData("jump")]
	[InlineData("a::b::c")]
	[InlineData("::jump")]
	[InlineData("game::")]
	[InlineData("_game::jump")]
	[InlineData("game::-jump")]
	[InlineData("my game::jump")]
	[InlineData("game:jump")]
	[InlineData("ga/me::jump")]
	public static void InvalidSidsAreRejected(string sid) {
		Assert.False(ActionRegistry.ValidateSid(sid, out string? err));
		Assert.NotNull(err);
		Assert.Throws<FormatException>(() => new ActionRegistry().Register(sid));
	}

	[Fact]
	public static void SidPartsFollowOwnerAndLocalIdLengthLimits() {
		Assert.True(ActionRegistry.ValidateSid(new string('o', 64) + "::" + new string('l', 256), out _));
		Assert.False(ActionRegistry.ValidateSid(new string('o', 65) + "::a", out string? nsErr));
		Assert.Contains("owner ID", nsErr);
		Assert.False(ActionRegistry.ValidateSid("a::" + new string('l', 257), out string? nameErr));
		Assert.Contains("local ID", nameErr);
	}

	[Fact]
	public static void NullSidIsInvalid() {
		Assert.False(ActionRegistry.ValidateSid(null, out _));
	}

	[Fact]
	public static void RegisterAndLookupWorks() {
		ActionRegistry reg = new();
		ActionId jump = reg.Register("game::jump");
		ActionId duck = reg.Register("game::duck");
		Assert.True(jump.IsValid);
		Assert.NotEqual(jump, duck);
		Assert.Equal(jump, reg.GetId("game::jump"));
		Assert.Equal("game::duck", reg.GetSid(duck));
		Assert.False(reg.TryGetId("game::fly", out _));
		Assert.Throws<ArgumentException>(() => reg.GetId("game::fly"));
		Assert.Throws<ArgumentException>(() => reg.GetSid(default));
		Assert.Throws<InvalidOperationException>(() => reg.Register("game::jump"));
	}

	[Fact]
	public static void BatchWithNamespaceRegistersLocalNames() {
		ActionRegistry reg = new();
		ActionId a = default, b = default;
		reg.RegisterMany("ui", r => {
			a = r.Register("confirm");
			b = r.Register("cancel");
		});
		Assert.Equal(a, reg.GetId("ui::confirm"));
		Assert.Equal(b, reg.GetId("ui::cancel"));
		Assert.Throws<ArgumentException>(() => reg.RegisterMany("bad ns", static _ => { }));
		Assert.Throws<ArgumentException>(() => reg.RegisterMany("ui/menu", static _ => { }));
		Assert.Equal("ui::a/b", reg.GetSid(registerOneIn(reg, "ui", "a/b")));
	}

	private static ActionId registerOneIn(ActionRegistry reg, string ns, string name) {
		ActionId id = default;
		reg.RegisterMany(ns, r => id = r.Register(name));
		return id;
	}

	[Fact]
	public static void BatchRejectsDuplicates() {
		ActionRegistry reg = new();
		reg.Register("game::jump");
		Assert.Throws<InvalidOperationException>(() => reg.RegisterMany(static r => r.Register("game::jump")));
		Assert.Throws<InvalidOperationException>(() => reg.RegisterMany(static r => {
			r.Register("game::x");
			r.Register("game::x");
		}));
	}

	[Fact]
	public static void ThrowingBatchRegistryCallbackRegistersNothing() {
		ActionRegistry reg = new();
		Assert.Throws<InvalidTimeZoneException>(() => reg.RegisterMany(static r => {
			r.Register("game::a");
			r.Register("game::b");
			throw new InvalidTimeZoneException();
		}));
		Assert.False(reg.TryGetId("game::a", out _));
		Assert.False(reg.TryGetId("game::b", out _));
		// and the registry is still usable
		Assert.True(reg.Register("game::a").IsValid);
	}

	[Fact]
	public static void RegistryIsSafeForConcurrentRegistration() {
		ActionRegistry reg = new();
		Parallel.For(0, 200, i => reg.Register($"game::a{i}"));
		HashSet<ActionId> ids = new();
		for (int i = 0; i < 200; i++)
			Assert.True(ids.Add(reg.GetId($"game::a{i}")));
	}

	// ==========================================================================
	// map building
	[Fact]
	public static void BuilderRejectsDuplicatesAndInvalidActions() {
		ActionId a = new ActionRegistry().Register("t::a");
		ActionMapBuilder b = new();
		b.BindButton(a, InputButtonSource.Key(Key.A));
		Assert.Throws<ArgumentException>(() => b.BindButton(a, InputButtonSource.Key(Key.A)));
		Assert.Throws<ArgumentException>(() => b.BindButton(default, InputButtonSource.Key(Key.B)));
		b.BindButton(a, InputButtonSource.Key(Key.B)); // second source for the same action is fine
		Assert.Equal(2, b.ButtonBindings.Count);
	}

	[Fact]
	public static void SnapshotRejectsDuplicateBindings() {
		ActionId a = new ActionRegistry().Register("t::a");
		ButtonBinding bb = new(a, InputButtonSource.Key(Key.A));
		Assert.Throws<ArgumentException>(() => new ActionMapSnapshot([bb, bb], [], [], [], StateAxisMergePolicy.MaxAbs, StateAxis2dMergePolicy.MaxMagnitude));
	}

	[Fact]
	public static void ClearAndCopyWork() {
		ActionRegistry reg = new();
		ActionId a = reg.Register("t::a");
		ActionId b = reg.Register("t::b");
		ActionMapBuilder builder = new() { StateAxisMergePolicy = StateAxisMergePolicy.SumClamp };
		builder.BindButton(a, InputButtonSource.Key(Key.A));
		builder.BindStateAxis(a, InputStateAxisSource.GamepadAxis(GamepadAxis.LeftX), AxisDeadzone.None);
		builder.BindStateAxis2d(b, InputStateAxis2dSource.GamepadStick(GamepadStick.Left), Axis2dDeadzone.None, new Vector2(2, 1));
		builder.BindImpulseAxis(b, InputImpulseAxisSource.PointerWheel(PointerWheelAxis.Y), 3f);

		var copy = ActionMapBuilder.FromSnapshot(builder.ToSnapshot());
		Assert.Equal(StateAxisMergePolicy.SumClamp, copy.StateAxisMergePolicy);
		Assert.Equal(builder.StateAxis2dBindings, copy.StateAxis2dBindings);
		Assert.Equal(builder.ImpulseAxisBindings, copy.ImpulseAxisBindings);

		copy.ClearBindingsFor(a);
		Assert.Empty(copy.ButtonBindings);
		Assert.Empty(copy.StateAxisBindings);
		Assert.Single(copy.StateAxis2dBindings);
		copy.Clear();
		Assert.Empty(copy.ImpulseAxisBindings);
		Assert.Equal(StateAxisMergePolicy.SumClamp, copy.StateAxisMergePolicy); // policies are kept
	}

	[Fact]
	public static void BuilderDefaults() {
		ActionMapBuilder b = new();
		Assert.Equal(StateAxisMergePolicy.MaxAbs, b.StateAxisMergePolicy);
		Assert.Equal(StateAxis2dMergePolicy.MaxMagnitude, b.StateAxis2dMergePolicy);
		ActionId a = new ActionRegistry().Register("t::a");
		b.BindStateAxis2d(a, InputStateAxis2dSource.GamepadStick(GamepadStick.Left), Axis2dDeadzone.None);
		Assert.Equal(Vector2.One, b.StateAxis2dBindings[0].Scale);
	}

	[Fact]
	public static void ProfileReplaceBumpsVersion() {
		ActionMapSnapshot m1 = new ActionMapBuilder().ToSnapshot();
		ActionMapSnapshot m2 = new ActionMapBuilder().ToSnapshot();
		ActionProfile p = new(m1);
		Assert.Equal(1ul, p.Version);
		p.Replace(m2);
		Assert.Same(m2, p.Current);
		Assert.Equal(2ul, p.Version);
		Assert.Throws<ArgumentNullException>(() => p.Replace(null!));
		Assert.Throws<ArgumentNullException>(static () => new ActionProfile(null!));
	}
}
