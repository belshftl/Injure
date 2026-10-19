// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.AnalyzerShared.Usage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.DevAnalyzers.Usage;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InvalidDefaultAnalyzer() : InvalidDefaultAnalyzerBase(
	Diagnostics.Usage.InvalidDefaultCreated,
	Diagnostics.Usage.InvalidDefaultOptionalParameter,
	Diagnostics.Usage.InvalidDefaultFieldInStruct
);
