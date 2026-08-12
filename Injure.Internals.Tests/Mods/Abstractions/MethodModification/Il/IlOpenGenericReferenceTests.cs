// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Injure.Mods.Abstractions.MethodModification.Il;

namespace Injure.Internals.Tests.Mods.Abstractions.MethodModification.Il;

public sealed class IlOpenGenericReferenceTests : IDisposable {
	private readonly FileStream stream;
	private readonly PEReader peReader;
	private readonly MetadataReader metadata;

	public IlOpenGenericReferenceTests() {
		string location = typeof(IlFixture.GenericCallers).Assembly.Location;
		Assert.SkipWhen(string.IsNullOrEmpty(location), "fixture assembly has no on-disk location");
		stream = File.OpenRead(location);
		peReader = new PEReader(stream);
		metadata = peReader.GetMetadataReader();
	}

	public void Dispose() {
		peReader.Dispose();
		stream.Dispose();
	}

	// ==========================================================================================
	// building references
	private static IlNamedTypeRef innerDefinition => Assert.IsType<IlNamedTypeRef>(
		IlReferenceFactory.Type(typeof(IlFixture.Outer<>).GetNestedType("Inner`1")!)
	);

	private static IlNamedTypeRef boxDefinition => Assert.IsType<IlNamedTypeRef>(
		IlReferenceFactory.Type(typeof(IlFixture.Box<>))
	);

	/// <summary>
	/// <c>Outer&lt;!!0&gt;.Inner&lt;!!1&gt;::Pick&lt;!!2&gt;</c> as it appears inside
	/// <c>GenericCallers.Open&lt;A, B, V&gt;</c>.
	/// </summary>
	/// <remarks>
	/// The declaring instantiation and the method arguments are written in the call site's parameter
	/// space, but the signature is written in <c>Pick</c>'s own; its parameters are <c>!0</c>
	/// (<c>Outer</c>'s <c>T</c>), <c>!1</c> (<c>Inner</c>'s <c>U</c>), and <c>!!0</c> (<c>Pick</c>'s <c>V</c>).
	/// </remarks>
	private static IlMethodRef pickInOpenCaller() {
		IlGenericInstanceTypeRef declaring = IlReferenceFactory.GenericInstance(
			innerDefinition,
			IlReferenceFactory.MethodGenericParameter(0),
			IlReferenceFactory.MethodGenericParameter(1)
		);
		IlMethodSignature signature = IlReferenceFactory.Signature(
			returnType: IlReferenceFactory.MethodGenericParameter(0),
			genericParameterCount: 1,
			parameterTypes: [
				IlReferenceFactory.TypeGenericParameter(0),
				IlReferenceFactory.TypeGenericParameter(1),
				IlReferenceFactory.MethodGenericParameter(0),
			]
		);
		IlMethodRef definition = IlReferenceFactory.Method(declaring, "Pick", signature);
		return IlReferenceFactory.GenericMethod(definition, IlReferenceFactory.MethodGenericParameter(2));
	}

	/// <summary>
	/// <c>!0 Box&lt;!!0&gt;::Value</c> as it appears inside <c>FieldCallers.ReadOpen&lt;V&gt;</c>.
	/// </summary>
	private static IlFieldRef valueInOpenReader() => IlReferenceFactory.Field(
		IlReferenceFactory.GenericInstance(boxDefinition, IlReferenceFactory.MethodGenericParameter(0)),
		"Value",
		IlReferenceFactory.TypeGenericParameter(0)
	);

	// ==========================================================================================
	// structural agreement with the decoder
	[Fact]
	public void HandBuiltOpenGenericMethodMatchesDecodedOne() {
		IlMethodBody body = Fixture.Decode(peReader, metadata, "GenericCallers", "Open");
		IlMethodRef decoded = Fixture.SoleCall(body, "Pick");

		Assert.Equal(decoded, pickInOpenCaller());
	}

	[Fact]
	public void HandBuiltOpenGenericFieldMatchesDecodedOne() {
		IlMethodBody body = Fixture.Decode(peReader, metadata, "FieldCallers", "ReadOpen");
		IlFieldRef decoded = body.Instructions
			.Where(i => i.OpCode is ILOpCode.Ldfld)
			.Select(i => ((IlFieldOperand)i.Operand).Field)
			.Single();

		Assert.Equal(decoded, valueInOpenReader());
	}

	[Fact]
	public void SignatureIsWrittenInDeclaringTypesParameterSpace() {
		IlMethodBody body = Fixture.Decode(peReader, metadata, "GenericCallers", "Open");
		IlMethodRef decoded = Fixture.SoleCall(body, "Pick");

		Assert.Equal(IlReferenceFactory.TypeGenericParameter(0), decoded.Signature.ParameterTypes[0]);
		Assert.Equal(IlReferenceFactory.TypeGenericParameter(1), decoded.Signature.ParameterTypes[1]);
		Assert.Equal(IlReferenceFactory.MethodGenericParameter(0), decoded.Signature.ParameterTypes[2]);
		Assert.Equal(1, decoded.Signature.GenericParameterCount);
		Assert.Equal(IlReferenceFactory.MethodGenericParameter(2), Assert.Single(decoded.GenericArguments));
	}

	[Fact]
	public void MixingParameterSpacesProducesADifferentReference() {
		IlMethodSignature wrong = IlReferenceFactory.Signature(
			returnType: IlReferenceFactory.MethodGenericParameter(2),
			genericParameterCount: 1,
			parameterTypes: [
				IlReferenceFactory.MethodGenericParameter(0),
				IlReferenceFactory.MethodGenericParameter(1),
				IlReferenceFactory.MethodGenericParameter(2),
			]
		);
		IlMethodRef wrongRef = IlReferenceFactory.GenericMethod(
			IlReferenceFactory.Method(
				IlReferenceFactory.GenericInstance(
					innerDefinition,
					IlReferenceFactory.MethodGenericParameter(0),
					IlReferenceFactory.MethodGenericParameter(1)
				),
				"Pick",
				wrong
			),
			IlReferenceFactory.MethodGenericParameter(2)
		);

		Assert.NotEqual(pickInOpenCaller(), wrongRef);
	}

	// ==========================================================================================
	// matching
	[Fact]
	public void ConstructedOpenGenericReferenceMatchesCallSite() {
		IlMethodBody baseline = Fixture.Decode(peReader, metadata, "GenericCallers", "Open");
		int matches = -1;

		IlMethodRef target = IlReferenceFactory.GenericMethod(
			IlReferenceFactory.Method(
				IlReferenceFactory.GenericInstance(
					innerDefinition,
					IlReferenceFactory.MethodGenericParameter(0),
					IlReferenceFactory.MethodGenericParameter(1)
				),
				"Pick",
				IlReferenceFactory.Signature(
					returnType: IlReferenceFactory.MethodGenericParameter(0),
					genericParameterCount: 1,
					parameterTypes: [
						IlReferenceFactory.TypeGenericParameter(0),
						IlReferenceFactory.TypeGenericParameter(1),
						IlReferenceFactory.MethodGenericParameter(0),
					]
				)
			),
			IlReferenceFactory.MethodGenericParameter(2)
		);

		IlPipeline.Transform(
			baseline,
			[
				IlManipulatorRegistration.Create<TestL>(IlTest.OwnerId, "find", ctx => {
					matches = ctx.MatchAll([MatchIl.Call(target)], IlPatternProvenanceConstraint.Any).Count;
				}),
			]
		);

		Assert.Equal(1, matches);
	}

	// ==========================================================================================
	// emission
	[Fact]
	public void ConstructedOpenGenericCallCanBeEmittedAndSurvivesEncoding() {
		IlMethodBody baseline = Fixture.Decode(peReader, metadata, "GenericCallers", "Open");
		IlMethodRef target = pickInOpenCaller();

		IlPipelineResult result = IlPipeline.Transform(
			baseline,
			[
				IlManipulatorRegistration.Create<TestL>(IlTest.OwnerId, "patch", ctx =>
					ctx.MatchAll([MatchIl.Call(target)], IlPatternProvenanceConstraint.Any)
						.RequireSingle()
						.EmitBefore(e => {
							e.Ldarg(0);
							e.Ldarg(1);
							e.Ldarg(2);
							e.Call(target);
							e.Pop();
						})
				),
			]
		);

		Assert.True(result.Modified);
		IlMethodBody redecoded = Fixture.RoundtripThroughMetadata(
			peReader,
			metadata,
			result.Body,
			"GenericCallers",
			"Open"
		);

		IlMethodRef[] picks = redecoded.Instructions
			.Where(i => i.OpCode is ILOpCode.Call && ((IlMethodOperand)i.Operand).Method.Name == "Pick")
			.Select(i => ((IlMethodOperand)i.Operand).Method)
			.ToArray();
		Assert.Equal(2, picks.Length);
		Assert.All(picks, pick => Assert.Equal(target, pick));
	}

	[Fact]
	public void ConstructedReferenceResolvesToSameRowAsDecodedReference() {
		IlMethodBody body = Fixture.Decode(peReader, metadata, "GenericCallers", "Open");
		BlanketTestsTokenResolver resolver = new(metadata);

		Assert.Equal(
			resolver.ResolveMethod(Fixture.SoleCall(body, "Pick")),
			resolver.ResolveMethod(pickInOpenCaller())
		);
	}

	// ==========================================================================================
	// validation
	[Fact]
	public void MethodsCannotBeDeclaredOnTypesThatDeclareNothing() {
		IlMethodSignature signature = IlReferenceFactory.Signature(IlTest.Void);

		Assert.Throws<ArgumentException>(
			() => IlReferenceFactory.Method(IlReferenceFactory.ByRef(IlTest.Int32), "M", signature)
		);
		Assert.Throws<ArgumentException>(
			() => IlReferenceFactory.Method(IlReferenceFactory.Pointer(IlTest.Int32), "M", signature)
		);
		Assert.Throws<ArgumentException>(
			() => IlReferenceFactory.Method(IlReferenceFactory.FunctionPointer(signature), "M", signature)
		);
	}

	[Fact]
	public void PrimitiveDeclaringTypesAreAllowedDueToNormalization() {
		// System.String is a TypeDefinition in corelib and normalizes to a primitive, so a member on it
		// would be unnameable if primitives were rejected here
		IlMethodRef getLength = IlReferenceFactory.Method(
			new IlPrimitiveTypeRef(PrimitiveTypeCode.String),
			"get_Length",
			IlReferenceFactory.Signature(IlTest.Int32)
		);

		Assert.Equal("get_Length", getLength.Name);
	}

	[Fact]
	public void ArrayDeclaringTypesAreAllowedForPseudoMethods() {
		IlMethodRef get = IlReferenceFactory.Method(
			IlReferenceFactory.Array(IlTest.Int32, rank: 2),
			"Get",
			IlReferenceFactory.Signature(IlTest.Int32, IlTest.Int32, IlTest.Int32)
		);

		Assert.Equal("Get", get.Name);
	}

	[Fact]
	public void ConstructedMethodsAreNeverInstantiations() {
		IlMethodRef definition = IlReferenceFactory.Method(
			IlReferenceFactory.GenericInstance(
				innerDefinition,
				IlReferenceFactory.MethodGenericParameter(0),
				IlReferenceFactory.MethodGenericParameter(1)
			),
			"Pick",
			IlReferenceFactory.Signature(
				returnType: IlReferenceFactory.MethodGenericParameter(0),
				genericParameterCount: 1,
				parameterTypes: [IlReferenceFactory.MethodGenericParameter(0)]
			)
		);

		Assert.False(definition.IsGenericInstantiation);
		Assert.True(
			IlReferenceFactory.GenericMethod(definition, IlTest.Int32).IsGenericInstantiation
		);
	}

	[Fact]
	public void NamesAreRequired() {
		IlMethodSignature signature = IlReferenceFactory.Signature(IlTest.Void);

		Assert.Throws<ArgumentException>(() => IlReferenceFactory.Method(IlTest.Object, "", signature));
		Assert.Throws<ArgumentNullException>(() => IlReferenceFactory.Method(IlTest.Object, null!, signature));
		Assert.Throws<ArgumentException>(() => IlReferenceFactory.Field(IlTest.Object, "", IlTest.Int32));
	}
}
