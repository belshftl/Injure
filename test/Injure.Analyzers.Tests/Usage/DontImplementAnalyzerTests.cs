// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Analyzers.Usage;

namespace Injure.Analyzers.Tests.Usage;

public sealed class DontImplementAnalyzerTests {
	private const string prelude = """
		using Injure.Host;
		using Injure.Layers;
		public interface IDerived : ILayerTickFeeder {}

		""";

	private const string feed = "public T Feed<T>(T obj) where T : class, IHostTickReceiver => obj;";

	private static string[] reported(string decls) =>
		Gen.Analyze(new DontImplementAnalyzer(), prelude + decls)
			.Where(static d => d.Id == "IJ0105")
			.Select(static d => d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan))
			.ToArray();

	[Theory]
	[InlineData("public sealed class C : ILayerTickFeeder { " + feed + " }", "ILayerTickFeeder")]
	[InlineData("public struct S : System.IDisposable, IDerived { public void Dispose() {} " + feed + " }", "IDerived")]
	public static void ImplementationsAreReportedAtTheBaseListEntry(string decls, string at) =>
		Assert.Equal(at, Assert.Single(reported(decls)));

	[Fact]
	public static void InheritingThroughABaseClassIsntReported() =>
		Assert.Equal(
			["ILayerTickFeeder"],
			reported("public abstract class B : ILayerTickFeeder { " + feed + " } public sealed class C : B {}")
		);
}
