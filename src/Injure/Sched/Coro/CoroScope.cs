// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods;
using Injure.Mods.CodeAnalysis;

namespace Injure.Sched.Coro;

public sealed class CoroScope : IReloadTeardown, IDisposable {
	private readonly CoroScheduler scheduler;
	private readonly CoroScope? parent;
	private readonly HashSet<CoroHandle> members = new();
	private readonly List<CoroScope> children = new();
	private CoroCancellationReason? cancellationReason = null;
	private int cancelled = 0;

	public CoroScheduler Scheduler => scheduler;
	public CoroScope? Parent => parent;
	public string Name { get; }
	public string OwnerId { get; }
	public bool Cancelled => Volatile.Read(ref cancelled) != 0;

	private CoroScope(CoroScheduler scheduler, CoroScope? parent, string name, string ownerId) {
		ArgumentNullException.ThrowIfNull(scheduler);
		ArgumentNullException.ThrowIfNull(name);
		ModMetadataValidation.ValidateOwnerIdOrThrow(ownerId);
		this.scheduler = scheduler;
		this.parent = parent;
		Name = name;
		OwnerId = ownerId;
		if (parent is not null) {
			if (!ReferenceEquals(parent.scheduler, scheduler))
				throw new ArgumentException("parent scope belongs to a different scheduler", nameof(parent));
			parent.children.Add(this);
			if (parent.TryGetCancellationReason(out CoroCancellationReason reason))
				cancellationReason = reason;
		}
	}

	public static CoroScope CreateRoot(CoroScheduler scheduler, string name, string ownerId) => new(scheduler, null, name, ownerId);
	public CoroScope CreateChild(string name, string ownerId) => !Cancelled
		? new CoroScope(scheduler, this, name, ownerId)
		: throw new InvalidOperationException("cannot create a child from a cancelled scope");

	internal void Cancel(CoroCancellationReason reason) {
		if (Interlocked.Exchange(ref cancelled, 1) != 0)
			return;
		cancellationReason = reason;
		CoroScope[] childrenSnap = children.Count > 0 ? new CoroScope[children.Count] : Array.Empty<CoroScope>();
		if (childrenSnap.Length > 0)
			children.CopyTo(childrenSnap);
		var membersSnap = new CoroHandle[members.Count];
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

	internal bool TryRegister(CoroHandle handle) => !Cancelled && members.Add(handle);
	internal void Unregister(CoroHandle handle) => members.Remove(handle);

	public void Teardown(in ReloadTeardownContext ctx) => Cancel();

	[SatisfiesObjectObligation(ObligationSatisfactionLevel.Method)]
	public void Dispose() => Cancel();
}
