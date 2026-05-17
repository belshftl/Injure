// SPDX-License-Identifier: MIT

namespace Injure.Weaver.Model;

public readonly struct Options {
	public required string InputPath { get; init; }
	public required string OutputPath { get; init; }
	public required string OwnerID { get; init; }
	public string? HooksRoot { get; init; }
	public string? RawHooksRoot { get; init; }
}
