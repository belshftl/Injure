// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Analyzers.Usage;

namespace Injure.Analyzers.Tests.Usage;

public sealed class InterfaceImplKindAnalyzerTests {
	private const string prelude = """
		using Injure.CodeAnalysis;
		[InterfaceImplKindConstraint(InterfaceImplKind.Struct)] public interface IStructOnly {}
		[InterfaceImplKindConstraint(InterfaceImplKind.Class)] public interface IClassOnly {}
		[InterfaceImplKindConstraint(InterfaceImplKind.Struct)] public interface IGenericStructOnly<T> {}
		[InterfaceImplKindConstraint((InterfaceImplKind)7)] public interface IUnknownKind {}
		public interface IDerived : IStructOnly {}
		public interface IPlain {}

		""";

	private static (string At, string[] Args)[] reported(string decls) =>
		Gen.Analyze(new InterfaceImplKindAnalyzer(), prelude + decls)
			.Where(static d => d.Id == "IJ0109")
			.Select(static d => (d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan), Gen.MessageArgs(d)))
			.ToArray();

	[Theory]
	[InlineData("public sealed class C : IStructOnly {}", "IStructOnly")]
	[InlineData("public sealed record C : IStructOnly;", "IStructOnly")]
	[InlineData("public struct S : IClassOnly {}", "IClassOnly")]
	[InlineData("public record struct S : IClassOnly;", "IClassOnly")]
	[InlineData("public ref struct S : IClassOnly {}", "IClassOnly")]
	[InlineData("public sealed class C : IPlain, IGenericStructOnly<int> {}", "IGenericStructOnly<int>")]
	[InlineData("public sealed class C : IDerived {}", "IDerived")]
	public static void WrongKindsAreReportedAtTheBaseListEntry(string decls, string at) =>
		Assert.Equal(at, Assert.Single(reported(decls)).At);

	[Fact]
	public static void MessageNamesTheInterfaceAndTheAllowedKind() {
		Assert.Equal(["IStructOnly", "structs"], Assert.Single(reported("public sealed class C : IStructOnly {}")).Args);
		Assert.Equal(["IClassOnly", "classes"], Assert.Single(reported("public struct S : IClassOnly {}")).Args);
	}

	[Fact]
	public static void EachDeclarationOfAPartialTypeIsReportedOnce() =>
		Assert.Single(reported("public sealed partial class C : IStructOnly {} public sealed partial class C : IStructOnly {}"));

	[Theory]
	[InlineData("public struct S : IStructOnly {}")]
	[InlineData("public record struct S : IStructOnly;")]
	[InlineData("public ref struct S : IStructOnly {}")]
	[InlineData("public readonly struct S : IDerived {}")]
	[InlineData("public sealed class C : IClassOnly {}")]
	[InlineData("public sealed record C : IClassOnly;")]
	[InlineData("public sealed class C : IUnknownKind {}")]
	[InlineData("public struct S : IPlain {}")]
	[InlineData("public interface IMore : IStructOnly, IClassOnly {}")]
	public static void MatchingKindsAndNonImplementersAreFine(string decls) => Assert.Empty(reported(decls));

	[Fact]
	public static void OnlyTheBaseClassIsReportedForAnInheritedInterface() =>
		Assert.Equal(
			"public abstract class B : IStructOnly {}".IndexOf("IStructOnly", StringComparison.Ordinal),
			Assert.Single(
				Gen.Analyze(new InterfaceImplKindAnalyzer(), prelude + "public abstract class B : IStructOnly {} public sealed class C : B {}")
					.Where(static d => d.Id == "IJ0109")
			).Location.SourceSpan.Start - prelude.Length
		);
}
