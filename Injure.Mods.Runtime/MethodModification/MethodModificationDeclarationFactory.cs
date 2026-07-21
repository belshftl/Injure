// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Reflection;
using Injure.Mods.Abstractions.MethodModification;
using Injure.Mods.Abstractions.MethodModification.Il;

namespace Injure.Mods.Runtime.MethodModification;

internal static class MethodModificationDeclarationFactory {
	public static RuntimeModificationOrder CreateOrder(DetourAttribute attr, MethodInfo method, int ordinal, string prefix) {
		InternalStateException.ThrowIfNull(attr);
		return createOrder(
			attr.LocalIdOverride,
			attr.LocalPriority,
			attr.SoftBefore,
			attr.HardBefore,
			attr.SoftAfter,
			attr.HardAfter,
			method,
			ordinal,
			prefix
		);
	}

	public static RuntimeModificationOrder CreateOrder(PatchAttribute attr, MethodInfo method, int ordinal, string prefix) {
		InternalStateException.ThrowIfNull(attr);
		return createOrder(
			attr.LocalIdOverride,
			attr.LocalPriority,
			attr.SoftBefore,
			attr.HardBefore,
			attr.SoftAfter,
			attr.HardAfter,
			method,
			ordinal,
			prefix
		);
	}

	public static RuntimeModificationOrder CreateOrder(in ModDetourConfig config) => new() {
		LocalId = config.LocalId,
		LocalPriority = config.LocalPriority,
		Before = config.Before ?? Array.Empty<OwnerOrderingConstraint>(),
		After = config.After ?? Array.Empty<OwnerOrderingConstraint>(),
	};

	public static RuntimeModificationOrder CreateOrder(in ModPatchConfig config) => new() {
		LocalId = config.LocalId,
		LocalPriority = config.LocalPriority,
		Before = config.Before ?? Array.Empty<OwnerOrderingConstraint>(),
		After = config.After ?? Array.Empty<OwnerOrderingConstraint>(),
	};

	public static IlManipulatorRegistration CreatePatchRegistrationFromMethod(Type lifetimeIdentityType, string ownerId, string localId, MethodInfo manipulatorMethod) {
		Type delegateType = typeof(IlManipulator<>).MakeGenericType(lifetimeIdentityType);
		Delegate manipulator = manipulatorMethod.CreateDelegate(delegateType);

		MethodInfo create = typeof(IlManipulatorRegistration).GetMethod(
			nameof(IlManipulatorRegistration.Create),
			BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
		) ?? throw new InternalStateException("failed to find IlManipulatorRegistration.Create");

		MethodInfo closedCreate = create.MakeGenericMethod(lifetimeIdentityType);
		return (IlManipulatorRegistration)(closedCreate.Invoke(null, [ownerId, localId, manipulator]) ??
			throw new InternalStateException("IlManipulatorRegistration.Create() unexpectedly returned null"));
	}

	private static RuntimeModificationOrder createOrder(
		string? explicitLocalId,
		int localPriority,
		IReadOnlyList<string>? softBefore,
		IReadOnlyList<string>? hardBefore,
		IReadOnlyList<string>? softAfter,
		IReadOnlyList<string>? hardAfter,
		MethodInfo method,
		int ordinal,
		string prefix
	) => new() {
		LocalId = explicitOrGeneratedLocalId(explicitLocalId, method, ordinal, prefix),
		LocalPriority = localPriority,
		Before = parseConstraints(softBefore, hardBefore),
		After = parseConstraints(softAfter, hardAfter),
	};

	private static string explicitOrGeneratedLocalId(string? explicitLocalId, MethodInfo method, int ordinal, string prefix) {
		if (!string.IsNullOrWhiteSpace(explicitLocalId))
			return explicitLocalId;
		if (method.DeclaringType?.FullName is null)
			throw new MethodModificationValidationException("expected declaration method to have a declaring type with a fully qualified name");
		return prefix + "/" + method.DeclaringType.FullName + "." + method.Name + "#" + ordinal.ToString(CultureInfo.InvariantCulture);
	}

	private static OwnerOrderingConstraint[] parseConstraints(IReadOnlyList<string>? softTargets, IReadOnlyList<string>? hardTargets) {
		List<OwnerOrderingConstraint>? constraints = null;
		addTargets(ref constraints, softTargets, hard: false);
		addTargets(ref constraints, hardTargets, hard: true);
		return constraints?.ToArray() ?? Array.Empty<OwnerOrderingConstraint>();

		static void addTargets(
			ref List<OwnerOrderingConstraint>? constraints,
			IReadOnlyList<string>? targets,
			bool hard
		) {
			if (targets is null)
				return;
			foreach (string targetStr in targets) {
				OwnerOrderingConstraintTarget target = parseTarget(targetStr);
				OwnerOrderingConstraint constraint = hard
					? OwnerOrderingConstraint.Hard(target)
					: OwnerOrderingConstraint.Soft(target);
				(constraints ??= new List<OwnerOrderingConstraint>()).Add(constraint);
			}
		}

		static OwnerOrderingConstraintTarget parseTarget(string s) {
			if (string.IsNullOrWhiteSpace(s))
				throw new MethodModificationValidationException($"invalid ordering target '{s ?? "<null>"}'");
			int i = s.IndexOf("::", StringComparison.Ordinal);
			if (i < 0)
				return OwnerOrderingConstraintTarget.Owner(s);
			if (s.IndexOf("::", i + 2, StringComparison.Ordinal) >= 0)
				throw new MethodModificationValidationException($"invalid ordering target '{s}': duplicate `::` separator");
			string ownerId = s[..i];
			string localId = s[(i + 2)..];
			if (!ModMetadataValidation.ValidateOwnerId(ownerId, out string? e) ||
				!ModMetadataValidation.ValidateLocalId(localId, out e))
				throw new MethodModificationValidationException($"invalid ordering target '{s}': {e}");
			return OwnerOrderingConstraintTarget.Entry(ownerId, localId);
		}
	}
}
