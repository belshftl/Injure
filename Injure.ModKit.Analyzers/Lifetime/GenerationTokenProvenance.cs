// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Injure.ModKit.Analyzers.Lifetime;

internal sealed class GenerationTokenProvenance(KnownTypes known) {
	private readonly KnownTypes known = known;
	private readonly HashSet<ILocalSymbol> generationTokenLocals = new(SymbolEqualityComparer.Default);

	public void ObserveAssignment(ILocalSymbol local, IOperation value) {
		if (IsGenerationBoundedToken(value))
			generationTokenLocals.Add(local);
	}

	public bool IsGenerationBoundedToken(IOperation operation) {
		if (operation is IConversionOperation conversion)
			return IsGenerationBoundedToken(conversion.Operand);
		if (operation is ILocalReferenceOperation localRef && generationTokenLocals.Contains(localRef.Local))
			return true;
		ITypeSymbol? type = operation.Type;
		if (type is INamedTypeSymbol named && known.GenerationCancellationToken is not null)
			if (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, known.GenerationCancellationToken))
				return true;
		return false;
	}
}
