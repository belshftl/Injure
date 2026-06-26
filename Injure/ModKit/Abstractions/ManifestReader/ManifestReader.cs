// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Injure.ModKit.Abstractions.ManifestReader;

public static class ManifestReader {
	private enum ModPackageKind {
		Content,
		Code,
	}

	public static ModManifest Parse(SourceText source) => Parse(LocatedJsonParser.Parse(source));

	public static ModManifest Parse(JNode rootNode) {
		JsonObjectReader root = new(rootNode);
		root.RejectUnknownProperties(
			"schema",
			"type",
			"id",
			"version",
			"reloadable",
			"live-reloadable",
			"display-name",
			"description",
			"license-spdx",
			"game",
			"relationships",
			"assets",
			"native-libraries",
			"entry-assembly",
			"contract-assemblies"
		);

		int schema = root.RequiredInt("schema");
		if (schema != 0)
			throw err(root.RequiredNode("schema"), $"unsupported schema {schema}");

		ModPackageKind type = root.RequiredEnum<ModPackageKind>("type");

		string ownerID = root.RequiredString("id");
		validateOwnerID(ownerID, root.RequiredNode("id"));

		Semver version = readSemver(root.RequiredNode("version"), root.RequiredString("version"), "version");

		bool reloadable = root.OptionalBool("reloadable", defaultValue: false);
		bool liveReloadable = root.OptionalBool("live-reloadable", defaultValue: false);

		if (!reloadable && liveReloadable)
			throw err(root.RequiredNode("live-reloadable"), "live-reloadable = true is only valid together with reloadable = true");
		if (type == ModPackageKind.Content && root.Has("live-reloadable"))
			throw err(root.RequiredNode("live-reloadable"), "live-reloadable is only valid for code mods");

		string? displayName = root.OptionalString("display-name");
		string? description = root.OptionalString("description");
		string? licenseSpdx = root.OptionalString("license-spdx");

		GameCompatibilityManifest game = readGame(root.OptionalObject("game"));
		IReadOnlyList<ModRelationshipManifest> relationships = readRelationships(root.OptionalArray("relationships"));
		ModAssetsManifest assets = readAssets(root.OptionalObject("assets"));
		IReadOnlyList<ModNativeLibraryManifest> nativeLibraries = readNativeLibraries(root.OptionalArray("native-libraries"));

		if (reloadable && nativeLibraries.Count != 0)
			throw err(root.RequiredNode("native-libraries"), "native-libraries must be empty for reloadable mods as they cannot package native libraries");

		return type switch {
			ModPackageKind.Content => parseContent(
				root,
				ownerID,
				version,
				reloadable,
				displayName,
				description,
				licenseSpdx,
				game,
				relationships,
				assets,
				nativeLibraries
			),
			ModPackageKind.Code => parseCode(
				root,
				ownerID,
				version,
				reloadable,
				liveReloadable,
				displayName,
				description,
				licenseSpdx,
				game,
				relationships,
				assets,
				nativeLibraries
			),
			_ => throw new UnreachableException(),
		};
	}

	private static ContentModManifest parseContent(
		JsonObjectReader root,
		string ownerID,
		Semver version,
		bool reloadable,
		string? displayName,
		string? description,
		string? licenseSpdx,
		GameCompatibilityManifest game,
		IReadOnlyList<ModRelationshipManifest> relationships,
		ModAssetsManifest assets,
		IReadOnlyList<ModNativeLibraryManifest> nativeLibraries
	) {
		rejectIfPresent(root, "entry-assembly", "entry-assembly is only valid for code mods");
		rejectIfPresent(root, "contract-assemblies", "contract-assemblies is only valid for code mods");
		return new ContentModManifest {
			OwnerID = ownerID,
			Version = version,
			Reloadable = reloadable,
			DisplayName = displayName,
			Description = description,
			LicenseSpdx = licenseSpdx,
			Game = game,
			Relationships = relationships,
			Assets = assets,
			NativeLibraries = nativeLibraries,
		};
	}

	private static CodeModManifest parseCode(
		JsonObjectReader root,
		string ownerID,
		Semver version,
		bool reloadable,
		bool liveReloadable,
		string? displayName,
		string? description,
		string? licenseSpdx,
		GameCompatibilityManifest game,
		IReadOnlyList<ModRelationshipManifest> relationships,
		ModAssetsManifest assets,
		IReadOnlyList<ModNativeLibraryManifest> nativeLibraries
	) {
		string entryAssembly = root.RequiredString("entry-assembly");
		validateRelativePath(entryAssembly, root.RequiredNode("entry-assembly"), "entry assembly");
		IReadOnlyList<string> contractAssemblies = readContractAssemblies(root.OptionalArray("contract-assemblies"));
		return new CodeModManifest {
			OwnerID = ownerID,
			Version = version,
			Reloadable = reloadable,
			DisplayName = displayName,
			Description = description,
			LicenseSpdx = licenseSpdx,
			Game = game,
			Relationships = relationships,
			Assets = assets,
			NativeLibraries = nativeLibraries,
			EntryAssembly = entryAssembly,
			LiveReloadable = liveReloadable,
			ContractAssemblies = contractAssemblies,
		};
	}

	private static GameCompatibilityManifest readGame(JsonObjectReader? game) {
		if (game is null)
			return new GameCompatibilityManifest {
				TargetVersion = null,
				TargetBuildMvid = null,
			};

		game.RejectUnknownProperties(
			"target-version",
			"target-build-mvid"
		);

		Semver? targetVersion = null;
		if (game.Has("target-version"))
			targetVersion = readSemver(game.RequiredNode("target-version"), game.RequiredString("target-version"), "target game version");

		Guid? targetBuildMvid = null;
		if (game.Has("target-build-mvid")) {
			string raw = game.RequiredString("target-build-mvid");
			if (!Guid.TryParse(raw, out Guid guid))
				throw err(game.RequiredNode("target-build-mvid"), $"invalid target-build-mvid '{raw}'");
			targetBuildMvid = guid;
		}

		return new GameCompatibilityManifest {
			TargetVersion = targetVersion,
			TargetBuildMvid = targetBuildMvid,
		};
	}

	private static IReadOnlyList<ModRelationshipManifest> readRelationships(IReadOnlyList<JNode> nodes) {
		if (nodes.Count == 0)
			return Array.Empty<ModRelationshipManifest>();
		List<ModRelationshipManifest> result = new(nodes.Count);
		HashSet<(string OwnerID, ModRelationshipKind Kind)> seenExact = new();
		foreach (JNode node in nodes) {
			JsonObjectReader rel = new(node);
			rel.RejectUnknownProperties(
				"id",
				"kind",
				"version",
				"description"
			);

			string ownerID = rel.RequiredString("id");
			validateOwnerID(ownerID, rel.RequiredNode("id"));

			ModRelationshipKind kind = ModRelationshipKind.Enum.FromTag(rel.RequiredEnum<ModRelationshipKind.Case>("kind"));

			Semver? version = null;
			if (kind.Tag is ModRelationshipKind.Case.RequiresSelfAfter or ModRelationshipKind.Case.RequiresSelfBefore) {
				version = readSemver(rel.RequiredNode("version"), rel.RequiredString("version"), "relationship version");
			} else if (kind.Tag is ModRelationshipKind.Case.IfPresentSelfAfter or ModRelationshipKind.Case.IfPresentSelfBefore) {
				if (rel.Has("version"))
					version = readSemver(rel.RequiredNode("version"), rel.RequiredString("version"), "relationship version");
			} else if (kind == ModRelationshipKind.Conflicts) {
				if (rel.Has("version"))
					version = readSemver(rel.RequiredNode("version"), rel.RequiredString("version"), "relationship version");
			} else {
				throw new UnreachableException();
			}

			string? description = rel.OptionalString("description");

			if (!seenExact.Add((ownerID, kind)))
				throw err(rel.RequiredNode("id"), $"duplicate relationship '{JsonObjectReader.ToKebab(kind.ToString())}' for owner '{ownerID}'");

			result.Add(
				new ModRelationshipManifest {
					OwnerID = ownerID,
					Kind = kind,
					Version = version,
					Description = description,
				}
			);
		}
		return result;
	}

	private static ModAssetsManifest readAssets(JsonObjectReader? assets) {
		if (assets is null)
			return new ModAssetsManifest {
				ManagementKind = ModAssetManagementKind.None,
				Root = null,
			};

		assets.RejectUnknownProperties(
			"management",
			"root"
		);

		ModAssetManagementKind managementKind = ModAssetManagementKind.Enum.FromTag(assets.RequiredEnum<ModAssetManagementKind.Case>("management"));
		string? root = assets.OptionalString("root");

		if (managementKind.Tag is ModAssetManagementKind.Case.Tracked or ModAssetManagementKind.Case.Manual && string.IsNullOrWhiteSpace(root))
			throw err(assets.RequiredNode("management"), "assets.root is required when assets.management is tracked or manual");
		if (managementKind == ModAssetManagementKind.None && root is not null)
			throw err(assets.RequiredNode("root"), "assets.root must be absent when assets.management is none");

		if (root is not null)
			validateRelativePath(root, assets.RequiredNode("root"), "assets.root");

		return new ModAssetsManifest {
			ManagementKind = managementKind,
			Root = root,
		};
	}

	private static IReadOnlyList<ModNativeLibraryManifest> readNativeLibraries(IReadOnlyList<JNode> nodes) {
		if (nodes.Count == 0)
			return Array.Empty<ModNativeLibraryManifest>();
		List<ModNativeLibraryManifest> result = new(nodes.Count);
		HashSet<(string ID, string? RuntimeIdentifier)> seen = new();
		foreach (JNode node in nodes) {
			JsonObjectReader lib = new(node);
			lib.RejectUnknownProperties(
				"id",
				"path",
				"rid"
			);

			string id = lib.RequiredString("id");
			validateLocalID(id, lib.RequiredNode("id"), "native library ID");

			string path = lib.RequiredString("path");
			validateRelativePath(path, lib.RequiredNode("path"), "native library");

			string rid = lib.RequiredString("rid");
			if (string.IsNullOrWhiteSpace(rid))
				throw err(lib.RequiredNode("rid"), "native library runtime identifier cannot be empty or whitespace");

			if (!seen.Add((id, rid)))
				throw err(lib.RequiredNode("id"), $"duplicate native library '{id}' for runtime identifier '{rid}'");

			result.Add(
				new ModNativeLibraryManifest {
					ID = id,
					Path = path,
					RuntimeIdentifier = rid,
				}
			);
		}
		return result;
	}

	private static IReadOnlyList<string> readContractAssemblies(IReadOnlyList<JNode> nodes) {
		if (nodes.Count == 0)
			return Array.Empty<string>();
		List<string> result = new(nodes.Count);
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (JNode node in nodes) {
			if (node.Kind != JKind.String)
				throw err(node, "must be a string");
			string v = node.StringValue!;
			validateRelativePath(v, node, "contract assembly");
			if (!seen.Add(v))
				throw err(node, $"duplicate contract assembly '{v}'");
			result.Add(v);
		}
		return result;
	}

	private static Semver readSemver(JNode node, string raw, string what) {
		try {
			return Semver.Parse(raw);
		} catch (Exception ex) {
			throw new ManifestReadException(node.NodePath, node.Span, $"invalid {what} '{raw}': {ex.Message}");
		}
	}

	private static void validateOwnerID(string value, JNode node) {
		if (!ModMetadataValidation.ValidateOwnerID(value, out string? e))
			throw err(node, $"invalid owner ID '{value}': {e}");
	}

	private static void validateLocalID(string value, JNode node, string what) {
		if (!ModMetadataValidation.ValidateLocalID(value, out string? e))
			throw err(node, $"invalid {what} '{value}': {e}");
	}

	private static void validateRelativePath(string s, JNode node, string what) {
		static void checkSeg(ReadOnlySpan<char> seg, JNode node, string what) {
			if (seg.IsEmpty)
				throw err(node, $"{what} path must not contain empty path segments");
			if (seg.SequenceEqual("."))
				throw err(node, $"{what} path must not contain '.' path segments");
			if (seg.SequenceEqual(".."))
				throw err(node, $"{what} path must not contain '..' path segments");
		}

		if (s.Length == 0)
			throw err(node, $"{what} path must not be empty");
		if (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1]))
			throw err(node, $"{what} path must not have leading/trailing whitespace");
		if (s[0] == '/' || Path.IsPathRooted(s))
			throw err(node, $"{what} path must be a relative path");
		if (s[^1] == '/')
			throw err(node, $"{what} path must not end with '/'");
		int segStart = 0;
		for (int i = 0; i < s.Length; i++) {
			char c = s[i];
			if (char.IsControl(c))
				throw err(node, $"{what} path must not contain control characters");
			if (c == '\\')
				throw err(node, $"{what} path must use '/' as the path separator, not '\\'");
			if (c == '/') {
				checkSeg(s.AsSpan(segStart, i - segStart), node, what);
				segStart = i + 1;
			}
		}
		checkSeg(s.AsSpan(segStart), node, what);
	}

	private static void rejectIfPresent(JsonObjectReader obj, string property, string message) {
		if (obj.Has(property))
			throw err(obj.RequiredNode(property), message);
	}

	private static ManifestReadException err(JNode node, string message) => new(node.NodePath, node.Span, message);
}
