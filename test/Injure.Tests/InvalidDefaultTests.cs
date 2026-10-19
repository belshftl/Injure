// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw;
using Injure.Gpu;

namespace Injure.Tests;

public sealed class InvalidDefaultTests {
	private readonly struct Test(int value) : IHasInvalidDefault {
		private readonly int value = value;
		bool IHasInvalidDefault.IsDefault => value == 0;
	}

	private static bool isDefault<T>(T value) where T : struct, IHasInvalidDefault => value.IsDefault;

	[Fact]
	public static void ExceptionMessagesNameTheType() {
		const string expected = "the default value of the struct 'Injure.Draw.CanvasTarget' is invalid";
		Assert.Equal(expected, InvalidDefaultException.For<CanvasTarget>().Message);
		Assert.Equal(expected, InvalidDefaultException.For("Injure.Draw.CanvasTarget").Message);
		Assert.Equal(expected, ArgumentInvalidDefaultException.For<CanvasTarget>().Message);
		var withParam = ArgumentInvalidDefaultException.For<CanvasTarget>("target");
		Assert.Equal("target", withParam.ParamName);
		Assert.StartsWith(expected, withParam.Message);
	}

	[Fact]
	public static void ThrowIfDefaultThrowsOnlyForDefault() {
		ArgumentInvalidDefaultException.ThrowIfDefault(new Test(1));
		Test t = default;
		ArgumentInvalidDefaultException ex = Assert.Throws<ArgumentInvalidDefaultException>(() => ArgumentInvalidDefaultException.ThrowIfDefault(t));
		Assert.Equal("t", ex.ParamName);
		Assert.Contains("t", ex.Message);
	}

	[Fact]
	public static void ThrowIfDefaultTakesAnExplicitParamName() =>
		Assert.Equal(
			"other",
			Assert.Throws<ArgumentInvalidDefaultException>(() => ArgumentInvalidDefaultException.ThrowIfDefault(default(Test), "other")).ParamName
		);

	[Fact]
	public static void ThrowIfDefaultAcceptsNull() {
		Test? none = null;
		ArgumentInvalidDefaultException.ThrowIfDefault(none);
		ArgumentInvalidDefaultException.ThrowIfDefault((Test?)new Test(1));
		Test? some = default(Test);
		Assert.Equal("some", Assert.Throws<ArgumentInvalidDefaultException>(() => ArgumentInvalidDefaultException.ThrowIfDefault(some)).ParamName);
	}

	[Fact]
	public static void ThrowIfAnyDefaultSpecifiesTheIndex() {
		ArgumentInvalidDefaultException.ThrowIfAnyDefault<Test>([new(1), new(2)], "items");
		Assert.Equal(
			"items[1]",
			Assert.Throws<ArgumentInvalidDefaultException>(static () => ArgumentInvalidDefaultException.ThrowIfAnyDefault<Test>([new(1), default, default], "items")).ParamName
		);
	}

	[Fact]
	public static void DefaultIsInvalidClosedEnumsImplementTheInterface() {
		Assert.True(isDefault(default(MapMode)));
		Assert.False(isDefault(MapMode.Read));
	}

	[Fact]
	public static void ClosedEnumsRawTagAndToStringDontThrow() {
		Assert.Throws<InvalidDefaultException>(static () => default(MapMode).Tag);
		Assert.Equal((MapMode.Case)0, default(MapMode).RawTag);
		Assert.Equal("MapMode(0)", default(MapMode).ToString());
		Assert.Equal("Read", MapMode.Read.ToString());
		Assert.Equal(MapMode.Case.Read, MapMode.Read.RawTag);
		Assert.Equal(default(MapMode).GetHashCode(), default(MapMode).GetHashCode());
	}

	[Fact]
	public static void ClosedFlagsRawMaskDoesntThrow() {
		BufferUsage usage = BufferUsage.CopyDst | BufferUsage.Vertex;
		Assert.Equal(usage.Mask, usage.RawMask);
		Assert.Equal(BufferUsage.Bits.None, default(BufferUsage).RawMask);
		Assert.Equal(usage.Mask.ToString(), usage.ToString());
	}
}
