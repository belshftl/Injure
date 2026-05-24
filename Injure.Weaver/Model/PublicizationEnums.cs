// SPDX-License-Identifier: MIT

namespace Injure.Weaver.Model;

public enum PublicizedOriginalAccessibility {
	NotApplicable,
	Private,
	PrivateProtected,
	Internal,
	Protected,
	ProtectedInternal,
	Public,
	Other,
}

public enum PublicizedStateMachineKind {
	Unknown,
	Async,
	Iterator,
	AsyncIterator,
}
