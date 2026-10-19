// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Analyzers.Usage;

namespace Injure.Analyzers.Tests.Usage;

public sealed class InvalidDefaultAnalyzerTests {
	private const string prelude = """
		using Injure;
		public readonly struct Bad : IHasInvalidDefault {
			private readonly int v;
			public Bad(int v) { this.v = v; }
			bool IHasInvalidDefault.IsDefault => v == 0;
			public static Bad Zero => default; // the type's own code is exempt
			public static bool operator ==(Bad a, Bad b) => a.v == b.v;
			public static bool operator !=(Bad a, Bad b) => a.v != b.v;
			public override bool Equals(object? o) => o is Bad b && b == this;
			public override int GetHashCode() => v;
		}
		public struct WithCtor : IHasInvalidDefault {
			public int V;
			public WithCtor() { V = 1; }
			bool IHasInvalidDefault.IsDefault => V == 0;
		}
		public struct Settable : IHasInvalidDefault {
			public int V;
			bool IHasInvalidDefault.IsDefault => V == 0;
		}

		""";

	private static (string Id, string At)[] reported(string decls) =>
		Gen.Analyze(new InvalidDefaultAnalyzer(), prelude + decls)
			.Select(static d => (d.Id, d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan)))
			.ToArray();

	private static string body(string statements) => $$"""
		public static class C {
			private static void use(Bad b) {}
			private static void M(Bad b) {
				{{statements}}
			}
		}
		""";

	[Fact]
	public static void ThePreludeIsClean() => Assert.Empty(reported(""));

	[Theory]
	[InlineData("Bad x = default;", "default")]
	[InlineData("var x = default(Bad);", "default(Bad)")]
	[InlineData("var x = new Bad();", "new Bad()")]
	[InlineData("use(default);", "default")]
	[InlineData("object o = default(Bad);", "default(Bad)")]
	[InlineData("Bad x = b.GetHashCode() > 0 ? b : default;", "default")]
	[InlineData("Bad? n = null; Bad x = n ?? default;", "default")]
	public static void DefaultValuesAreReported(string statements, string at) =>
		Assert.Equal(("IJ0100", at), Assert.Single(reported(body(statements))));

	[Theory]
	[InlineData("public static class G { public static T Make<T>() where T : struct, IHasInvalidDefault => default; }", "default")]
	[InlineData("public static class G { public static T Make<T>() where T : struct, IHasInvalidDefault => new T(); }", "new T()")]
	[InlineData("public sealed class F { private Bad b = default; }", "default")]
	public static void DefaultValuesOutsideMethodBodiesAndInGenericCodeAreReported(string decls, string at) =>
		Assert.Equal(("IJ0100", at), Assert.Single(reported(decls)));

	[Theory]
	[InlineData("bool x = b == default;")]
	[InlineData("bool x = b != default(Bad);")]
	[InlineData("var x = new WithCtor();")]
	[InlineData("var x = new Settable { V = 1 };")]
	[InlineData("Bad? x = default;")]
	[InlineData("int x = default;")]
	[InlineData("var x = Bad.Zero;")]
	public static void ComparisonsAndValidValuesAreFine(string statements) => Assert.Empty(reported(body(statements)));

	[Theory]
	[InlineData("public static class T { public static bool Try(out Bad b) { b = default; return false; } }")]
	[InlineData("public static class G { public static T Make<T>() => default!; }")]
	[InlineData("public static class T { public static bool Try(bool c, Bad v, out Bad b) { b = c ? v : default; return c; } }")]
	[InlineData("public static class T { public static bool Try(Bad? v, out Bad b) { b = v ?? default; return v is not null; } }")]
	public static void OutAssignmentsAndUnconstrainedGenericsAreFine(string decls) => Assert.Empty(reported(decls));

	[Theory]
	[InlineData("public static class C { public static void M(Bad b = default) {} }", "b")]
	[InlineData("public static class C { public static void M(int i, Bad b = new()) {} }", "b")]
	[InlineData("public sealed class C { public int this[Bad b = default] => 0; }", "b")]
	[InlineData("public static class C { public static void M() { void f(Bad b = default) {} f(); } }", "b")]
	[InlineData("public static class C { public static void M() { var f = (Bad b = default) => 0; } }", "b")]
	[InlineData("public class B { public virtual void M(Bad b = default) {} } public sealed class D : B { public override void M(Bad b = default) {} }", "b")]
	[InlineData("public readonly record struct R(int I, Bad B = default) : IHasInvalidDefault { bool IHasInvalidDefault.IsDefault => I == 0; }", "B")]
	public static void OptionalParametersDefaultingToDefaultAreReportedOnce(string decls, string at) =>
		Assert.Equal(("IJ0101", at), Assert.Single(reported(decls)));

	[Fact]
	public static void CallsOmittingSuchParametersArentReported() =>
		Assert.Equal(
			["IJ0101"],
			reported("public static class C { public static void M(Bad b = default) {} public static void N() => M(); }").Select(static r => r.Id)
		);

	[Fact]
	public static void NullableOptionalParametersAreFine() =>
		Assert.Empty(reported("public static class C { public static void M(Bad? b = null) {} }"));

	[Theory]
	[InlineData("public struct H { private Bad b; }", "b")]
	[InlineData("public struct H { public Bad B { get; } }", "B")]
	[InlineData("public readonly record struct H(Bad B);", "B")]
	[InlineData("public struct H { private int i; private Bad first; private Bad second; }", "first")]
	[InlineData("public struct H<T> where T : struct, IHasInvalidDefault { private T t; }", "t")]
	[InlineData("public ref struct H { private Bad b; }", "b")]
	public static void StructsHoldingSuchFieldsAreReportedOnce(string decls, string at) =>
		Assert.Equal(("IJ0102", at), Assert.Single(reported(decls)));

	[Theory]
	[InlineData("public struct H : IHasInvalidDefault { private Bad b; bool IHasInvalidDefault.IsDefault => b == default; }")]
	[InlineData("public struct H { private static Bad s; private Bad? n; }")]
	[InlineData("public sealed class H { private Bad b; }")]
	public static void OtherHoldersAreFine(string decls) => Assert.Empty(reported(decls));

	[Fact]
	public static void MessagesNameTheTypesInvolved() {
		Microsoft.CodeAnalysis.Diagnostic[] all = Gen.Analyze(
			new InvalidDefaultAnalyzer(),
			prelude + "public struct H { private Bad b; public static void M(Bad p = default) { var x = default(Bad); } }"
		).OrderBy(static d => d.Id).ToArray();
		Assert.Equal(
			[
				("IJ0100", ["Bad"]),
				("IJ0101", ["p", "Bad"]),
				("IJ0102", ["b", "Bad", "H"]),
			],
			all.Select(static d => (d.Id, Gen.MessageArgs(d)))
		);
	}
}
