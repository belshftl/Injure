// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Reflection;
using Injure.Mods.Abstractions.Hooks;
using Injure.Mods.Abstractions.Hooks.Il;

namespace Injure.Mods.Runtime.Hooks;

internal static class HookDeclarationFactory {
	public static RuntimeHookOrder CreateOrder(
		HookAttribute attr,
		MethodInfo method,
		int ordinal,
		string prefix
	) {
		return new RuntimeHookOrder {
			LocalId = explicitOrGeneratedLocalId(attr.LocalIdOverride, method, ordinal, prefix),
			LocalPriority = attr.LocalPriority,
			Before = parseConstraints(attr.SoftBefore, attr.HardBefore),
			After = parseConstraints(attr.SoftAfter, attr.HardAfter),
		};
	}

	public static RuntimeHookOrder CreateOrder(in ModHookConfig config) {
		return new RuntimeHookOrder {
			LocalId = config.LocalId,
			LocalPriority = config.LocalPriority,
			Before = config.Before ?? Array.Empty<OwnerOrderingConstraint>(),
			After = config.After ?? Array.Empty<OwnerOrderingConstraint>(),
		};
	}

	public static IlManipulatorRegistration CreateRegistrationFromMethod(Type lifetimeIdentityType, string ownerId, string localId, MethodInfo manipulatorMethod) {
		Type delegateType = typeof(IlManipulator<>).MakeGenericType(lifetimeIdentityType);
		Delegate manipulator = manipulatorMethod.CreateDelegate(delegateType);

		MethodInfo create = typeof(IlManipulatorRegistration).GetMethod(
			nameof(IlManipulatorRegistration.Create),
			BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
		) ?? throw new InternalStateException("failed to find IlManipulatorRegistration.Create");

		MethodInfo closedCreate = create.MakeGenericMethod(lifetimeIdentityType);
		return (IlManipulatorRegistration)(closedCreate.Invoke(null, [ownerId, localId, manipulator]) ?? throw new InternalStateException("IlManipulatorRegistration.Create() unexpectedly returned null"));
	}

	private static string explicitOrGeneratedLocalId(string? explicitLocalId, MethodInfo method, int ordinal, string prefix) {
		if (!string.IsNullOrWhiteSpace(explicitLocalId))
			return explicitLocalId;
		if (method.DeclaringType?.FullName is null)
			throw new HookValidationException("expected hook method to have a declaring type with a fully qualified name");
		return prefix + "/" + method.DeclaringType.FullName + "." + method.Name + "#" + ordinal.ToString(CultureInfo.InvariantCulture);
	}

	private static OwnerOrderingConstraint[] parseConstraints(IReadOnlyList<string>? softTargets, IReadOnlyList<string>? hardTargets) {
		List<OwnerOrderingConstraint>? constraints = null;
		addTargets(ref constraints, softTargets, hard: false);
		addTargets(ref constraints, hardTargets, hard: true);
		return constraints?.ToArray() ?? Array.Empty<OwnerOrderingConstraint>();

		static void addTargets(ref List<OwnerOrderingConstraint>? constraints, IReadOnlyList<string>? targets, bool hard) {
			if (targets is null)
				return;
			foreach (string targetStr in targets) {
				OwnerOrderingConstraintTarget target = parseTarget(targetStr);
				OwnerOrderingConstraint constraint = hard ? OwnerOrderingConstraint.Hard(target) : OwnerOrderingConstraint.Soft(target);
				(constraints ??= new List<OwnerOrderingConstraint>()).Add(constraint);
			}
		}

		static OwnerOrderingConstraintTarget parseTarget(string s) {
			if (string.IsNullOrWhiteSpace(s))
				throw new HookValidationException($"invalid hook ordering target '{s ?? "<null>"}'");
			int i = s.IndexOf("::", StringComparison.Ordinal);
			if (i < 0)
				return OwnerOrderingConstraintTarget.Owner(s);
			if (s.IndexOf("::", i + 2, StringComparison.Ordinal) >= 0)
				throw new HookValidationException($"invalid hook ordering target '{s}': duplicate `::` separator");
			string ownerId = s[..i];
			string localId = s[(i + 2)..];
			if (!ModMetadataValidation.ValidateOwnerId(ownerId, out string? e) || !ModMetadataValidation.ValidateLocalId(localId, out e))
				throw new HookValidationException($"invalid hook ordering target '{s}': {e}");
			return OwnerOrderingConstraintTarget.Entry(ownerId, localId);
		}
	}
}
