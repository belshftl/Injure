// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.AnalyzerShared.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.Analyzers.Language;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ForeachAnalyzer() : ForeachAnalyzerBase(Diagnostics.Language.ForeachImplicitBadCast);
