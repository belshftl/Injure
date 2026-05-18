// SPDX-License-Identifier: MIT

namespace Injure.Weaver.Model;

public enum PublicizedOriginalAccessibilityMirror {
	NotApplicable = 0,
	Private,
	PrivateProtected,
	Internal,
	Protected,
	ProtectedInternal,
	Public,
	Other,
}

public enum PublicizedStateMachineKindMirror {
	Unknown = 0,
	Async,
	Iterator,
	AsyncIterator,
}
