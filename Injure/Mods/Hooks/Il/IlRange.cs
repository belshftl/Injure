// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Hooks.Il;

internal readonly struct IlRange {
	public int Start { get; }
	public int End { get; }
	public int Length => End - Start;

	public IlRange(int start, int end) {
		if (start > end)
			throw new InternalStateException("IlRange has its start past its end");
		Start = start;
		End = end;
	}
}
