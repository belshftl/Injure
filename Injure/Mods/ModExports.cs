// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Injure.CodeAnalysis;

namespace Injure.Mods;

public interface IModExportContract<L> where L : struct, IModLifetimeIdentity {
}

[DontImplement]
public interface IModExportDeclarations<L> where L : struct, IModLifetimeIdentity {
	void Add<TContract>(TContract impl) where TContract : class, IModExportContract<L>;
}

[DontImplement]
public interface IModExportTable<L, LDependency> where L : struct, IModLifetimeIdentity where LDependency : struct, IModLifetimeIdentity {
	bool TryGet<TContract>([NotNullWhen(true)] out TContract? impl) where TContract : class, IModExportContract<LDependency>;
	TContract Require<TContract>() where TContract : class, IModExportContract<LDependency>;
}
