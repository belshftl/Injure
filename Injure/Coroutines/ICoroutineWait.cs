// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Coroutines;

public interface ICoroutineWait {
	bool KeepWaiting(in CoroutineContext ctx);
	void OnCancel(CoroCancellationReason reason);
	string? GetDebugWaitDescription();
}
