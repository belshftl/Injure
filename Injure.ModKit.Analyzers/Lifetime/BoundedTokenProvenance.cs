// SPDX-License-Identifier: MIT

using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Injure.ModKit.Analyzers.Lifetime;

internal sealed class BoundedTokenProvenance(KnownTypes known) {
	private readonly KnownTypes known = known;
	private readonly HashSet<ILocalSymbol> boundedTokenLocals = new(SymbolEqualityComparer.Default);

	public void ObserveAssignment(ILocalSymbol local, IOperation value) {
		if (IsBoundedToken(value))
			boundedTokenLocals.Add(local);
		else
			boundedTokenLocals.Remove(local);
	}

	public bool IsBoundedToken(IOperation operation) {
		if (operation is IConversionOperation conversion)
			return IsBoundedToken(conversion.Operand);
		if (operation is ILocalReferenceOperation localRef && boundedTokenLocals.Contains(localRef.Local))
			return true;
		ITypeSymbol? type = operation.Type;
		if (type is INamedTypeSymbol named && known.BoundedCt is not null)
			if (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, known.BoundedCt))
				return true;
		return false;
	}
}
