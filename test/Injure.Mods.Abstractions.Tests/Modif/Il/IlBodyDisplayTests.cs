// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Injure.Mods.Abstractions.Modif.Il;

namespace Injure.Mods.Abstractions.Tests.Modif.Il;

public sealed class IlBodyDisplayTests {
	private static IlTransactionCore open(IlMethodBody body, string ownerId = "ModB", string? localId = "check") =>
		new(body, ownerId, localId, default, null);

	private static string[] lines(string text) =>
		text.Split('\n').Select(static l => l.TrimEnd('\r')).ToArray();

	private static string[] code(string text) =>
		lines(text).SkipWhile(static l => l != "code:").Skip(1).Where(static l => l.Length != 0).ToArray();

	/// <summary>
	/// A code line without its gutter and provenance comment.
	/// </summary>
	private static string instructionOf(string line) {
		int comment = line.IndexOf("//", StringComparison.Ordinal);
		return line[8..comment].TrimEnd();
	}

	private static IlMethodBody baseline(BodyBuilder builder, ImmutableArray<IlTypeRef> locals = default, bool initLocals = true) =>
		builder.Build(locals: locals, initLocals: initLocals, baselineProvenance: new InternalIlProvenance("TestGame", null));

	// ==========================================================================================
	// layout
	[Fact]
	public static void ListingMatchesTheDocumentedLayout() {
		IlMethodBody body = baseline(new BodyBuilder().Nop().Nop().Ret(), [IlTest.Int32]);
		IlTransactionCore first = open(body, "ModA", "dispatch");
		first.EmitAtBoundary(1, static e => e.LdcI4(13));
		first.Commit();

		IlTransactionCore core = open(body);
		IlLocal local = core.DeclareLocal(IlTest.Object);
		IlLabel target = core.DefineLabel();
		core.EmitAtBoundary(2, e => {
			e.Ldnull();
			e.Stloc(local);
			e.Br(target);
			e.MarkLabel(target);
		});

		Assert.Equal(
			"""
			IL for 'void object::Target()', in transaction 'ModB::check'
			locals (zeroed):
			     0. int32  // TestGame
			+    1. object // ModB

			code:
			     0. nop       // TestGame
			     1. ldc.i4 13 // ModA
			+       ldnull    // ModB
			+       stloc 1   // ModB
			+       br L0     // ModB
			L0:
			     2. nop       // TestGame
			     3. ret       // TestGame

			""".ReplaceLineEndings(),
			core.Display(default)
		);
	}

	[Fact]
	public static void UnknownProvenanceStringIsAsExpected() {
		string text = IlBodyDisplay.Format(new BodyBuilder().Ret().Build(), default);

		Assert.EndsWith("// <unknown provenance>", code(text).Single());
	}

	[Fact]
	public static void ALocallessMethodSaysSo() =>
		Assert.Contains("locals: none", lines(IlBodyDisplay.Format(new BodyBuilder().Ret().Build(), default)));

	[Fact]
	public static void UninitLocalsAreLabeledAsSuch() =>
		Assert.Contains(
			"locals (uninit):",
			lines(IlBodyDisplay.Format(new BodyBuilder().Ret().Build(locals: [IlTest.Int32], initLocals: false), default))
		);

	[Fact]
	public static void FirstLocalOfALocallessMethodIsListedAsZeroed() {
		IlTransactionCore core = open(new BodyBuilder().Ret().Build(initLocals: false));
		_ = core.DeclareLocal(IlTest.Int32);

		Assert.Contains("locals (zeroed):", lines(core.Display(default)));
	}

	[Fact]
	public static void TheGutterWidensForFiveDigitIndices() {
		BodyBuilder builder = new();
		for (int i = 0; i < 10000; i++)
			builder.Nop();
		string[] rows = code(IlBodyDisplay.Format(builder.Ret().Build(), default));

		Assert.Equal("      0. nop", rows[0][..12]);
		Assert.Equal("  10000. ret", rows[^1][..12]);
	}

	[Fact]
	public static void OverlongInstructionsDontWidenTheCommentColumn() {
		IlMethodBody body = new BodyBuilder()
			.Add(ILOpCode.Ldstr, new IlStringOperand(new string('x', 100)))
			.Pop()
			.Ret()
			.Build();
		string[] rows = code(IlBodyDisplay.Format(body, default));

		// the long row gets a single space, the rest align to the longest among themselves
		Assert.Contains("\" //", rows[0], StringComparison.Ordinal);
		Assert.Equal(rows[1].IndexOf("//", StringComparison.Ordinal), rows[2].IndexOf("//", StringComparison.Ordinal));
		Assert.Equal(8 + "pop".Length + 1, rows[1].IndexOf("//", StringComparison.Ordinal));
	}

	[Fact]
	public static void LocalsAndCodeAlignTheirCommentsSeparately() {
		IlNamedTypeRef longType = IlTest.Named("Some.Very.Long.Namespace", "WithAnEquallyLongTypeName", IlNamedTypeKind.Class);
		string text = IlBodyDisplay.Format(new BodyBuilder().Ret().Build(locals: [longType]), default);

		string local = lines(text).Single(static l => l.Contains("WithAnEquallyLongTypeName", StringComparison.Ordinal));
		string ret = code(text).Single();
		Assert.Equal("     0. ret // <unknown provenance>", ret);
		Assert.EndsWith("WithAnEquallyLongTypeName // <unknown provenance>", local);
	}

	// ==========================================================================================
	// numbering and targets
	[Fact]
	public static void ExistingRowsKeepTheirSnapshotIndicesAroundInsertions() {
		IlTransactionCore core = open(new BodyBuilder().Nop().Nop().Ret().Build());
		core.EmitAtBoundary(1, static e => { e.Nop(); e.Nop(); });

		string[] rows = code(core.Display(default));

		Assert.StartsWith("     0.", rows[0]);
		Assert.StartsWith("+      ", rows[1]);
		Assert.StartsWith("+      ", rows[2]);
		Assert.StartsWith("     1.", rows[3]);
		Assert.StartsWith("     2.", rows[4]);
	}

	[Fact]
	public static void ExistingAndPendingTargetsShareOneNamingInListingOrder() {
		// br 2; nop; ret, with a pending branch to a label marked before the nop
		IlTransactionCore core = open(new BodyBuilder().Br(2).Nop().Ret().Build());
		IlLabel label = core.DefineLabel();
		core.EmitAtBoundary(1, e => { e.Br(label); e.MarkLabel(label); });

		string[] rows = code(core.Display(default));

		Assert.Equal(
			["br L1", "br L0", "L0:", "nop", "L1:", "ret"],
			rows.Select(static r => r.StartsWith('L') ? r : instructionOf(r)).ToArray()
		);
	}

	[Fact]
	public static void ABranchToTheEndOfTheBodyGetsALabelAfterTheLastRow() {
		string[] rows = code(IlBodyDisplay.Format(new BodyBuilder().Ret().Br(2).Build(), default));

		Assert.Equal("L0:", rows[^1]);
	}

	[Fact]
	public static void AnUnmarkedLabelIsShownAsSuch() {
		IlTransactionCore core = open(new BodyBuilder().Ret().Build());
		IlLabel label = core.DefineLabel();
		core.EmitAtBoundary(0, e => e.Br(label));

		Assert.Equal("br <unmarked label>", instructionOf(code(core.Display(default))[0]));
	}

	// ==========================================================================================
	// exception regions
	private static BodyBuilder tryFinally() {
		// try { leave 3 } finally { endfinally } nop; ret
		BodyBuilder builder = new();
		builder.Leave(3).Add(ILOpCode.Endfinally).Nop().Ret();
		builder.ExceptionRegion(IlExceptionRegionKind.Finally, 0, 1, 1, 2);
		return builder;
	}

	[Fact]
	public static void ATryFinallyIsRenderedAsBlocks() {
		string[] rows = code(IlBodyDisplay.Format(tryFinally().Build(), default));

		Assert.Equal(
			[".try {", "leave L0", "} finally {", "endfinally", "}", "nop", "L0:", "ret"],
			rows.Select(static r => r.Contains("//", StringComparison.Ordinal) ? instructionOf(r) : r).ToArray()
		);
	}

	[Fact]
	public static void AnInsertionAtTryStartIsRenderedOutsideTheTry() {
		IlTransactionCore core = open(tryFinally().Build());
		core.EmitAtBoundary(0, static e => e.Nop());

		string[] rows = code(core.Display(default));

		Assert.StartsWith("+", rows[0]);
		Assert.Equal(".try {", rows[1]);
	}

	[Fact]
	public static void AnInsertionAtTryEndIsRenderedInsideTheTry() {
		IlTransactionCore core = open(tryFinally().Build());
		core.EmitAtBoundary(1, static e => e.Nop());

		string[] rows = code(core.Display(default));

		Assert.StartsWith("+", rows[2]);
		Assert.Equal("} finally {", rows[3]);
	}

	[Fact]
	public static void CatchClausesSharingATryRenderOneTryBlock() {
		// try { leave 5 } catch A { pop; leave 5 } catch B { pop; leave 5 } ret
		IlNamedTypeRef other = IlTest.Named("System", "InvalidOperationException", IlNamedTypeKind.Class);
		BodyBuilder builder = new();
		builder.Leave(5).Pop().Leave(5).Pop().Leave(5).Ret();
		builder.ExceptionRegion(IlExceptionRegionKind.Catch, 0, 1, 1, 3);
		builder.ExceptionRegion(IlExceptionRegionKind.Catch, 0, 1, 3, 5);

		string[] rows = code(IlBodyDisplay.Format(builder.Build(), default));

		Assert.Single(rows, static r => r == ".try {");
		Assert.Equal(2, rows.Count(static r => r == "} catch System.Exception {"));
		Assert.Equal("}", rows[^3]);
	}

	[Fact]
	public static void NestedRegionsCloseInnerFirstAndIndent() {
		// try { try { leave 3 } finally { endfinally } leave 5 } finally { endfinally } ret
		BodyBuilder builder = new();
		builder.Leave(2).Add(ILOpCode.Endfinally).Leave(4).Add(ILOpCode.Endfinally).Ret();
		builder.ExceptionRegion(IlExceptionRegionKind.Finally, 0, 1, 1, 2);
		builder.ExceptionRegion(IlExceptionRegionKind.Finally, 0, 3, 3, 4);

		string[] rows = code(IlBodyDisplay.Format(builder.Build(), default));

		Assert.Equal(
			[".try {", "  .try {", "leave L0", "  } finally {", "endfinally", "  }", "L0:", "leave L1", "} finally {", "endfinally", "}", "L1:", "ret"],
			rows.Select(static r => r.Contains("//", StringComparison.Ordinal) ? instructionOf(r) : r).ToArray()
		);
	}

	// ==========================================================================================
	// consistency with commit
	[Fact]
	public static void FormattingDoesntTouchTheBody() {
		IlMethodBody body = tryFinally().Build();
		IlTransactionCore core = open(body);
		core.EmitAtBoundary(1, static e => e.Nop());

		string before = core.Display(default);
		string again = core.Display(default);
		core.Abort();

		Assert.Equal(before, again);
		Assert.Equal(4, body.Instructions.Count);
	}

	[Fact]
	public static void ThePreviewListsWhatCommitProduces() {
		IlMethodBody body = tryFinally().Build();
		IlTransactionCore core = open(body);
		IlLabel label = core.DefineLabel();
		core.EmitAtBoundary(0, e => { e.Br(label); e.Nop(); });
		core.EmitAtBoundary(2, e => { e.MarkLabel(label); e.Nop(); });

		string[] preview = code(core.Display(default));
		core.Commit();
		string[] committed = code(IlBodyDisplay.Format(body, default));

		static string strip(string r) => r.Contains("//", StringComparison.Ordinal) ? instructionOf(r) : r;
		Assert.Equal(preview.Select(strip), committed.Select(strip));
	}

	// ==========================================================================================
	// color option
	[Fact]
	public static void PlainTextHasNoEscapes() =>
		Assert.DoesNotContain('\x1b', IlBodyDisplay.Format(tryFinally().Build(), default));

	[Fact]
	public static void ColorHasInsertedInstrRowsGreenAndCommentsFaint() {
		IlTransactionCore core = open(new BodyBuilder().Ret().Build());
		core.EmitAtBoundary(0, static e => e.Nop());

		string inserted = code(core.Display(new IlFormatOptions { Color = true }))[0];

		Assert.StartsWith("\x1b[0;32m+       nop", inserted);
		Assert.EndsWith("\x1b[0;2m// ModB\x1b[0m", inserted);
	}

	[Fact]
	public static void ColorHasInsertedLocalRowsGreenAndCommentsFaint() {
		IlTransactionCore core = open(new BodyBuilder().Ret().Build());
		_ = core.DeclareLocal(IlTest.Int32);

		string inserted = lines(core.Display(new IlFormatOptions { Color = true })).Single(static l => l.Contains("int32", StringComparison.Ordinal));

		Assert.StartsWith("\x1b[0;32m+    \x1b[0;1;32m0\x1b[0;32m. int32", inserted);
		Assert.EndsWith("\x1b[0;2m// ModB\x1b[0m", inserted);
	}

	[Fact]
	public static void ColorLeavesBaselineRowsUncoloredButBold() {
		using FileStream stream = File.OpenRead(typeof(IlFixture.Mechanism).Assembly.Location);
		using PEReader pe = new(stream);
		IlMethodBody body = Fixture.Decode(pe, pe.GetMetadataReader(), "Mechanism", "Sizeof", new InternalIlProvenance("TestGame", null));

		string row = code(IlBodyDisplay.Format(body, new IlFormatOptions { Color = true }))[0];

		Assert.StartsWith("     \x1b[0;1m0\x1b[0m. sizeof", row);
	}

	[Fact]
	public static void ColorHasStableOwnerColors() {
		static string color(string row) => row[..row.IndexOf('m')];

		IlMethodBody body = new BodyBuilder().Ret().Build();
		IlTransactionCore first = open(body, "ModA", "a");
		first.EmitAtBoundary(0, static e => { e.Nop(); e.Nop(); });
		first.Commit();

		string[] rows = code(IlBodyDisplay.Format(body, new IlFormatOptions { Color = true }));

		Assert.Equal(color(rows[0]), color(rows[1]));
		Assert.NotEqual(color(rows[0]), color(rows[2]));
	}

	[Fact]
	public static void ColorGivesALocalItsOwnersInstructionColor() {
		static string color(string row) {
			int start = row.IndexOf("\x1b[0;1;", StringComparison.Ordinal) + "\x1b[0;1;".Length;
			return row[start..row.IndexOf('m', start)];
		}

		IlMethodBody body = baseline(new BodyBuilder().Ret(), [IlTest.Int32]);
		IlTransactionCore core = open(body, "ModA", "dispatch");
		IlLocal local = core.DeclareLocal(IlTest.Object);
		core.EmitAtBoundary(0, e => { e.Ldloc(local); e.Pop(); });
		core.Commit();

		string text = IlBodyDisplay.Format(body, new IlFormatOptions { Color = true });
		string declared = lines(text).Single(static l => l.Contains("m. object", StringComparison.Ordinal));
		string emitted = code(text)[0];

		string style = color(emitted);
		Assert.Equal(style, color(declared));
		Assert.Contains($"\x1b[0;{style}m. object", declared, StringComparison.Ordinal);
	}

	[Fact]
	public static void ColorLeavesBaselineLocalsUncoloredAcrossCommits() {
		IlMethodBody body = baseline(new BodyBuilder().Ret(), [IlTest.Int32]);
		IlTransactionCore core = open(body, "ModA", "dispatch");
		_ = core.DeclareLocal(IlTest.Object);
		core.Commit();

		string local = lines(IlBodyDisplay.Format(body.Clone(), new IlFormatOptions { Color = true }))
			.Single(static l => l.Contains("int32", StringComparison.Ordinal));

		Assert.StartsWith("     \x1b[0;1m0\x1b[0m. int32", local);
	}

	// ==========================================================================================
	// misc options
	[Fact]
	public static void ListingWithProvenanceLocalIdsMatchesToo() {
		IlMethodBody body = baseline(new BodyBuilder().Nop().Nop().Ret(), [IlTest.Int32]);
		IlTransactionCore first = open(body, "ModA", "dispatch");
		first.EmitAtBoundary(1, static e => e.LdcI4(13));
		first.Commit();

		IlTransactionCore core = open(body);
		IlLocal local = core.DeclareLocal(IlTest.Object);
		IlLabel target = core.DefineLabel();
		core.EmitAtBoundary(2, e => {
			e.Ldnull();
			e.Stloc(local);
			e.Br(target);
			e.MarkLabel(target);
		});

		Assert.Equal(
			"""
			IL for 'void object::Target()', in transaction 'ModB::check'
			locals (zeroed):
			     0. int32  // TestGame
			+    1. object // ModB::check

			code:
			     0. nop       // TestGame
			     1. ldc.i4 13 // ModA::dispatch
			+       ldnull    // ModB::check
			+       stloc 1   // ModB::check
			+       br L0     // ModB::check
			L0:
			     2. nop       // TestGame
			     3. ret       // TestGame

			""".ReplaceLineEndings(),
			core.Display(new IlFormatOptions { IncludeProvenanceLocalIds = true })
		);
	}

	[Fact]
	public static void AddLeadingNewlineWorks() {
		string text = IlBodyDisplay.Format(new BodyBuilder().Ret().Build(), new IlFormatOptions { AddLeadingNewline = true });

		Assert.StartsWith(Environment.NewLine, text);
	}
}
