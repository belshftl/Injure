// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;

using Injure.ModKit.Abstractions;
using Injure.ModKit.Abstractions.CodeAnalysis;

namespace Injure.Coroutines;

public sealed class CoroutineScope : IReloadTeardown, IDisposable {
	private readonly CoroutineScheduler scheduler;
	private readonly CoroutineScope? parent;
	private readonly HashSet<CoroutineHandle> members = new();
	private readonly List<CoroutineScope> children = new();
	private CoroCancellationReason? cancellationReason = null;
	private int cancelled = 0;

	public CoroutineScheduler Scheduler => scheduler;
	public CoroutineScope? Parent => parent;
	public string Name { get; }
	public string OwnerID { get; }
	public bool Cancelled => Volatile.Read(ref cancelled) != 0;

	private CoroutineScope(CoroutineScheduler scheduler, CoroutineScope? parent, string name, string ownerID) {
		ArgumentNullException.ThrowIfNull(scheduler);
		ArgumentNullException.ThrowIfNull(name);
		ModMetadataValidation.ValidateOwnerIDOrThrow(ownerID);
		this.scheduler = scheduler;
		this.parent = parent;
		Name = name;
		OwnerID = ownerID;
		if (parent is not null) {
			if (!ReferenceEquals(parent.scheduler, scheduler))
				throw new ArgumentException("parent scope belongs to a different scheduler", nameof(parent));
			parent.children.Add(this);
			if (parent.TryGetCancellationReason(out CoroCancellationReason reason))
				cancellationReason = reason;
		}
	}

	public static CoroutineScope CreateRoot(CoroutineScheduler scheduler, string name, string ownerID) => new(scheduler, null, name, ownerID);
	public CoroutineScope CreateChild(string name, string ownerID) => !Cancelled ? new CoroutineScope(scheduler, this, name, ownerID)
		: throw new InvalidOperationException("cannot create a child from a cancelled scope");

	internal void Cancel(CoroCancellationReason reason) {
		if (Interlocked.Exchange(ref cancelled, 1) != 0)
			return;
		cancellationReason = reason;
		CoroutineScope[] childrenSnap = children.Count > 0 ? new CoroutineScope[children.Count] : Array.Empty<CoroutineScope>();
		if (childrenSnap.Length > 0)
			children.CopyTo(childrenSnap);
		CoroutineHandle[] membersSnap = new CoroutineHandle[members.Count];
		members.CopyTo(membersSnap);
		for (int i = 0; i < childrenSnap.Length; i++)
			childrenSnap[i].Cancel(reason);
		for (int i = 0; i < membersSnap.Length; i++)
			scheduler.TryCancel(membersSnap[i], reason);
		parent?.children.Remove(this);
	}

	[SatisfiesObjectObligation(ObligationSatisfactionLevel.Method)]
	public void Cancel() => Cancel(CoroCancellationReason.ScopeCancelled);

	public bool TryGetCancellationReason(out CoroCancellationReason reason) {
		if (cancellationReason is CoroCancellationReason r) {
			reason = r;
			return true;
		}
		reason = default;
		return false;
	}

	internal bool TryRegister(CoroutineHandle handle) => !Cancelled && members.Add(handle);
	internal void Unregister(CoroutineHandle handle) => members.Remove(handle);

	public void Teardown(in ReloadTeardownContext ctx) => Cancel();

	[SatisfiesObjectObligation(ObligationSatisfactionLevel.Method)]
	public void Dispose() => Cancel();
}
