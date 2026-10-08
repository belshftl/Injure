// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Injure.Collections;
using Injure.Mods;

namespace Injure.Input;

/// <summary>
/// Maps action SIDs (string IDs) to <see cref="ActionId"/>s.
/// </summary>
/// <remarks>
/// <para>
/// An action SID has the form <c>ns::name</c>, where <c>ns</c> is a valid owner ID and <c>name</c>
/// is a valid local ID; for example, <c>mygame::jump</c> or <c>mygame::ui/confirm</c>. Registration
/// is permanent; there is no way to unregister an action.
/// </para>
/// <para>
/// A game normally only uses a single <see cref="ActionRegistry"/> across the entire process.
/// IDs from different registries never compare equal.
/// </para>
/// <para>
/// Thread-safe: registration is serialized internally, and lookups can run concurrently with
/// registration, seeing either the state before or after any given registration call.
/// </para>
/// </remarks>
public sealed class ActionRegistry {
	/// <summary>
	/// Handed to a callback to register several actions as one atomic operation; see
	/// <see cref="RegisterMany(Action{BatchRegistrar})"/>.
	/// </summary>
	/// <remarks>
	/// The <see langword="default"/> value is invalid.
	/// </remarks>
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

		/// <summary>
		/// Adds an action to the batch and returns the ID it will have once the batch is committed.
		/// </summary>
		/// <param name="sidOrLocalName">
		/// A full SID if the batch has no namespace, or only the name segment if it has one.
		/// </param>
		/// <exception cref="FormatException">
		/// Thrown if the resulting SID is not a valid SID.
		/// </exception>
		/// <exception cref="InvalidOperationException">
		/// Thrown if the SID is already registered or already part of this batch, or if the
		/// registry has run out of IDs.
		/// </exception>
		public ActionId Register(string sidOrLocalName) {
			string sid = ns is null ? sidOrLocalName : ns + "::" + sidOrLocalName;
			ValidateSidOrThrow(sid);

			for (int i = 0; i < sids.Count; i++)
				if (StringComparer.Ordinal.Equals(sids[i], sid))
					throw new InvalidOperationException($"action SID {sid} is already registered in this batch");
			if (owner.actions.ContainsLeft(sid))
				throw new InvalidOperationException($"action SID {sid} is already registered");
			if (owner.nextId + (ulong)ids.Count >= uint.MaxValue)
				throw new InvalidOperationException("action ID space exhausted");
			ActionId id = new(owner.registryId, owner.nextId + 1u + (uint)ids.Count);
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

	/// <summary>
	/// The last registry ID handed out. Incremented before use, so IDs start at 1 and 0 stays
	/// reserved for invalid/<see langword="default"/> action IDs.
	/// </summary>
	private static ulong nextRegistryId = 0;

	/// <remarks>
	/// <see cref="FrozenSnapshotBijectiveMap{TLeft, TRight}"/> doesn't corrupt under concurrent
	/// writes, but when they race, only the winner's snapshot update makes it in and the other
	/// changes are lost. Reads don't need a lock.
	/// </remarks>
	private readonly Lock writeLock = new();

	private readonly ulong registryId = Interlocked.Increment(ref nextRegistryId);
	private readonly FrozenSnapshotBijectiveMap<string, ActionId> actions = new(cmpLeft: StringComparer.Ordinal);
	/// <summary>
	/// The last registry-local ID handed out. IDs start at 1, so 0 stays reserved for
	/// <see langword="default"/>.
	/// </summary>
	private uint nextId = 0;

	/// <summary>
	/// Registers a single action.
	/// </summary>
	/// <exception cref="FormatException">
	/// Thrown if <paramref name="sid"/> is not a valid SID.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="sid"/> is already registered, or if the registry has run out of IDs.
	/// </exception>
	public ActionId Register(string sid) {
		ValidateSidOrThrow(sid);
		lock (writeLock) {
			if (actions.ContainsLeft(sid))
				throw new InvalidOperationException($"action SID {sid} is already registered");
			if (nextId == uint.MaxValue)
				throw new InvalidOperationException("action ID space exhausted");
			ActionId id = new(registryId, nextId + 1);
			actions.Set(sid, id);
			nextId++;
			return id;
		}
	}

	/// <summary>
	/// Registers several actions, given as full SIDs, as one atomic operation.
	/// </summary>
	/// <param name="register">
	/// Callback that registers the actions through the <see cref="BatchRegistrar"/> it receives.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="register"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Nothing is registered unless <paramref name="register"/> returns normally; if it throws, its
	/// registrations are logically discarded, and the IDs it obtained are invalid.
	/// </para>
	/// <para>
	/// Concurrent registrations on this registry will block until the callback returns/throws.
	/// </para>
	/// </remarks>
	public void RegisterMany(Action<BatchRegistrar> register) {
		ArgumentNullException.ThrowIfNull(register);
		lock (writeLock) {
			BatchRegistrar reg = new(this, null);
			register(reg);
			reg.Commit();
		}
	}

	/// <summary>
	/// Registers several actions in the namespace <paramref name="ns"/>, given as name segments only,
	/// as one atomic operation.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="ns"/> is not a valid owner ID.
	/// </exception>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="register"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// <inheritdoc cref="RegisterMany(Action{BatchRegistrar})" path="/remarks"/>
	/// </remarks>
	public void RegisterMany(string ns, Action<BatchRegistrar> register) {
		ArgumentNullException.ThrowIfNull(register);
		if (!ModMetadataValidation.ValidateOwnerId(ns, out string? err))
			throw new ArgumentException($"action SID namespace is not a valid owner ID: {err}", nameof(ns));
		lock (writeLock) {
			BatchRegistrar reg = new(this, ns);
			register(reg);
			reg.Commit();
		}
	}

	/// <summary>
	/// Looks up the ID registered for <paramref name="sid"/>.
	/// </summary>
	public bool TryGetId(string sid, out ActionId id) => actions.TryGetByLeft(sid, out id);

	/// <summary>
	/// Looks up the SID that <paramref name="id"/> was registered under. Fails for IDs from other
	/// registries.
	/// </summary>
	public bool TryGetSid(ActionId id, [NotNullWhen(true)] out string? sid) =>
		actions.TryGetByRight(id, out sid);

	/// <summary>
	/// Gets the ID registered for <paramref name="sid"/>.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="sid"/> is not registered.
	/// </exception>
	public ActionId GetId(string sid) {
		if (!actions.TryGetByLeft(sid, out ActionId id))
			throw new ArgumentException("unknown action SID", nameof(sid));
		return id;
	}

	/// <summary>
	/// Gets the SID that <paramref name="id"/> was registered under.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="id"/> is invalid, belongs to a different registry, or is not
	/// registered.
	/// </exception>
	public string GetSid(ActionId id) {
		if (id.IsValid && id.RegistryId != registryId)
			throw new ArgumentException("action ID belongs to a different registry", nameof(id));
		if (!actions.TryGetByRight(id, out string? sid))
			throw new ArgumentException("unknown action ID", nameof(id));
		return sid;
	}

	/// <summary>
	/// Checks whether <paramref name="sid"/> is a valid action SID; see <see cref="ActionRegistry"/>
	/// for the format.
	/// </summary>
	/// <param name="sid">The SID to check.</param>
	/// <param name="err">If invalid, a description of the problem.</param>
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
		if (!ModMetadataValidation.ValidateOwnerId(sid.AsSpan(0, sep), out string? nsErr)) {
			err = $"action SID namespace is not a valid owner ID: {nsErr}";
			return false;
		}
		if (!ModMetadataValidation.ValidateLocalId(sid.AsSpan(sep + 2), out string? nameErr)) {
			err = $"action SID name is not a valid local ID: {nameErr}";
			return false;
		}
		err = null;
		return true;
	}

	/// <summary>
	/// Throws if <paramref name="sid"/> is not a valid action SID.
	/// </summary>
	/// <exception cref="FormatException">
	/// Thrown if <paramref name="sid"/> is not a valid SID.
	/// </exception>
	public static void ValidateSidOrThrow([NotNull] string? sid) {
		if (!ValidateSid(sid, out string? err))
			throw new FormatException(err);
	}
}
