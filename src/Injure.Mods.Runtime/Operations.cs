// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Mods.Runtime;

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct ReloadRequestKind {
	public enum Case {
		Any = 1,
		SafeBoundary,
		Live,
	}
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct DisableRequestKind {
	public enum Case {
		Strict = 1,
		DisableDependents,
		DisableDependentsAndReloadOptionalDependents,
	}
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct EnableRequestKind {
	public enum Case {
		Strict = 1,
		EnableRequiredDependencies,
		EnableRequiredDependenciesAndReloadOptionalDependents,
	}
}
