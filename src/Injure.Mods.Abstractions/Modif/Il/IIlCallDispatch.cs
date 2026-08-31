// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Everything <see cref="IlEmitter.IndirectCall(MethodInfo)"/> needs.
/// </summary>
internal interface IIlCallDispatch {
	/// <summary>
	/// The method the emitted instructions call to resolve a slot; must have a signature of
	/// <c>Func&lt;int, IntPtr&gt;</c>.
	/// </summary>
	IlMethodRef ResolveTarget { get; }

	/// <summary>
	/// Reserves a slot for a target, or returns an existing one.
	/// </summary>
	int AllocateSlot(MethodInfo target);
}
