// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Text;

namespace Injure.Mods.Abstractions.Modif.Il;

[Flags]
internal enum IlPrefixFlags : byte {
	None = 0,
	Constrained = 1 << 0,
	Volatile = 1 << 1,
	Tail = 1 << 2,
	Unaligned = 1 << 3,
	ReadOnly = 1 << 4,
	No = 1 << 5,
}

[Flags]
internal enum IlSkipChecks : byte {
	None = 0,
	TypeCheck = 1 << 0,
	RangeCheck = 1 << 1,
	NullCheck = 1 << 2,
}

internal sealed record IlInstructionPrefixes {
	public IlPrefixFlags Flags { get; }
	public IlTypeRef? ConstrainedType { get; }
	public byte Alignment { get; }
	public IlSkipChecks SkipChecks { get; }

	public IlInstructionPrefixes(
		IlPrefixFlags flags,
		IlTypeRef? constrainedType,
		byte alignment,
		IlSkipChecks skipChecks
	) {
		if (flags == IlPrefixFlags.None)
			throw new InternalStateException("an IlInstructionPrefixes value must carry at least one prefix");
		if ((flags & IlPrefixFlags.Constrained) != 0 != (constrainedType is not null))
			throw new InternalStateException("constrained. prefix flag and constrained type disagree");
		if ((flags & IlPrefixFlags.Unaligned) != 0 != (alignment != 0))
			throw new InternalStateException("unaligned. prefix flag and alignment disagree");
		if ((flags & IlPrefixFlags.No) != 0 != (skipChecks != IlSkipChecks.None))
			throw new InternalStateException("no. prefix flag and skipped checks disagree");
		if (alignment is not (0 or 1 or 2 or 4))
			throw new InternalStateException($"unaligned. alignment {alignment} is not 1, 2, or 4");
		Flags = flags;
		ConstrainedType = constrainedType;
		Alignment = alignment;
		SkipChecks = skipChecks;
	}

	public bool Has(IlPrefixFlags flag) => (Flags & flag) != 0;

	public override string ToString() {
		StringBuilder sb = new();
		if (Has(IlPrefixFlags.Constrained))
			sb.Append("constrained. ").Append(ConstrainedType).Append(' ');
		if (Has(IlPrefixFlags.No))
			sb.Append("no. ").Append((byte)SkipChecks).Append(' ');
		if (Has(IlPrefixFlags.ReadOnly))
			sb.Append("readonly. ");
		if (Has(IlPrefixFlags.Tail))
			sb.Append("tail. ");
		if (Has(IlPrefixFlags.Unaligned))
			sb.Append("unaligned. ").Append(Alignment).Append(' ');
		if (Has(IlPrefixFlags.Volatile))
			sb.Append("volatile. ");
		return sb.ToString().TrimEnd();
	}
}
