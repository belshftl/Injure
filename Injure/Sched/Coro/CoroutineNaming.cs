// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Injure.Sched.Coro;

public sealed class NamedCoroIterator(
	IEnumerator<CoroYield> iterator,
	string debugName,
	string sourceFile,
	int sourceLine,
	string sourceMember
) : IEnumerator<CoroYield>, IDisposable {
	private readonly IEnumerator<CoroYield> iterator = iterator;
	public string DebugName { get; } = debugName;
	public string SourceFile { get; } = sourceFile;
	public int SourceLine { get; } = sourceLine;
	public string SourceMember { get; } = sourceMember;

	public CoroYield Current => iterator.Current;
	object IEnumerator.Current => iterator.Current;
	public bool MoveNext() => iterator.MoveNext();
	public void Reset() => iterator.Reset();

	public void Dispose() => (iterator as IDisposable)?.Dispose();
}

internal static partial class CoroNameCleanup {
	[GeneratedRegex(@"^<(?<name>[^>]+)>d(?:__\d+)?$")]
	private static partial Regex IteratorNameRe();
	[GeneratedRegex(@"^<<(?<outer>[^>]+)>g__(?<inner>[^|>]+)\|[^>]*>d(?:__\d+)?$")]
	private static partial Regex LocalFunctionIteratorNameRe();

	public static string Clean(string s) {
		if (LocalFunctionIteratorNameRe().Match(s) is Match { Success: true } m)
			return m.Groups["outer"].Value + "." + m.Groups["inner"].Value;
		if (IteratorNameRe().Match(s) is Match { Success: true } m2)
			return m2.Groups["name"].Value;
		return s;
	}
}

public static class CoroNamingExtensions {
	extension(IEnumerator<CoroYield> iterator) {
		public NamedCoroIterator Named(
			string debugName,
			[CallerFilePath] string sourceFile = "",
			[CallerLineNumber] int sourceLine = 0,
			[CallerMemberName] string sourceMember = ""
		) => new(iterator, debugName, sourceFile, sourceLine, sourceMember);
	}
}
