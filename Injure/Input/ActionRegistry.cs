// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Injure.Collections;

namespace Injure.Input;

public sealed class ActionRegistry {
	public readonly ref struct BatchRegistrar {
		private readonly ActionRegistry owner;
		private readonly string? ns;
		private readonly List<string> sids;
		private readonly List<ActionId> ids;

		internal BatchRegistrar(ActionRegistry owner, string? ns) {
			this.owner = owner;
			this.ns = ns;
			sids = new List<string>();
			ids = new List<ActionId>();
		}

		public ActionId Register(string sidOrLocalName) {
			string sid = ns is null ? sidOrLocalName : ns + "::" + sidOrLocalName;
			ValidateSidOrThrow(sid);

			for (int i = 0; i < sids.Count; i++)
				if (StringComparer.Ordinal.Equals(sids[i], sid))
					throw new InvalidOperationException($"action SID {sid} is already registered in this batch");
			if (owner.actions.ContainsLeft(sid))
				throw new InvalidOperationException($"action SID {sid} is already registered");
			if ((ulong)owner.nextId + (ulong)ids.Count >= uint.MaxValue)
				throw new InvalidOperationException("action ID space exhausted");
			ActionId id = new(owner.nextId + 1u + (uint)ids.Count);
			sids.Add(sid);
			ids.Add(id);
			return id;
		}

		internal void Commit() {
			if (sids.Count == 0)
				return;
			owner.actions.Set(CollectionsMarshal.AsSpan(sids), CollectionsMarshal.AsSpan(ids));
			owner.nextId += (uint)ids.Count;
		}
	}

	// FrozenSnapshotTwoWayMap isn't safe for concurrent writes without external mutexing,
	// it won't corrupt state but if writes race only the winner's snapshot update makes
	// it in and the other changes get lost
	private readonly Lock writeLock = new();

	private readonly FrozenSnapshotTwoWayMap<string, ActionId> actions = new(cmpLeft: StringComparer.Ordinal);
	private uint nextId = 0; // first will be 1 since this gets incremented upfront

	// for now just do this
	internal ActionRegistry() {
	}

	public ActionId Register(string sid) {
		ValidateSidOrThrow(sid);
		lock (writeLock) {
			if (actions.ContainsLeft(sid))
				throw new InvalidOperationException($"action SID {sid} is already registered");
			ActionId id = new(nextId + 1);
			actions.Set(sid, id);
			nextId++;
			return id;
		}
	}

	public void RegisterMany(Action<BatchRegistrar> register) {
		ArgumentNullException.ThrowIfNull(register);
		lock (writeLock) {
			BatchRegistrar reg = new(this, null);
			register(reg);
			reg.Commit();
		}
	}

	public void RegisterMany(string ns, Action<BatchRegistrar> register) {
		ArgumentNullException.ThrowIfNull(register);
		if (!validateSeg(ns, "namespace", out string? err))
			throw new ArgumentException(err, nameof(ns));
		lock (writeLock) {
			BatchRegistrar reg = new(this, ns);
			register(reg);
			reg.Commit();
		}
	}

	public bool TryGetId(string sid, out ActionId id) => actions.TryGetByLeft(sid, out id);
	public bool TryGetSid(ActionId id, [NotNullWhen(true)] out string? sid) => actions.TryGetByRight(id, out sid);

	// these could just redirect to actions.GetBy* but this has nicer exception messages
	public ActionId GetId(string sid) {
		if (!actions.TryGetByLeft(sid, out ActionId id))
			throw new ArgumentException("unknown action SID", nameof(sid));
		return id;
	}
	public string GetSid(ActionId id) {
		if (!actions.TryGetByRight(id, out string? sid))
			throw new ArgumentException("unknown action ID", nameof(id));
		return sid;
	}

	private static bool validateSeg(ReadOnlySpan<char> s, string kind, [NotNullWhen(false)] out string? err) {
		if (s.IsEmpty) {
			err = $"action SID {kind} segment must not be empty";
			return false;
		}
		if (!char.IsAsciiLetterOrDigit(s[0])) {
			err = $"action SID {kind} segment must start with an ASCII letter or ASCII digit";
			return false;
		}
		foreach (char c in s)
			if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c == '.')) {
				err = $"action SID contains invalid UTF-16 code unit U+{(ushort)c:X4} '{c}' (valid: ASCII letters, ASCII digits, '_', '-', '.')";
				return false;
			}
		err = null;
		return true;
	}

	public static bool ValidateSid([NotNullWhen(true)] string? sid, [NotNullWhen(false)] out string? err) {
		if (sid is null) {
			err = "action SID must not be null";
			return false;
		}
		if (sid.Length == 0) {
			err = "action SID must not be empty";
			return false;
		}
		int sep = sid.IndexOf("::", StringComparison.Ordinal);
		if (sep < 0 || sid.IndexOf("::", sep + 2, StringComparison.Ordinal) >= 0) {
			err = "action SID must contain exactly one occurrence of ::";
			return false;
		}
		if (!validateSeg(sid.AsSpan(0, sep), "namespace", out err) || !validateSeg(sid.AsSpan(sep + 2), "name", out err))
			return false;
		err = null;
		return true;
	}

	public static void ValidateSidOrThrow([NotNull] string? sid) {
		if (!ValidateSid(sid, out string? err))
			throw new FormatException(err);
	}
}
