// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Abstractions;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ModAnalyzerStampAttribute(string ruleset, string hash) : Attribute {
	public string Ruleset { get; } = ruleset;
	public string Hash { get; } = hash;
}
