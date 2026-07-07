// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Mono.Cecil;

namespace Injure.Mods.Abstractions.Hooks.Il;

/// <summary>
/// Provides reference import helpers for the Cecil module currently being transformed.
/// </summary>
public readonly ref struct IlReferenceImports {
	private readonly IlTransactionCore core;
	internal IlReferenceImports(IlTransactionCore core) {
		this.core = core ?? throw new InternalStateException("IlReferenceImports constructed with null core");
	}

	public TypeReference Import(Type type) => core.Import(type);
	public TypeReference Import(TypeReference type) => core.Import(type);
	public MethodReference Import(MethodBase method) => core.Import(method);
	public MethodReference Import(MethodReference method) => core.Import(method);
	public FieldReference Import(FieldInfo field) => core.Import(field);
	public FieldReference Import(FieldReference field) => core.Import(field);
}
