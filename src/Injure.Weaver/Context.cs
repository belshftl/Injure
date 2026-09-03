// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace Injure.Weaver;

public sealed class Context {
	public const string EngineAssemblyName = "Injure";
	public const string AttributeNs = "Injure.Mods.Weaver";

	public ModuleDefinition Module { get; }
	public Options Options { get; }

	public ITypeDefOrRef MulticastDelegate { get; }
	public ICustomAttributeType ModifTargetCtor { get; }
	public ICustomAttributeType ModifInjectedCtor { get; }
	public ICustomAttributeType ModifPublicizedCtor { get; }
	public ICustomAttributeType PublicizedCtor { get; }
	public ICustomAttributeType ReferenceAssemblyCtor { get; }

	/// <summary>
	/// NullableAttribute(byte), for a type with exactly one nullable-capable slot.
	/// </summary>
	public ICustomAttributeType? NullableByteCtor { get; }
	/// <summary>
	/// NullableAttribute(byte[]), for a type with more than one nullable-capable slot.
	/// </summary>
	public ICustomAttributeType? NullableByteArrayCtor { get; }
	public ICustomAttributeType? NullableContextCtor { get; }

	public CorLibTypeFactory Cor => Module.CorLibTypeFactory;

	public Context(ModuleDefinition module, Options options) {
		Module = module;
		Options = options;

		MulticastDelegate = module.DefaultImporter.ImportType(corType("System", "MulticastDelegate"));

		AssemblyReference a = module.AssemblyReferences.FirstOrDefault(static a => a.Name == EngineAssemblyName)
			?? throw new PatchException($"input must reference '{EngineAssemblyName}'");

		ModifTargetCtor = attrCtor(a, AttributeNs, "ModifTargetAttribute", Cor.Int32);
		ModifInjectedCtor = attrCtor(a, AttributeNs, "ModifInjectedAttribute", Cor.String, Cor.String);
		ModifPublicizedCtor = attrCtor(a, AttributeNs, "ModifPublicizedAttribute");
		PublicizedCtor = attrCtor(a, AttributeNs, "PublicizedAttribute", Cor.Byte);

		ReferenceAssemblyCtor = new MemberReference(
			module.DefaultImporter.ImportType(corType("System.Runtime.CompilerServices", "ReferenceAssemblyAttribute")),
			".ctor",
			MethodSignature.CreateInstance(Cor.Void)
		);

		if (module.AssemblyReferences.FirstOrDefault(static a => a.Name == "System.Runtime") is AssemblyReference sr) {
			NullableByteCtor = attrCtor(sr, "System.Runtime.CompilerServices", "NullableAttribute", Cor.Byte);
			NullableByteArrayCtor = attrCtor(
				sr,
				"System.Runtime.CompilerServices",
				"NullableAttribute",
				Cor.Byte.MakeSzArrayType()
			);
			NullableContextCtor = attrCtor(
				sr,
				"System.Runtime.CompilerServices",
				"NullableContextAttribute",
				Cor.Byte
			);
		}
	}

	public static CustomAttribute Attr(ICustomAttributeType ctor) => new(ctor, new CustomAttributeSignature());

	public static CustomAttribute Attr(ICustomAttributeType ctor, params CustomAttributeArgument[] args) =>
		new(ctor, new CustomAttributeSignature(args));

	public static CustomAttributeArgument Arg(TypeSignature type, object value) => new(type, value);

	private TypeReference corType(string ns, string name) => new(Module, Module.CorLibTypeFactory.CorLibScope, ns, name);

	private MemberReference attrCtor(AssemblyReference scope, string ns, string name, params TypeSignature[] @params) {
		TypeReference type = new(Module, scope, ns, name);
		return new MemberReference(
			Module.DefaultImporter.ImportType(type),
			".ctor",
			MethodSignature.CreateInstance(Cor.Void, @params)
		);
	}
}
