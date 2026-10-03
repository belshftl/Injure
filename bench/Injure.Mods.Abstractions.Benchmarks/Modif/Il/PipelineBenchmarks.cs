// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Abstractions.Modif.Il.Metadata;

namespace Injure.Mods.Abstractions.Benchmarks.Modif.Il;

public enum ManipulatorKind {
	/// <summary>
	/// Emits one <c>nop</c> at the start.
	/// </summary>
	NopAtStart,

	/// <summary>
	/// Matches every <c>call</c> and emits a <c>nop</c> before each.
	/// </summary>
	NopBeforeEachCall,
}

/// <summary>
/// Runs a full pipeline on the <c>Typical</c> corpus.
/// </summary>
[MemoryDiagnoser]
public class PipelineBenchmarks {
	private static readonly IlPatternElement[] callAnyOperand = [MatchIl.Call()];

	private Corpus corpus = null!;
	private IlManipulatorRegistration[] manipulators = null!;
	private MemoizingTokenResolver resolver = null!;

	[Params(1, 4, 16)]
	public int Manipulators { get; set; }

	[ParamsAllValues]
	public ManipulatorKind Kind { get; set; }

	[GlobalSetup]
	public void Setup() {
		corpus = Corpus.Load(CorpusKind.Typical);
		IlManipulator<BenchL> manipulator = Kind switch {
			ManipulatorKind.NopAtStart => static ctx => ctx.EmitAtStart(static e => e.Nop()),
			ManipulatorKind.NopBeforeEachCall => static ctx => {
				foreach (IlMatch match in ctx.MatchAll(callAnyOperand, IlProvenanceConstr.Any))
					match.EmitBefore(static e => e.Nop());
			},
			_ => throw new ArgumentOutOfRangeException(nameof(Kind)),
		};
		manipulators = Enumerable.Range(0, Manipulators)
			.Select(i => IlManipulatorRegistration.Create(IlBench.OwnerId, $"m{i}", manipulator))
			.ToArray();

		resolver = new MemoizingTokenResolver();
		foreach (IlMethodBody body in corpus.Bodies)
			_ = SrmMethodBodyEncoder.Prepare(transform(body), resolver);
	}

	[GlobalCleanup]
	public void Cleanup() => corpus.Dispose();

	/// <remarks>
	/// Do note that transform cost also includes cloning the baseline + validating the result.
	/// </remarks>
	[Benchmark(Baseline = true, OperationsPerInvoke = Corpus.Size)]
	public int Transform() {
		int total = 0;
		foreach (IlMethodBody body in corpus.Bodies)
			total += transform(body).Instructions.Count;
		return total;
	}

	/// <summary>
	/// <see cref="Transform"/> + an encode of the result, similar to the orchestrator for a patched
	/// method whose baseline is cached.
	/// </summary>
	[Benchmark(OperationsPerInvoke = Corpus.Size)]
	public int TransformAndEncode() {
		int total = 0;
		foreach (IlMethodBody body in corpus.Bodies)
			total += SrmMethodBodyEncoder.Prepare(transform(body), resolver).Size;
		return total;
	}

	private IlMethodBody transform(IlMethodBody body) =>
		IlPipeline.Transform(body, manipulators, null, null).Body;
}
