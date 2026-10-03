// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods.Abstractions.Modif.Il;

namespace Injure.Mods.Abstractions.Benchmarks.Modif.Il;

public enum PatternKind {
	/// <summary>
	/// <c>ret</c>, i.e. one pattern element and usually one match.
	/// </summary>
	Ret,

	/// <summary>
	/// <c>call</c> with any operand, i.e. one pattern element and usually more than one match.
	/// </summary>
	CallAnyOperand,

	/// <summary>
	/// <c>ldarg 0; ldfld</c> with any field, i.e. typical instance-field read.
	/// </summary>
	LdargLdfld,

	/// <summary>
	/// <c>ldarg 0; ldfld; ldarg 1; call</c>, i.e. longer and usually rejected one or two elements in.
	/// </summary>
	FourElements,

	/// <summary>
	/// Seven wildcards then <c>ret</c>, i.e. fairly close to worst-case without just making the
	/// pattern longer.
	/// </summary>
	WildcardsThenRet,
}

/// <summary>
/// <see cref="IlTransactionCore.MatchAll"/> over every body, per method.
/// </summary>
/// <remarks>
/// The transactions are opened ahead of time in setup; open is measured separately by
/// <see cref="TransactionBenchmarks.OpenAndAbort"/>.
/// </remarks>
[MemoryDiagnoser]
public class MatchBenchmarks {
	private Corpus corpus = null!;
	private IlTransactionCore[] cores = null!;
	private IlPatternElement[] pattern = null!;
	private readonly IlProvenanceConstr fromOwner = IlProvenanceConstr.AllFromOwner(IlBench.OwnerId);

	[ParamsAllValues]
	public CorpusKind Kind { get; set; }

	[ParamsAllValues]
	public PatternKind Pattern { get; set; }

	[GlobalSetup]
	public void Setup() {
		corpus = Corpus.Load(Kind);
		cores = corpus.Bodies
			.Select(static b => new IlTransactionCore(b, IlBench.OwnerId, "match", default, null))
			.ToArray();
		pattern = Pattern switch {
			PatternKind.Ret => [MatchIl.Ret],
			PatternKind.CallAnyOperand => [MatchIl.Call()],
			PatternKind.LdargLdfld => [MatchIl.Ldarg(0), MatchIl.Ldfld()],
			PatternKind.FourElements =>
				[MatchIl.Ldarg(0), MatchIl.Ldfld(), MatchIl.Ldarg(1), MatchIl.Call()],
			PatternKind.WildcardsThenRet => [.. Enumerable.Repeat(MatchIl.Any, 7), MatchIl.Ret],
			_ => throw new ArgumentOutOfRangeException(nameof(Pattern)),
		};
	}

	[GlobalCleanup]
	public void Cleanup() => corpus.Dispose();

	[Benchmark(Baseline = true, OperationsPerInvoke = Corpus.Size)]
	public int AnyProvenance() {
		int total = 0;
		foreach (IlTransactionCore core in cores)
			total += core.MatchAll(pattern, IlProvenanceConstr.Any).Count;
		return total;
	}

	/// <remarks>
	/// The corpus's methods have unknown provenance, so this never matches anything.
	/// </remarks>
	[Benchmark(OperationsPerInvoke = Corpus.Size)]
	public int FromOwner() {
		int total = 0;
		foreach (IlTransactionCore core in cores)
			total += core.MatchAll(pattern, fromOwner).Count;
		return total;
	}
}
