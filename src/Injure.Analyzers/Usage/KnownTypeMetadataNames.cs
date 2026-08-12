// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Analyzers.Usage;

internal static class KnownTypeMetadataNames {
	public const string Attribute = "System.Attribute";
	public const string IReadOnlyCollection = "System.Collections.Generic.IReadOnlyCollection`1";
	public const string ICollection = "System.Collections.Generic.ICollection`1";
	public const string IReadOnlyDictionary = "System.Collections.Generic.IReadOnlyDictionary`2";

	public const string DontCacheAttribute = "Injure.CodeAnalysis.Internal.DontCacheAttribute";
	public const string DontCaptureIntoClosureAttribute = "Injure.CodeAnalysis.Internal.DontCaptureIntoClosureAttribute";
	public const string DontImplementAttribute = "Injure.CodeAnalysis.Internal.DontImplementAttribute";
	public const string MethodAttributeUsageAttribute = "Injure.CodeAnalysis.MethodAttributeUsageAttribute";
}
