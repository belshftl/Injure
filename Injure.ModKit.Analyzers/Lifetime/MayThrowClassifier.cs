// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Injure.ModKit.Analyzers.Lifetime;

internal static class MayThrowClassifier {
	public static bool MayThrow(IOperation? operation) {
		if (operation is null)
			return false;
		return operation switch {
			ILiteralOperation or ILocalReferenceOperation or IParameterReferenceOperation or IInstanceReferenceOperation or IDefaultValueOperation => false,
			IFieldReferenceOperation field => field.Instance is not null && MayThrow(field.Instance),
			IConversionOperation conversion => conversionMayThrow(conversion) || MayThrow(conversion.Operand),
			IVariableDeclarationOperation declaration => declaration.Declarators.Any(MayThrow),
			IVariableDeclaratorOperation declarator => declarator.Initializer is not null && MayThrow(declarator.Initializer.Value),
			IVariableDeclarationGroupOperation group => group.Declarations.Any(MayThrow),
			ISimpleAssignmentOperation assignment => MayThrow(assignment.Target) || MayThrow(assignment.Value),
			IBlockOperation block => block.Operations.Any(MayThrow),
			_ => true,
		};
	}

	private static bool conversionMayThrow(IConversionOperation conversion) {
		if (conversion.Conversion.IsUserDefined)
			return true;
		if (conversion.IsTryCast)
			return false;
		if (conversion.IsChecked)
			return true;
		if (conversion.Conversion.IsImplicit)
			return false;
		return true;
	}
}
