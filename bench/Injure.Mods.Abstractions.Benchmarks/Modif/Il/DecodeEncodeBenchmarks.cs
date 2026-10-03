// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Abstractions.Modif.Il.Metadata;

namespace Injure.Mods.Abstractions.Benchmarks.Modif.Il;

/// <summary>
/// Decode / stack analysis / encode.
/// </summary>
[MemoryDiagnoser]
public class DecodeEncodeBenchmarks {
	private Corpus corpus = null!;
	private MemoizingTokenResolver resolver = null!;

	[ParamsAllValues]
	public CorpusKind Kind { get; set; }

	[GlobalSetup]
	public void Setup() {
		corpus = Corpus.Load(Kind);
		resolver = new MemoizingTokenResolver();
		// warms the resolver, and leaves every body's maxstack cached, as the pipeline would
		foreach (IlMethodBody body in corpus.Bodies)
			_ = SrmMethodBodyEncoder.Prepare(body, resolver);
	}

	[GlobalCleanup]
	public void Cleanup() => corpus.Dispose();

	/// <summary>
	/// Decode from metadata, including resolving all the references in the body.
	/// </summary>
	[Benchmark(OperationsPerInvoke = Corpus.Size)]
	public int Decode() {
		int total = 0;
		for (int i = 0; i < Corpus.Size; i++) {
			MethodDefinitionHandle handle = corpus.Handles[i];
			total += SrmMethodBodyDecoder.Decode(corpus.Metadata, handle, corpus.Blocks[i], default).Instructions.Count;
		}
		return total;
	}

	/// <summary>
	/// Maxstack analysis with nothing cached, like the real encoder runs on any body it gets uncached.
	/// </summary>
	[Benchmark(OperationsPerInvoke = Corpus.Size)]
	public int AnalyzeMaxStack() {
		int total = 0;
		foreach (IlMethodBody body in corpus.Bodies) {
			body.ComputedMaxStack = null;
			total += IlMaxStackAnalyzer.Analyze(body);
		}
		return total;
	}

	/// <summary>
	/// Encode with maxstack already cached and every token already resolved, as it is after the real
	/// pipeline.
	/// </summary>
	[Benchmark(OperationsPerInvoke = Corpus.Size)]
	public int Encode() {
		int total = 0;
		foreach (IlMethodBody body in corpus.Bodies)
			total += SrmMethodBodyEncoder.Prepare(body, resolver).Size;
		return total;
	}
}
