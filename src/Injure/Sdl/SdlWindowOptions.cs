// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Sdl;

/// <summary>
/// Initial window size state.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Normal"/>.
/// </remarks>
[ClosedEnum]
public readonly partial struct SdlWindowMode {
	public enum Case {
		Normal,
		Minimized,
		Maximized,
	}
}

/// <summary>
/// Initial window position.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>.
/// </remarks>
public readonly struct SdlWindowPosition : IEquatable<SdlWindowPosition> {
	private enum Kind : byte {
		Undefined,
		Centered,
		Explicit,
	}

	private readonly Kind kind;
	private readonly int x;
	private readonly int y;

	private SdlWindowPosition(Kind kind, int x, int y) {
		this.kind = kind;
		this.x = x;
		this.y = y;
	}

	/// <summary>
	/// Lets the window manager / SDL choose the position.
	/// </summary>
	public static SdlWindowPosition Undefined => default;

	/// <summary>
	/// Centers the window on the primary display.
	/// </summary>
	public static SdlWindowPosition Centered => new(Kind.Centered, 0, 0);

	/// <summary>
	/// Places the window at the given position in screen coordinates.
	/// </summary>
	public static SdlWindowPosition At(int x, int y) => new(Kind.Explicit, x, y);

	/// <summary>
	/// Gets the coordinates if this is an explicit position created with <see cref="At(int, int)"/>.
	/// </summary>
	public bool TryGetExplicit(out int x, out int y) {
		x = this.x;
		y = this.y;
		return kind == Kind.Explicit;
	}

	internal (int X, int Y) ToSdl() => kind switch {
		Kind.Undefined => (unchecked((int)SDL3.SDL.WindowPosUndefinedMask), unchecked((int)SDL3.SDL.WindowPosUndefinedMask)),
		Kind.Centered => (unchecked((int)SDL3.SDL.WindowPosCenteredMask), unchecked((int)SDL3.SDL.WindowPosCenteredMask)),
		Kind.Explicit => (x, y),
		_ => throw InternalStateException.BadOpenEnum(kind),
	};

	public bool Equals(SdlWindowPosition other) => kind == other.kind && x == other.x && y == other.y;
	public override bool Equals(object? obj) => obj is SdlWindowPosition other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(kind, x, y);
	public static bool operator ==(SdlWindowPosition left, SdlWindowPosition right) => left.Equals(right);
	public static bool operator !=(SdlWindowPosition left, SdlWindowPosition right) => !left.Equals(right);

	public override string ToString() => kind switch {
		Kind.Explicit => $"({x}, {y})",
		_ => kind.ToString(),
	};
}

/// <summary>
/// Options for <see cref="SdlWindow.Create(SdlInstance, in SdlWindowOptions)"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid, since it has a <see langword="null"/> title and
/// a zero size.
/// </remarks>
public readonly struct SdlWindowOptions {
	/// <summary>
	/// Window title.
	/// </summary>
	public required string Title { get; init; }

	/// <summary>
	/// Width in screen coordinates; must be positive.
	/// </summary>
	public required int Width { get; init; }

	/// <summary>
	/// Height in screen coordinates; must be positive.
	/// </summary>
	public required int Height { get; init; }

	/// <summary>
	/// Initial size state. Must be <see cref="SdlWindowMode.Normal"/> if <see cref="Fullscreen"/> is
	/// set.
	/// </summary>
	public SdlWindowMode Mode { get; init; } = SdlWindowMode.Normal;

	/// <summary>
	/// Initial position.
	/// </summary>
	public SdlWindowPosition Position { get; init; } = SdlWindowPosition.Undefined;

	public bool Visible { get; init; } = true;
	public bool Resizable { get; init; } = true;
	public bool Borderless { get; init; } = false;
	public bool Fullscreen { get; init; } = false;

	/// <summary>
	/// Whether to request a full-resolution drawable on HiDPI displays
	/// (<c>SDL_PROP_WINDOW_CREATE_HIGH_PIXEL_DENSITY_BOOLEAN</c>).
	/// </summary>
	public bool HighPixelDensity { get; init; } = true;

	public SdlWindowOptions() {
	}

	internal void Validate(string paramName) {
		if (Title is null)
			throw new ArgumentException("title must not be null", paramName);
		if (Width <= 0 || Height <= 0)
			throw new ArgumentException($"size must be positive, got {Width}x{Height}", paramName);
		if (Fullscreen && Mode != SdlWindowMode.Normal)
			throw new ArgumentException("fullscreen windows must use SdlWindowMode.Normal", paramName);
	}
}
