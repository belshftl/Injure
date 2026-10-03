// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using BenchmarkDotNet.Loggers;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Abstractions.Modif.Il.Metadata;

namespace Injure.Mods.Abstractions.Benchmarks.Modif.Il;

public enum CorpusKind {
	/// <summary>
	/// Even selection from all the decodable bodies in corelib.
	/// </summary>
	Typical,

	/// <summary>
	/// The largest decodable bodies in corelib (by instruction count).
	/// </summary>
	Large,
}

/// <summary>
/// A fixed number of method bodies from corelib.
/// </summary>
/// <remarks>
/// <para>
/// The bodies come from the corelib currently in use, so absolute numbers change a little bit
/// between runtime versions. Ideally, compare runs from the same .NET runtime version.
/// </para>
/// </remarks>
internal sealed class Corpus : IDisposable {
	private readonly record struct Entry(MethodDefinitionHandle Handle, MethodBodyBlock Block, IlMethodBody Body);

	public const int Size = 1024;

	private readonly FileStream stream;
	private readonly PEReader pe;

	public MetadataReader Metadata { get; }
	public MethodDefinitionHandle[] Handles { get; }
	public MethodBodyBlock[] Blocks { get; }
	public IlMethodBody[] Bodies { get; }
	public int InstructionCount { get; }

	private Corpus(FileStream stream, PEReader pe, MetadataReader metadata, Entry[] picked) {
		this.stream = stream;
		this.pe = pe;
		Metadata = metadata;
		Handles = picked.Select(static p => p.Handle).ToArray();
		Blocks = picked.Select(static p => p.Block).ToArray();
		Bodies = picked.Select(static p => p.Body).ToArray();
		InstructionCount = Bodies.Sum(static b => b.Instructions.Count);
	}

	public static Corpus Load(CorpusKind kind) {
		FileStream stream = File.OpenRead(typeof(object).Assembly.Location);
		PEReader pe = new(stream);
		MetadataReader metadata = pe.GetMetadataReader();

		List<Entry> all = new();
		foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions) {
			MethodDefinition method = metadata.GetMethodDefinition(handle);
			if (method.RelativeVirtualAddress == 0)
				continue;
			MethodBodyBlock block = pe.GetMethodBody(method.RelativeVirtualAddress);
			try {
				all.Add(new Entry(handle, block, SrmMethodBodyDecoder.Decode(metadata, handle, block, default)));
			} catch {
				// the roundtrip test should cover this, testing correctness is not our problem
			}
		}
		if (all.Count < Size)
			throw new InvalidOperationException($"only decoded {all.Count} from corelib, expected at least {Size} for the corpus");

		Entry[] selection = kind switch {
			CorpusKind.Typical => Enumerable.Range(0, Size)
				.Select(i => all[(int)((long)i * all.Count / Size)])
				.ToArray(),
			CorpusKind.Large => all
				.OrderByDescending(static e => e.Body.Instructions.Count)
				.ThenBy(static e => MetadataTokens.GetRowNumber(e.Handle))
				.Take(Size)
				.ToArray(),
			_ => throw new ArgumentOutOfRangeException(nameof(kind)),
		};

		Corpus corpus = new(stream, pe, metadata, selection);
		ConsoleLogger.Default.WriteLine(
			$"// corpus {kind}: {Size} methods (of {all.Count} total), {corpus.InstructionCount} instructions"
		);
		return corpus;
	}

	public void Dispose() {
		pe.Dispose();
		stream.Dispose();
	}
}
