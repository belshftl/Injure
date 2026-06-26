// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.Mods;

public interface IModExportContract<L> where L : struct, IModLifetimeIdentity {
}

public interface IModExportDeclarations<L> where L : struct, IModLifetimeIdentity {
	void Add<TContract>(TContract impl) where TContract : class, IModExportContract<L>;
}

public interface IModExportTable<L, LDependency> where L : struct, IModLifetimeIdentity where LDependency : struct, IModLifetimeIdentity {
	bool TryGet<TContract>([NotNullWhen(true)] out TContract? impl) where TContract : class, IModExportContract<LDependency>;
	TContract Require<TContract>() where TContract : class, IModExportContract<LDependency>;
}
