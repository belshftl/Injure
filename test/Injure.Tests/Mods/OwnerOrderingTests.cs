// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods;

namespace Injure.Tests.Mods;

public sealed class OwnerOrderingTests {
	[Fact]
	public void LocalPriorityWorks() {
		OwnerOrderedEntry<string>[] entries = [
			new("third", "owner", "b", 2),
			new("first", "owner", "a", 0),
			new("second", "owner", "c", 1),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["first", "second", "third"], result);
	}

	[Fact]
	public void TiebreakingWithLocalIdWorks() {
		OwnerOrderedEntry<string>[] entries = [
			new("B", "owner", "b", 0),
			new("A", "owner", "a", 0),
			new("C", "owner", "c", 0),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["A", "B", "C"], result);
	}

	[Fact]
	public void BeforeOwnerWorks() {
		OwnerOrderedEntry<string>[] entries = [
			new("second", "ownerA", "a"),
			new("first", "ownerB", "b", before: [OwnerOrderingConstraint.SoftOwner("ownerA")]),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["first", "second"], result);
	}

	[Fact]
	public void AfterOwnerWorks() {
		OwnerOrderedEntry<string>[] entries = [
			new("second", "ownerA", "a", after: [OwnerOrderingConstraint.SoftOwner("ownerB")]),
			new("first", "ownerB", "b"),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["first", "second"], result);
	}

	[Fact]
	public void UnknownOwnerReferenceIsIgnoredForSoftOwner() {
		OwnerOrderedEntry<string>[] entries = [
			new("first", "ownerA", "a", before: [OwnerOrderingConstraint.SoftOwner("missing")]),
			new("second", "ownerB", "b"),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["first", "second"], result);
	}

	[Fact]
	public void UnknownOwnerReferenceThrowsForHardOwner() {
		OwnerOrderedEntry<string>[] entries = [
			new("first", "ownerA", "a", before: [OwnerOrderingConstraint.HardOwner("missing")]),
			new("second", "ownerB", "b"),
		];
		OwnerOrderingException ex = Assert.Throws<OwnerOrderingException>(() => OwnerOrderedSorter.Sort(entries));
		Assert.Contains("hard 'before' constraint", ex.Message, StringComparison.Ordinal);
		Assert.Contains("unknown owner 'missing'", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void BeforeEntryWorks() {
		OwnerOrderedEntry<string>[] entries = [
			new("second", "ownerA", "target"),
			new("first", "ownerB", "source", before: [OwnerOrderingConstraint.HardEntry("ownerA", "target")]),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["first", "second"], result);
	}

	[Fact]
	public void AfterEntryWorks() {
		OwnerOrderedEntry<string>[] entries = [
			new("second", "ownerA", "source", after: [OwnerOrderingConstraint.HardEntry("ownerB", "target")]),
			new("first", "ownerB", "target"),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["first", "second"], result);
	}

	[Fact]
	public void EntryConstraintDoesNotOrderWholeOwner() {
		OwnerOrderedEntry<string>[] entries = [
			new("ownerA first", "ownerA", "a"),
			new("ownerA second", "ownerA", "b"),
			new("ownerB", "ownerB", "a", before: [OwnerOrderingConstraint.HardEntry("ownerA", "b")]),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["ownerA first", "ownerB", "ownerA second"], result);
	}

	[Fact]
	public void UnknownEntryReferenceIsIgnoredForSoftEntry() {
		OwnerOrderedEntry<string>[] entries = [
			new("first", "ownerA", "a", before: [OwnerOrderingConstraint.SoftEntry("ownerB", "missing")]),
			new("second", "ownerB", "b"),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["first", "second"], result);
	}

	[Fact]
	public void UnknownEntryOwnerIsIgnoredForSoftEntry() {
		OwnerOrderedEntry<string>[] entries = [
			new("first", "ownerA", "a", before: [OwnerOrderingConstraint.SoftEntry("missing", "entry")]),
			new("second", "ownerB", "b"),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["first", "second"], result);
	}

	[Fact]
	public void UnknownEntryReferenceThrowsForHardEntry() {
		OwnerOrderedEntry<string>[] entries = [
			new("first", "ownerA", "a", before: [OwnerOrderingConstraint.HardEntry("ownerB", "missing")]),
			new("second", "ownerB", "b"),
		];
		OwnerOrderingException ex = Assert.Throws<OwnerOrderingException>(() => OwnerOrderedSorter.Sort(entries));
		Assert.Contains("hard 'before' constraint", ex.Message, StringComparison.Ordinal);
		Assert.Contains("unknown entry 'ownerB::missing'", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ConstraintTargetOverloadWorks() {
		var target = OwnerOrderingConstraintTarget.Entry("ownerA", "target");
		OwnerOrderedEntry<string>[] entries = [
			new("second", "ownerA", "target"),
			new("first", "ownerB", "source", before: [OwnerOrderingConstraint.Soft(target)]),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["first", "second"], result);
	}

	[Fact]
	public void DuplicateConstraintTargetWithinListThrows() {
		ArgumentException ex = Assert.Throws<ArgumentException>(() =>
			new OwnerOrderedEntry<string>(
				"item",
				"ownerA",
				"entry",
				before: [
					OwnerOrderingConstraint.SoftEntry("ownerB", "target"),
					OwnerOrderingConstraint.HardEntry("ownerB", "target"),
				]
			)
		);
		Assert.Contains("duplicate target", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void DuplicateLocalIdWithinOwnerThrows() {
		OwnerOrderedEntry<string>[] entries = [
			new("x1", "owner", "dup"),
			new("x2", "owner", "dup"),
		];
		OwnerOrderingException ex = Assert.Throws<OwnerOrderingException>(() => OwnerOrderedSorter.Sort(entries));
		Assert.Contains("duplicate LocalId", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void OwnerSelfReferenceThrows() {
		OwnerOrderedEntry<string>[] entries = [
			new("self-ref", "ownerA", "a", before: [OwnerOrderingConstraint.SoftOwner("ownerA")]),
		];
		OwnerOrderingException ex = Assert.Throws<OwnerOrderingException>(() => OwnerOrderedSorter.Sort(entries));
		Assert.Contains("self-reference", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void EntrySelfReferenceThrows() {
		OwnerOrderedEntry<string>[] entries = [
			new("self-ref", "ownerA", "a", before: [OwnerOrderingConstraint.SoftEntry("ownerA", "a")]),
		];
		OwnerOrderingException ex = Assert.Throws<OwnerOrderingException>(() => OwnerOrderedSorter.Sort(entries));
		Assert.Contains("entry 'ownerA::a'", ex.Message, StringComparison.Ordinal);
		Assert.Contains("self-reference", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void SimpleOwnerCycleThrows() {
		OwnerOrderedEntry<string>[] entries = [
			new("cycle 1", "ownerA", "a", before: [OwnerOrderingConstraint.SoftOwner("ownerB")]),
			new("cycle 2", "ownerB", "b", before: [OwnerOrderingConstraint.SoftOwner("ownerA")]),
		];
		OwnerOrderingException ex = Assert.Throws<OwnerOrderingException>(() => OwnerOrderedSorter.Sort(entries));
		Assert.Contains("unsatisfiable", ex.Message, StringComparison.Ordinal);
		Assert.Contains("ownerA -> ownerB -> ownerA", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void LongerOwnerCycleThrows() {
		OwnerOrderedEntry<string>[] entries = [
			new("cycle 1", "ownerA", "a", before: [OwnerOrderingConstraint.SoftOwner("ownerB")]),
			new("cycle 2", "ownerB", "b", before: [OwnerOrderingConstraint.SoftOwner("ownerC")]),
			new("cycle 3", "ownerC", "c", before: [OwnerOrderingConstraint.SoftOwner("ownerD")]),
			new("cycle 4", "ownerD", "d", before: [OwnerOrderingConstraint.SoftOwner("ownerA")]),
		];
		OwnerOrderingException ex = Assert.Throws<OwnerOrderingException>(() => OwnerOrderedSorter.Sort(entries));
		Assert.Contains("unsatisfiable", ex.Message, StringComparison.Ordinal);
		Assert.Contains("ownerA -> ownerB -> ownerC -> ownerD -> ownerA", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void SimpleEntryCycleThrows() {
		OwnerOrderedEntry<string>[] entries = [
			new("cycle 1", "ownerA", "a", before: [OwnerOrderingConstraint.SoftEntry("ownerB", "b")]),
			new("cycle 2", "ownerB", "b", before: [OwnerOrderingConstraint.SoftEntry("ownerA", "a")]),
		];
		OwnerOrderingException ex = Assert.Throws<OwnerOrderingException>(() => OwnerOrderedSorter.Sort(entries));
		Assert.Contains("unsatisfiable", ex.Message, StringComparison.Ordinal);
		Assert.Contains("ownerA::a -> ownerB::b -> ownerA::a", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void LongerEntryCycleThrows() {
		OwnerOrderedEntry<string>[] entries = [
			new("cycle 1", "ownerA", "a", before: [OwnerOrderingConstraint.SoftEntry("ownerB", "b")]),
			new("cycle 2", "ownerB", "b", before: [OwnerOrderingConstraint.SoftEntry("ownerC", "c")]),
			new("cycle 3", "ownerC", "c", before: [OwnerOrderingConstraint.SoftEntry("ownerD", "d")]),
			new("cycle 4", "ownerD", "d", before: [OwnerOrderingConstraint.SoftEntry("ownerA", "a")]),
		];
		OwnerOrderingException ex = Assert.Throws<OwnerOrderingException>(() => OwnerOrderedSorter.Sort(entries));
		Assert.Contains("unsatisfiable", ex.Message, StringComparison.Ordinal);
		Assert.Contains(
			"ownerA::a -> ownerB::b -> ownerC::c -> ownerD::d -> ownerA::a",
			ex.Message,
			StringComparison.Ordinal
		);
	}

	[Fact]
	public void EntryConstraintConsistentWithLocalPriorityWorks() {
		OwnerOrderedEntry<string>[] entries = [
			new("second", "owner", "b", 1),
			new("first", "owner", "a", 0, before: [OwnerOrderingConstraint.HardEntry("owner", "b")]),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["first", "second"], result);
	}

	[Fact]
	public void EntryConstraintContradictingLocalPriorityThrows() {
		OwnerOrderedEntry<string>[] entries = [
			new("first", "owner", "a", 0),
			new("second", "owner", "b", 1, before: [OwnerOrderingConstraint.SoftEntry("owner", "a")]),
		];
		OwnerOrderingException ex = Assert.Throws<OwnerOrderingException>(() => OwnerOrderedSorter.Sort(entries));
		Assert.Contains("unsatisfiable", ex.Message, StringComparison.Ordinal);
		Assert.Contains("owner::a", ex.Message, StringComparison.Ordinal);
		Assert.Contains("owner::b", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void OwnerOrderingThenLocalPriorityWorks() {
		OwnerOrderedEntry<string>[] entries = [
			new("ownerA second", "ownerA", "a", 0),
			new("ownerA first", "ownerA", "z", -1),
			new("ownerB first", "ownerB", "b", 0, before: [OwnerOrderingConstraint.SoftOwner("ownerA")]),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["ownerB first", "ownerA first", "ownerA second"], result);
	}

	[Fact]
	public void OwnerConstraintStillOrdersWholeOwnerWhenEntryConstraintsExist() {
		OwnerOrderedEntry<string>[] entries = [
			new("ownerA first", "ownerA", "a"),
			new("ownerA second", "ownerA", "b"),
			new(
				"ownerB first",
				"ownerB",
				"a",
				before: [
					OwnerOrderingConstraint.SoftOwner("ownerA"),
					OwnerOrderingConstraint.SoftEntry("ownerB", "b"),
				]
			),
			new("ownerB second", "ownerB", "b"),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(
			["ownerB first", "ownerB second", "ownerA first", "ownerA second"],
			result
		);
	}

	[Fact]
	public void MixedOwnerAndEntryConstraintsCanInterleaveUnrelatedOwner() {
		OwnerOrderedEntry<string>[] entries = [
			new("ownerA first", "ownerA", "a"),
			new("ownerA second", "ownerA", "b"),
			new("ownerB", "ownerB", "a", before: [OwnerOrderingConstraint.SoftEntry("ownerA", "b")]),
			new("ownerC", "ownerC", "a", after: [OwnerOrderingConstraint.SoftOwner("ownerA")]),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["ownerA first", "ownerB", "ownerA second", "ownerC"], result);
	}
}
