// SPDX-License-Identifier: MIT

using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Injure.ModKit.Analyzers.Lifetime;

internal sealed class LabelPositionMap {
	private readonly Dictionary<ILabelSymbol, int> positions;

	private LabelPositionMap(Dictionary<ILabelSymbol, int> positions) {
		this.positions = positions;
	}

	public static LabelPositionMap Build(IOperation body) {
		Dictionary<ILabelSymbol, int> positions = new(SymbolEqualityComparer.Default);
		collect(body, positions);
		return new LabelPositionMap(positions);
	}

	public bool TryGetPosition(ILabelSymbol label, out int position) => positions.TryGetValue(label, out position);

	private static void collect(IOperation operation, Dictionary<ILabelSymbol, int> positions) {
		if (operation is ILabeledOperation labeled)
			positions[labeled.Label] = operation.Syntax.SpanStart;
		foreach (IOperation child in operation.ChildOperations)
			collect(child, positions);
	}
}
