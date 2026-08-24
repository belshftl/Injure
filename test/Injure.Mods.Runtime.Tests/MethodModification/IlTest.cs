// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods.Abstractions;

namespace Injure.Mods.Runtime.Tests.MethodModification;

[ModLifetimeIdentityBelongsTo(IlTest.OwnerId)]
internal readonly struct TestL : IModLifetimeIdentity;

internal static class IlTest {
	public const string OwnerId = "test";
	public const string LocalId = "patch";
}
