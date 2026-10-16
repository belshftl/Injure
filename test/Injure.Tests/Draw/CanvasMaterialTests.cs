// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw;

namespace Injure.Tests.Draw;

public sealed class CanvasMaterialTests {
	[Fact]
	public void SdfAcceptsValidParams() {
		SdfParams p = new(4f, EdgeValue: 0f, SoftnessPixels: 0f, OutlineWidthPixels: 2f);
		CanvasMaterial m = CanvasMaterial.Sdf(p);
		Assert.Equal(TextureInterpretation.Sdf, m.TextureInterpretation);
		Assert.Equal(p, m.SdfParams);
		Assert.Equal(1f, CanvasMaterial.Sdf(p with { EdgeValue = 1f }).SdfParams!.Value.EdgeValue);
	}

	[Fact]
	public void SdfRejectsTheDefaultParams() {
		var ex = Assert.Throws<ArgumentInvalidDefaultException>(() => CanvasMaterial.Sdf(default));
		Assert.Equal("params", ex.ParamName);
	}

	public static TheoryData<SdfParams> InvalidParams => [
		new SdfParams(0f),
		new SdfParams(-1f),
		new SdfParams(float.NaN),
		new SdfParams(float.PositiveInfinity),
		new SdfParams(4f, EdgeValue: -0.1f),
		new SdfParams(4f, EdgeValue: 1.1f),
		new SdfParams(4f, EdgeValue: float.NaN),
		new SdfParams(4f, SoftnessPixels: -1f),
		new SdfParams(4f, SoftnessPixels: float.PositiveInfinity),
		new SdfParams(4f, OutlineWidthPixels: -1f),
		new SdfParams(4f, OutlineWidthPixels: float.NaN),
	];

	[Theory]
	[MemberData(nameof(InvalidParams))]
	public void SdfRejectsOutOfRangeParams(SdfParams p) {
		var ex = Assert.Throws<ArgumentOutOfRangeException>(() => CanvasMaterial.Sdf(p));
		Assert.Equal("params", ex.ParamName);
	}

	[Fact]
	public void NonSdfMaterialsHaveNoSdfParams() {
		Assert.Null(CanvasMaterial.Color.SdfParams);
		Assert.Null(CanvasMaterial.Rmask.SdfParams);
	}
}
