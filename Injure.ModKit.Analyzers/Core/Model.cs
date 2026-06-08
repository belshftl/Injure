// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Core;

internal sealed class ModAssemblyModel(AttributeData attribute, string ownerID, int hotReloadRawValue, string hotReloadName, bool isLive) {
	public AttributeData Attribute { get; } = attribute;
	public string OwnerID { get; } = ownerID;
	public int HotReloadRawValue { get; } = hotReloadRawValue;
	public string HotReloadName { get; } = hotReloadName;
	public bool IsLive { get; } = isLive;
	public Location Location => Attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None;
}

internal sealed class LifetimeIdentityModel(INamedTypeSymbol type, AttributeData attribute) {
	public INamedTypeSymbol Type { get; } = type;
	public AttributeData Attribute { get; } = attribute;
	public Location Location => SymbolHelpers.GetBestLocation(Type, Attribute);
}

internal sealed class EntrypointModel(INamedTypeSymbol type, AttributeData attribute, INamedTypeSymbol implementedInterface, ITypeSymbol gameApiType, ITypeSymbol lifetimeType) {
	public INamedTypeSymbol Type { get; } = type;
	public AttributeData Attribute { get; } = attribute;
	public INamedTypeSymbol ImplementedInterface { get; } = implementedInterface;
	public ITypeSymbol GameApiType { get; } = gameApiType;
	public ITypeSymbol LifetimeType { get; } = lifetimeType;
	public Location Location => SymbolHelpers.GetBestLocation(Type, Attribute);
}

internal sealed class Model {
	private Model(
		ModAssemblyModel? modAssembly,
		ImmutableArray<AttributeData> modAssemblyAttributes,
		ImmutableArray<INamedTypeSymbol> lifetimeCandidates,
		LifetimeIdentityModel? lifetimeIdentity,
		ImmutableArray<INamedTypeSymbol> entrypointCandidates,
		EntrypointModel? entrypoint,
		ImmutableArray<INamedTypeSymbol> reloadEntrypointCandidates,
		EntrypointModel? reloadEntrypoint
	) {
		ModAssembly = modAssembly;
		ModAssemblyAttributes = modAssemblyAttributes;
		LifetimeCandidates = lifetimeCandidates;
		LifetimeIdentity = lifetimeIdentity;
		EntrypointCandidates = entrypointCandidates;
		Entrypoint = entrypoint;
		ReloadEntrypointCandidates = reloadEntrypointCandidates;
		ReloadEntrypoint = reloadEntrypoint;
	}

	public ModAssemblyModel? ModAssembly { get; }
	public ImmutableArray<AttributeData> ModAssemblyAttributes { get; }
	public ImmutableArray<INamedTypeSymbol> LifetimeCandidates { get; }
	public LifetimeIdentityModel? LifetimeIdentity { get; }
	public ImmutableArray<INamedTypeSymbol> EntrypointCandidates { get; }
	public EntrypointModel? Entrypoint { get; }
	public ImmutableArray<INamedTypeSymbol> ReloadEntrypointCandidates { get; }
	public EntrypointModel? ReloadEntrypoint { get; }

	public static Model Create(Compilation compilation) {
		KnownTypes known = new(compilation);
		return Create(compilation, known);
	}

	public static Model Create(Compilation compilation, KnownTypes known) {
		ModAssemblyModel? modAssembly = tryReadModAssembly(compilation, known, out ImmutableArray<AttributeData> modAssemblyAttrs);
		var allTypes = collectAllNamedTypes(compilation.Assembly.GlobalNamespace).ToImmutableArray();

		var lifetimeCandidates = allTypes
			.Where(type => SymbolHelpers.HasAttribute(type, known.ModLifetimeIdentityMarkerAttribute, out _))
			.ToImmutableArray();
		LifetimeIdentityModel? lifetime = lifetimeCandidates.Length == 1
			? tryReadLifetimeIdentity(lifetimeCandidates[0], known)
			: null;

		var entrypointCandidates = allTypes
			.Where(type => SymbolHelpers.HasAttribute(type, known.ModEntrypointAttribute, out _))
			.ToImmutableArray();
		EntrypointModel? entrypoint = entrypointCandidates.Length == 1 && lifetime is not null
			? tryReadEntrypoint(entrypointCandidates[0], known.ModEntrypointAttribute, known.ModEntrypointInterface, lifetime.Type)
			: null;

		var reloadCandidates = allTypes
			.Where(type => SymbolHelpers.HasAttribute(type, known.ModReloadEntrypointAttribute, out _))
			.ToImmutableArray();
		EntrypointModel? reload = reloadCandidates.Length == 1 && lifetime is not null
			? tryReadEntrypoint(reloadCandidates[0], known.ModReloadEntrypointAttribute, known.ModReloadEntrypointInterface, lifetime.Type)
			: null;

		return new Model(modAssembly, modAssemblyAttrs, lifetimeCandidates, lifetime, entrypointCandidates, entrypoint, reloadCandidates, reload);
	}

	private static ModAssemblyModel? tryReadModAssembly(Compilation compilation, KnownTypes known, out ImmutableArray<AttributeData> matches) {
		if (known.ModAssemblyAttribute is null) {
			matches = ImmutableArray<AttributeData>.Empty;
			return null;
		}

		matches = compilation.Assembly.GetAttributes()
			.Where(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, known.ModAssemblyAttribute))
			.ToImmutableArray();
		if (matches.Length != 1)
			return null;

		AttributeData attr = matches[0];
		ImmutableArray<TypedConstant> args = attr.ConstructorArguments;
		if (args.Length < 2 || args[0].Value is not string ownerID || args[1].Value is null)
			return null;

		int raw = Convert.ToInt32(args[1].Value, CultureInfo.InvariantCulture);
		string name = getEnumFieldName(known.ModAssemblyHotReloadLevel, raw) ?? raw.ToString(CultureInfo.InvariantCulture);
		return new ModAssemblyModel(attr, ownerID, raw, name, StringComparer.Ordinal.Equals(name, "Live"));
	}

	internal static bool IsEnumValueNamed(INamedTypeSymbol? enumType, int raw) => getEnumFieldName(enumType, raw) is not null;

	private static string? getEnumFieldName(INamedTypeSymbol? enumType, int raw) {
		if (enumType is null || enumType.EnumUnderlyingType is null)
			return null;

		foreach (ISymbol member in enumType.GetMembers()) {
			if (member is not IFieldSymbol field || !field.HasConstantValue || field.ConstantValue is null)
				continue;
			int val = Convert.ToInt32(field.ConstantValue, CultureInfo.InvariantCulture);
			if (val == raw)
				return field.Name;
		}
		return null;
	}

	private static LifetimeIdentityModel? tryReadLifetimeIdentity(INamedTypeSymbol type, KnownTypes known) {
		if (!SymbolHelpers.HasAttribute(type, known.ModLifetimeIdentityMarkerAttribute, out AttributeData attr))
			return null;
		return new LifetimeIdentityModel(type, attr);
	}

	private static EntrypointModel? tryReadEntrypoint(
		INamedTypeSymbol type,
		INamedTypeSymbol? attributeType,
		INamedTypeSymbol? openInterface,
		INamedTypeSymbol expectedLifetime
	) {
		if (attributeType is null || openInterface is null)
			return null;
		if (!SymbolHelpers.HasAttribute(type, attributeType, out AttributeData attr))
			return null;

		foreach (INamedTypeSymbol iface in type.AllInterfaces) {
			if (!SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, openInterface))
				continue;
			if (iface.TypeArguments.Length != 2)
				continue;
			if (!SymbolEqualityComparer.Default.Equals(iface.TypeArguments[1], expectedLifetime))
				continue;
			return new EntrypointModel(type, attr, iface, iface.TypeArguments[0], iface.TypeArguments[1]);
		}
		return null;
	}

	private static IEnumerable<INamedTypeSymbol> collectAllNamedTypes(INamespaceSymbol ns) {
		foreach (INamedTypeSymbol type in ns.GetTypeMembers()) {
			yield return type;
			foreach (INamedTypeSymbol nested in collectNestedTypes(type))
				yield return nested;
		}
		foreach (INamespaceSymbol child in ns.GetNamespaceMembers())
			foreach (INamedTypeSymbol type in collectAllNamedTypes(child))
				yield return type;
	}

	private static IEnumerable<INamedTypeSymbol> collectNestedTypes(INamedTypeSymbol type) {
		foreach (INamedTypeSymbol nested in type.GetTypeMembers()) {
			yield return nested;
			foreach (INamedTypeSymbol child in collectNestedTypes(nested))
				yield return child;
		}
	}
}
