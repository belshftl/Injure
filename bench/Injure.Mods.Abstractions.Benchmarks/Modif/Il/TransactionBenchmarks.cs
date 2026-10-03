// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods.Abstractions.Modif.Il;

namespace Injure.Mods.Abstractions.Benchmarks.Modif.Il;

/// <summary>
/// Transaction open/emit/commit.
/// </summary>
/// <remarks>
/// Committing changes the body, so the benchmarks that commit do a clone first; subtract
/// <see cref="Clone"/> to get the cost of just the transaction.
/// </remarks>
[MemoryDiagnoser]
public class TransactionBenchmarks {
	private static readonly IlTypeRef int32 = IlRefFactory.Type(typeof(int));

	private Corpus corpus = null!;

	[ParamsAllValues]
	public CorpusKind Kind { get; set; }

	[GlobalSetup]
	public void Setup() => corpus = Corpus.Load(Kind);

	[GlobalCleanup]
	public void Cleanup() => corpus.Dispose();

	[Benchmark(Baseline = true, OperationsPerInvoke = Corpus.Size)]
	public int Clone() {
		int total = 0;
		foreach (IlMethodBody body in corpus.Bodies)
			total += body.Clone().Instructions.Count;
		return total;
	}

	[Benchmark(OperationsPerInvoke = Corpus.Size)]
	public int OpenAndAbort() {
		int total = 0;
		foreach (IlMethodBody body in corpus.Bodies) {
			IlTransactionCore core = new(body, IlBench.OwnerId, "open", default, null);
			total += core.InstructionCount;
			core.Abort();
		}
		return total;
	}

	[Benchmark(OperationsPerInvoke = Corpus.Size)]
	public int CloneEmitNopAndCommit() {
		int total = 0;
		foreach (IlMethodBody body in corpus.Bodies) {
			IlMethodBody working = body.Clone();
			IlTransactionCore core = new(working, IlBench.OwnerId, "nop", default, null);
			core.EmitAtBoundary(0, static e => e.Nop());
			core.Commit();
			total += working.Instructions.Count;
		}
		return total;
	}

	/// <summary>
	/// Declares a local and resolves a label, which commit has to merge in on top of the instructions.
	/// </summary>
	[Benchmark(OperationsPerInvoke = Corpus.Size)]
	public int CloneEmitWithLocalAndLabelAndCommit() {
		int total = 0;
		foreach (IlMethodBody body in corpus.Bodies) {
			IlMethodBody working = body.Clone();
			IlTransactionCore core = new(working, IlBench.OwnerId, "local", default, null);
			IlLocal local = core.DeclareLocal(int32);
			IlLabel label = core.DefineLabel();
			core.EmitAtBoundary(0, e => {
				e.LdcI4(0);
				e.Stloc(local);
				e.Br(label);
				e.MarkLabel(label);
			});
			core.Commit();
			total += working.Instructions.Count;
		}
		return total;
	}
}
