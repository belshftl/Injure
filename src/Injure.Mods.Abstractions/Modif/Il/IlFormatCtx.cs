// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Formats metadata into human-readable strings for failure messages.
/// </summary>
/// <remarks>
/// <para>
/// The anchor lookup is a linear scan on purpose; failure messages usually include a handful of anchors at
/// most, so building a dictionary/index would move the const onto every transaction to save basically nothing.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and formats every anchor by ID.
/// </para>
/// </remarks>
internal readonly struct IlFormatCtx {
	private readonly IlSnapshot? snapshot;

	public IlFormatCtx(IlSnapshot snapshot) {
		InternalStateException.ThrowIfNull(snapshot);
		this.snapshot = snapshot;
	}

	public string FormatAnchor(IlAnchorId anchor) {
		if (snapshot is not null)
			for (int boundary = 0; boundary < snapshot.Anchors.Length; boundary++)
				if (snapshot.Anchors[boundary] == anchor)
					return $"instruction boundary {boundary.ToString(CultureInfo.InvariantCulture)}";
		return anchor.ToString();
	}
}
