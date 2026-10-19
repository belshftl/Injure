// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Analyzers.Assets;

namespace Injure.Analyzers.Tests.Assets;

public sealed class AssetBorrowValueAnalyzerTests {
	private static string[] storeKinds(string body) {
		string source = $$"""
			using System;
			using System.Collections.Generic;
			using Injure.Assets;
			public sealed class C {
				private object? field;
				public static object? Prop { get; set; }
				private readonly List<object> list = new();
				private readonly Dictionary<int, object> dict = new();
				private readonly object[] arr = new object[1];
				private static void use(object o) {}
				{{body}}
			}
			""";
		return Gen.Analyze(new AssetBorrowValueAnalyzer(), source)
			.Where(static d => d.Id == "IJ0210")
			.Select(static d => Gen.MessageArgs(d)[0])
			.ToArray();
	}

	[Theory]
	[InlineData("void M(Asset<object> a) { field = a.Borrow().Value; }", "stored in a field")]
	[InlineData("void M(Asset<object> a) { Prop = a.Borrow().Value; }", "stored in a property")]
	[InlineData("void M(Asset<object> a) { var b = a.Borrow(); var v = b.Value; var w = v; field = w; }", "stored in a field")]
	[InlineData("void M(Asset<object> a) { list.Add(a.Borrow().Value); }", "added to a collection with 'Add'")]
	[InlineData("void M(Asset<object> a) { var v = a.Borrow().Value; dict[0] = v; }", "stored through an indexer")]
	[InlineData("void M(Asset<object> a) { arr[0] = a.Borrow().Value; }", "stored in an array")]
	[InlineData("void M(Asset<object> a) { var v = a.Borrow().Value; Action f = () => use(v); }", "captured by a lambda")]
	[InlineData("void M(Asset<object> a) { var v = a.Borrow().Value; void f() => use(v); f(); }", "captured by local function 'f'")]
	[InlineData("void M(Asset<object> a, bool c) { field = c ? a.Borrow().Value : null; }", "stored in a field")]
	public static void StoresThatCanOutliveTheBorrowAreReported(string body, string expected) =>
		Assert.Equal(expected, Assert.Single(storeKinds(body)));

	[Theory]
	[InlineData("void M(Asset<object> a) { use(a.Borrow().Value); }")]
	[InlineData("object M(Asset<object> a) => a.Borrow().Value;")]
	[InlineData("void M(Asset<object> a) { var v = a.Borrow().Value; use(v); var w = v; use(w); }")]
	[InlineData("void M(Asset<object> a) { Action f = () => { var v = a.Borrow().Value; use(v); }; }")]
	[InlineData("void M(Asset<object> a) { field = a.Pin().Value; }")]
	[InlineData("void M(Asset<object> a) { object o = new(); field = o; }")]
	public static void UsesWithinTheBorrowAreFine(string body) => Assert.Empty(storeKinds(body));
}
