// SPDX-License-Identifier: MIT

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Injure.IO;

public sealed class FileHotReloadMonitorOptions {
	public TimeSpan DebounceDelay { get; init; } = TimeSpan.FromMilliseconds(35);
	public TimeSpan StabilitySampleInterval { get; init; } = TimeSpan.FromMilliseconds(35);
	public int RequiredStableSamples { get; init; } = 2;
	public int ReadBufferSize { get; init; } = 512 * 1024;
	public int WatcherInternalBufferSize { get; init; } = 64 * 1024;
}

public sealed class FileHotReloadMonitor : IDisposable {
	private readonly record struct FileFingerprint(bool Exists, long Length, ulong Hash) {
		public static FileFingerprint Missing => new(Exists: false, Length: 0, Hash: 0);
		public bool ContentEquals(FileFingerprint other) {
			if (!Exists || !other.Exists)
				return Exists == other.Exists;
			return Length == other.Length && Hash == other.Hash;
		}
	}

	private sealed class WatchedFile(string fullPath, DirectoryWatch directory) {
		public readonly string FullPath = fullPath;
		public readonly DirectoryWatch Directory = directory;
		public int RefCount = 1;
		public long Generation;
		public bool ProbeMayRaise;
		public CancellationTokenSource? ProbeCts;
		public FileFingerprint? LastStableFingerprint;
	}

	private sealed class DirectoryWatch(string path, FileSystemWatcher watcher, StringComparer pathComparer) : IDisposable {
		public readonly string Path = path;
		public readonly FileSystemWatcher Watcher = watcher;
		public readonly Dictionary<string, WatchedFile> FilesByPath = new(pathComparer);
		public void Dispose() => Watcher.Dispose();
	}

	private readonly Lock @lock = new();
	private readonly FileHotReloadMonitorOptions options;
	private readonly StringComparer pathComparer;
	private readonly Dictionary<string, DirectoryWatch> dirsByPath;
	private readonly Dictionary<string, WatchedFile> filesByPath;
	private bool disposed = false;

	public event Action<string>? StablePathChanged;

	public FileHotReloadMonitor(FileHotReloadMonitorOptions? options = null) {
		if (options is not null) {
			if (options.DebounceDelay < TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(options), options.DebounceDelay, "debounce delay must not be negative");
			if (options.StabilitySampleInterval <= TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(options), options.StabilitySampleInterval, "stability sample interval must be positive");
			if (options.RequiredStableSamples <= 0)
				throw new ArgumentOutOfRangeException(nameof(options), options.RequiredStableSamples, "required stable sample count must be positive");
			if (options.ReadBufferSize <= 0)
				throw new ArgumentOutOfRangeException(nameof(options), options.ReadBufferSize, "read buffer size must be positive");
			if (options.WatcherInternalBufferSize < 4096)
				throw new ArgumentOutOfRangeException(nameof(options), options.WatcherInternalBufferSize, "watcher internal buffer size must be at least 4096 bytes");
		}
		this.options = options ?? new FileHotReloadMonitorOptions();

		pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

		dirsByPath = new Dictionary<string, DirectoryWatch>(pathComparer);
		filesByPath = new Dictionary<string, WatchedFile>(pathComparer);
	}

	public void WatchFile(string path) {
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ObjectDisposedException.ThrowIf(disposed, this);
		string fullPath = Path.GetFullPath(path);
		string dirPath = Path.GetDirectoryName(fullPath) ?? throw new ArgumentException("path must have a parent directory", nameof(path));
		lock (@lock) {
			ObjectDisposedException.ThrowIf(disposed, this);
			if (filesByPath.TryGetValue(fullPath, out WatchedFile? f)) {
				checked {
					f.RefCount++;
				}
				return;
			}
			DirectoryWatch d = getDirectoryWatchLocked(dirPath);
			f = new WatchedFile(fullPath, d);
			filesByPath.Add(fullPath, f);
			d.FilesByPath.Add(fullPath, f);
			scheduleProbeLocked(f, mayRaise: false);
		}
	}

	public void UnwatchFile(string path) {
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ObjectDisposedException.ThrowIf(disposed, this); // TODO: decide if this should throw or no-op
		string fullPath = Path.GetFullPath(path);
		lock (@lock) {
			if (disposed)
				return;
			if (!filesByPath.TryGetValue(fullPath, out WatchedFile? f))
				return;
			if (--f.RefCount > 0)
				return;
			filesByPath.Remove(fullPath);
			f.Directory.FilesByPath.Remove(fullPath);
			f.ProbeCts?.Cancel();
			f.ProbeCts?.Dispose();
			f.ProbeCts = null;
			if (f.Directory.FilesByPath.Count == 0) {
				dirsByPath.Remove(f.Directory.Path);
				f.Directory.Dispose();
			}
		}
	}

	public void Dispose() {
		DirectoryWatch[] dirs;
		WatchedFile[] files;
		lock (@lock) {
			if (disposed)
				return;
			disposed = true;
			dirs = dirsByPath.Values.ToArray();
			files = filesByPath.Values.ToArray();
			dirsByPath.Clear();
			filesByPath.Clear();
		}
		foreach (WatchedFile f in files) {
			f.ProbeCts?.Cancel();
			f.ProbeCts?.Dispose();
			f.ProbeCts = null;
		}
		foreach (DirectoryWatch d in dirs)
			d.Dispose();
	}

	private DirectoryWatch getDirectoryWatchLocked(string path) {
		string fullPath = Path.GetFullPath(path);
		if (!dirsByPath.TryGetValue(fullPath, out DirectoryWatch? d)) {
			FileSystemWatcher wtch = new(fullPath) {
				IncludeSubdirectories = false,
				InternalBufferSize = options.WatcherInternalBufferSize,
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
			};
			d = new DirectoryWatch(fullPath, wtch, pathComparer);
			wtch.Created += onFsEvent;
			wtch.Changed += onFsEvent;
			wtch.Deleted += onFsEvent;
			wtch.Renamed += onRenamedEvent;
			wtch.Error += onWatcherError;
			wtch.EnableRaisingEvents = true;
			dirsByPath.Add(fullPath, d);
		}
		return d;
	}

	private void onFsEvent(object sender, FileSystemEventArgs args) =>
		markPossiblyChanged(args.FullPath);

	private void onRenamedEvent(object sender, RenamedEventArgs args) {
		markPossiblyChanged(args.OldFullPath);
		markPossiblyChanged(args.FullPath);
	}

	private void onWatcherError(object sender, ErrorEventArgs args) {
		if (sender is not FileSystemWatcher wtch)
			return;
		string path = Path.GetFullPath(wtch.Path);
		lock (@lock) {
			if (disposed)
				return;
			if (!dirsByPath.TryGetValue(path, out DirectoryWatch? d))
				return;
			foreach (WatchedFile f in d.FilesByPath.Values)
				scheduleProbeLocked(f, mayRaise: true);
		}
	}

	private void markPossiblyChanged(string path) {
		string fullPath;
		try {
			fullPath = Path.GetFullPath(path);
		} catch {
			return;
		}
		lock (@lock) {
			if (disposed)
				return;
			if (!filesByPath.TryGetValue(fullPath, out WatchedFile? file))
				return;
			scheduleProbeLocked(file, mayRaise: true);
		}
	}

	private void scheduleProbeLocked(WatchedFile file, bool mayRaise) {
		checked {
			file.Generation++;
		}
		file.ProbeMayRaise = mayRaise;
		file.ProbeCts?.Cancel();
		file.ProbeCts?.Dispose();

		CancellationTokenSource cts = new();
		file.ProbeCts = cts;

		_ = probeWhenStableAsync(file.FullPath, file.Generation, mayRaise, cts);
	}

	private async Task probeWhenStableAsync(string fullPath, long generation, bool mayRaise, CancellationTokenSource cts) {
		try {
			CancellationToken ct = cts.Token;
			if (options.DebounceDelay > TimeSpan.Zero)
				await Task.Delay(options.DebounceDelay, ct).ConfigureAwait(false);
			FileFingerprint? last = null;
			int stableSamples = 0;
			for (;;) {
				FileFingerprint? curr = await tryReadFingerprintAsync(fullPath, ct).ConfigureAwait(false);
				if (curr is not null && last is not null && curr.Value.ContentEquals(last.Value))
					stableSamples++;
				else
					stableSamples = curr is null ? 0 : 1;

				last = curr;
				if (curr is not null && stableSamples >= options.RequiredStableSamples)
					break;
				await Task.Delay(options.StabilitySampleInterval, ct).ConfigureAwait(false);
			}

			FileFingerprint stable = last!.Value;
			bool shouldRaise;
			lock (@lock) {
				if (disposed)
					return;
				if (!filesByPath.TryGetValue(fullPath, out WatchedFile? file))
					return;
				if (file.Generation != generation)
					return;

				FileFingerprint? prev = file.LastStableFingerprint;
				file.LastStableFingerprint = stable;
				shouldRaise = mayRaise && stable.Exists && prev is not null && !stable.ContentEquals(prev.Value);
			}

			if (shouldRaise)
				StablePathChanged?.Invoke(fullPath);
		} catch (OperationCanceledException) {
		} finally {
			lock (@lock) {
				if (!disposed && filesByPath.TryGetValue(fullPath, out WatchedFile? f) && f.Generation == generation && ReferenceEquals(f.ProbeCts, cts))
					f.ProbeCts = null;
			}
			cts.Dispose();
		}
	}

	private async ValueTask<FileFingerprint?> tryReadFingerprintAsync(string fullPath, CancellationToken ct) {
		FileInfo info = new(fullPath);
		if (!info.Exists)
			return FileFingerprint.Missing;
		XxHash3 xxh = new();
		byte[] buf = ArrayPool<byte>.Shared.Rent(options.ReadBufferSize);
		try {
			await using FileStream stream = new(
				fullPath,
				FileMode.Open,
				FileAccess.Read,
				FileShare.ReadWrite | FileShare.Delete,
				bufferSize: options.ReadBufferSize,
				options: FileOptions.Asynchronous | FileOptions.SequentialScan
			);
			for (;;) {
				int n = await stream.ReadAsync(buf.AsMemory(0, options.ReadBufferSize), ct).ConfigureAwait(false);
				if (n == 0)
					break;
				xxh.Append(buf.AsSpan(0, n));
			}
			ulong hash = xxh.GetCurrentHashAsUInt64();
			return new FileFingerprint(
				Exists: true,
				Length: stream.Length,
				Hash: hash
			);
		} catch (FileNotFoundException) {
			return FileFingerprint.Missing;
		} catch (DirectoryNotFoundException) {
			return FileFingerprint.Missing;
		} catch (IOException) {
			return null;
		} catch (UnauthorizedAccessException) {
			return null;
		} finally {
			ArrayPool<byte>.Shared.Return(buf);
		}
	}
}
