// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Abstractions;

public interface IModLifetimeIdentity {
}

[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ModLifetimeIdentityMarkerAttribute : Attribute {
}
