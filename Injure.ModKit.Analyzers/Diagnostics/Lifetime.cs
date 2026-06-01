// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Diagnostics;

internal static class Lifetime {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor LifetimeObligationLeaked = new(
		id: "IJM0100",
		title: "Reload lifetime obligation leaks",
		messageFormat: "Object '{0}' with obligation '{1}' leaked here by '{2}'; obligation must be satisfied by at least '{3}', best found is '{4}'",
		category: "ReloadSafety",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor LifetimeObligationExceptionLeaked = new(
		id: "IJM0101",
		title: "Reload lifetime obligation may leak on exception",
		messageFormat: "Object '{0}' with obligation '{1}' may leak if this statement throws; obligation must be satisfied by at least '{2}', best found is '{3}'",
		category: "ReloadSafety",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor LifetimeObligationLeakedSometimes = new(
		id: "IJM0102",
		title: "Reload lifetime obligation leaks on some paths/branches",
		messageFormat: "Object '{0}' with obligation '{1}' leaked here by '{2}' on some branches/paths; obligation must be satisfied by at least '{3}', best found is '{4}', worst found is '{5}'",
		category: "ReloadSafety",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor LifetimeObligationExceptionLeakedSometimes = new(
		id: "IJM0103",
		title: "Reload lifetime obligation may leak on exception on some paths/branches",
		messageFormat: "Object '{0}' with obligation '{1}' may leak on some branches/paths if this statement throws; obligation must be satisfied by at least '{2}', best found is '{3}', worst found is '{4}'",
		category: "ReloadSafety",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor AsyncCallNeedsBoundedToken = new(
		id: "IJM0198",
		title: "Cancellable async call should use generation-bounded cancellation",
		messageFormat: "Async call '{0}' accepts a CancellationToken but is not passed a generation-bounded token",
		category: "ReloadSafety",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor AnalysisBailout = new(
		id: "IJM0199",
		title: "Reload-safety lifetime analysis isn't supported for this method",
		messageFormat: "Reload-safety lifetime analysis bailed out on this method: {0}",
		category: "ReloadSafety",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
