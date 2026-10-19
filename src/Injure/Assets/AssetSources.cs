// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Text;

namespace Injure.Assets;

/// <summary>
/// Asset source that serves files from a filesystem directory for one asset namespace.
/// </summary>
/// <param name="matchNamespace">Asset namespace handled by this source.</param>
/// <param name="root">Root directory to serve files from.</param>
/// <param name="fileOpenOptions">
/// Options for opened file streams, or <see langword="null"/> for defaults.
/// </param>
/// <remarks>
/// <para>
/// Namespace matching is ordinal and case-sensitive. Missing files return <c>NotHandled</c>;
/// failure occurs for files that exist but cannot be opened or read.
/// </para>
/// <para>
/// Asset paths are matched exactly on every platform, also on filesystems that ignore case (the
/// default on Windows and macOS) or Unicode normalization (macOS): a path whose file or directories
/// only match when spelled differently fails with an <see cref="IOException"/>, which names the
/// actual spelling where it can, rather than loading on those filesystems and going missing on
/// others. The spelling of <c>root</c> itself isn't checked.
/// </para>
/// </remarks>
public sealed class DirectoryAssetSource(string matchNamespace, string root, FileStreamOptions? fileOpenOptions = null) : IAssetSource {
	private readonly string matchNamespace = matchNamespace;
	private readonly string root = !string.IsNullOrWhiteSpace(root) ? root : throw new ArgumentException("root must be non-null/empty/whitespace");
	private readonly FileStreamOptions fileOpenOptions = fileOpenOptions ?? DefaultFileOpenOptions;

	/// <summary>
	/// Default <see cref="FileStreamOptions"/> used for opened files if an override isn't provided.
	/// </summary>
	public static readonly FileStreamOptions DefaultFileOpenOptions = new() {
		Mode = FileMode.Open,
		Access = FileAccess.Read,
		Share = FileShare.ReadWrite | FileShare.Delete,
		Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
		BufferSize = 64 * 1024,
	};

	public ValueTask<AssetSourceResult> TrySourceAsync(AssetSourceInfo info, IAssetDepCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (info.AssetId.Namespace != matchNamespace)
			return ValueTask.FromResult(AssetSourceResult.NotHandled());
		string path = Path.Combine(root, info.AssetId.Path.Replace('/', Path.DirectorySeparatorChar));

		Stream stream;
		try {
			stream = new FileStream(path, fileOpenOptions);
		} catch (FileNotFoundException) {
			return ValueTask.FromResult(AssetSourceResult.NotHandled());
		} catch (DirectoryNotFoundException) {
			return ValueTask.FromResult(AssetSourceResult.NotHandled());
		}
		try {
			chkCasing(info.AssetId.Path);
		} catch {
			stream.Dispose();
			throw;
		}
		coll.Add(new FileAssetDep(Path.GetFullPath(path)));
		return ValueTask.FromResult(AssetSourceResult.Success(stream));
	}

	// the open succeeded, so every segment exists, but on a filesystem that ignores case or Unicode
	// normalization maybe only under a different spelling, which the open doesn't tell apart
	private void chkCasing(string assetPath) {
		string dir = root;
		foreach (string seg in assetPath.Split('/')) {
			if (!hasExactEntry(dir, seg, out string? similar)) {
				string actual = similar is not null ? $"'{similar}'" : "an entry spelled differently";
				throw new IOException(
					$"asset path '{assetPath}' only matches {actual} in '{dir}' (casing or Unicode normalization differs); asset paths are matched exactly on every platform"
				);
			}
			dir = Path.Combine(dir, seg);
		}
	}

	// `similar` is an entry differing only in casing or normalization, for the error message; it may
	// be null even then, since normalization depends on the globalization mode
	private static bool hasExactEntry(string dir, string name, out string? similar) {
		similar = null;
		foreach (string entry in Directory.EnumerateFileSystemEntries(dir)) {
			string n = Path.GetFileName(entry);
			if (string.Equals(n, name, StringComparison.Ordinal))
				return true;
			if (similar is null && sameLoosely(n, name))
				similar = n;
		}
		return false;
	}

	private static bool sameLoosely(string a, string b) =>
		string.Equals(a.Normalize(NormalizationForm.FormC), b.Normalize(NormalizationForm.FormC), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Asset source that serves embedded assembly resources from an explicit asset ID set.
/// </summary>
/// <remarks>
/// The asset ID path is used as the manifest resource name. The explicit ID set is used instead of
/// reflection-based discovery so this source remains simple and NativeAOT-friendly.
/// </remarks>
public sealed class EmbeddedAssetSource(Assembly assembly, HashSet<AssetId> ids) : IAssetSource {
	private readonly Assembly assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
	private readonly HashSet<AssetId> ids = chkIds(ids);

	public ValueTask<AssetSourceResult> TrySourceAsync(AssetSourceInfo info, IAssetDepCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (!ids.Contains(info.AssetId))
			return ValueTask.FromResult(AssetSourceResult.NotHandled());

		Stream stream = assembly.GetManifestResourceStream(info.AssetId.Path) ??
			throw new FileNotFoundException("failed to open embedded asset stream", info.AssetId.Path);
		coll.Add(new EmbeddedAssetDep(assembly, info.AssetId.Path));
		return ValueTask.FromResult(AssetSourceResult.Success(stream));
	}

	private static HashSet<AssetId> chkIds(HashSet<AssetId> ids) {
		ArgumentNullException.ThrowIfNull(ids);
		foreach (AssetId id in ids)
			ArgumentInvalidDefaultException.ThrowIfDefault(id, nameof(ids));
		return ids;
	}
}
