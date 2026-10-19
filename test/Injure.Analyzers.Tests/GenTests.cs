// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Analyzers.Usage;

namespace Injure.Analyzers.Tests;

// test the Gen test harness itself because it was broken in the past too and that ended up
// affecting the other tests
public sealed class GenTests {
	[Fact]
	public static void AnalyzeFailsOnSourceThatDoesntCompile() =>
		Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Gen.Analyze(new DontImplementAnalyzer(), "public sealed class C : INoSuchInterface {}"));

	[Fact]
	public static void RunFailsOnSourceThatDoesntCompile() =>
		Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Gen.Run("public sealed class C { NoSuchType x; }", []));
}
