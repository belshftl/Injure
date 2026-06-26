// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.ModKit.Abstractions;

namespace TestMod.Contracts;

[ModLifetimeIdentityBelongsTo("jdoe.test-mod")]
public readonly struct TestModL : IModLifetimeIdentity {
}

public interface ITestModExports : IModExportContract<TestModL> {
	void DoSomething();
}
