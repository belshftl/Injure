// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Abstractions;

public interface IModLifetimeIdentity {
}

[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ModLifetimeIdentityBelongsToAttribute(string ownerID) : Attribute {
	public string OwnerID { get; } = ownerID;
}
