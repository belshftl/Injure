// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Injure.DevAnalyzers.Shared;

internal static class Constants {
	public const string AttributeNamespace = "Injure.DevAnalyzers.Attributes";
	public const string GeneratorVersion = "0.1.0-alpha";

	// shared between ClosedEnum and ClosedFlags
	public static class ClosedType {
		public const string DefaultIsInvalidName = "DefaultIsInvalid";
		public const string CheckZeroNameName = "CheckZeroName";
		public const string MirrorSubsetName = "Subset";

		public static readonly FrozenSet<string> NeutralZeroNames = new HashSet<string>(StringComparer.Ordinal) {
			"None",
			"Unknown",
			"Unspecified",
			"Undefined",
			"Invalid",
			"Default",
			"Normal",
			"Empty",
			"Unset",
			"Null",
			"Zero",
			"NotHandled",
			"Unhandled",
			"Noop",
			"NoOp",
			"Ignore",
			"Ignored",
		}.ToFrozenSet(StringComparer.Ordinal);
		public static readonly ImmutableArray<string> NeutralZeroPrefixes = ImmutableArray.Create(
			"No",
			"Not",
			"Without"
		);
	}

	public static class ClosedEnum {
		public const string AttributeMetadataName = AttributeNamespace + ".ClosedEnumAttribute";
		public const string AttributeFilename = "ClosedEnumAttribute.g.cs";
		public static readonly string AttributeSource = closedAttributeSource("ClosedEnumAttribute");

		public const string MirrorAttributeMetadataName = AttributeNamespace + ".ClosedEnumMirrorAttribute";
		public const string MirrorAttributeFilename = "ClosedEnumMirrorAttribute.g.cs";
		public static readonly string MirrorAttributeSource = closedMirrorAttributeSource("ClosedEnumMirrorAttribute");

		public const string GeneratedSourceSuffix = ".ClosedEnum.g.cs";
		public const string BackingFieldName = "__ClosedEnum_tag";
		public const string IsDefinedMethodName = "__ClosedEnum_isDefined";
		public static readonly FrozenSet<string> ReservedMemberNames = new HashSet<string>(StringComparer.Ordinal) {
			"Case",
			"Tag",
			"Enum",
			"Equals",
			"GetHashCode",
			"ToString",
			BackingFieldName,
			IsDefinedMethodName,
		}.ToFrozenSet(StringComparer.Ordinal);
	}

	public static class ClosedFlags {
		public const string AttributeMetadataName = AttributeNamespace + ".ClosedFlagsAttribute";
		public const string AttributeFilename = "ClosedFlagsAttribute.g.cs";
		public static readonly string AttributeSource = closedAttributeSource("ClosedFlagsAttribute");

		public const string MirrorAttributeMetadataName = AttributeNamespace + ".ClosedFlagsMirrorAttribute";
		public const string MirrorAttributeFilename = "ClosedFlagsMirrorAttribute.g.cs";
		public static readonly string MirrorAttributeSource = closedMirrorAttributeSource("ClosedFlagsMirrorAttribute");

		public const string GeneratedSourceSuffix = ".ClosedFlags.g.cs";
		public const string BackingFieldName = "__ClosedFlags_bits";
		public const string AllBitsConstName = "__ClosedFlags_allBits";
		public const string IsDefinedMethodName = "__ClosedFlags_isDefined";
		public const string ValidateMethodName = "__ClosedFlags_validate";
		public static readonly FrozenSet<string> ReservedMemberNames = new HashSet<string>(StringComparer.Ordinal) {
			"Bits",
			"Mask",
			"HasAny",
			"HasAll",
			"HasNone",
			"Flags",
			"Equals",
			"GetHashCode",
			"ToString",
			BackingFieldName,
			AllBitsConstName,
			IsDefinedMethodName,
			ValidateMethodName,
		}.ToFrozenSet(StringComparer.Ordinal);
	}

	public static class StronglyTypedInt {
		public const string AttributeMetadataName = AttributeNamespace + ".StronglyTypedIntAttribute";
		public const string AttributeFilename = "StronglyTypedIntAttribute.g.cs";
		public static readonly string AttributeSource = attributeSource(
			"global::System.AttributeTargets.Struct",
			"""
				internal sealed class StronglyTypedIntAttribute : global::System.Attribute {
					public global::System.Type BackingType { get; }
					public StronglyTypedIntAttribute(global::System.Type backingType) {
						BackingType = backingType;
					}
				}
			"""
		);

		public const string GeneratedSourceSuffix = ".StronglyTypedInt.g.cs";
		public const string BackingFieldName = "__StronglyTypedInt_value";
	}

	public static class WrapperType {
		public const string AttributeMetadataName = AttributeNamespace + ".WrapperTypeAttribute";
		public const string AttributeFilename = "WrapperTypeAttribute.g.cs";
		public static readonly string AttributeSource = attributeSource(
			"global::System.AttributeTargets.Class | global::System.AttributeTargets.Struct",
			"""
				internal sealed class WrapperTypeAttribute : global::System.Attribute {
					public global::System.Type WrappedType { get; }
					public string[] Members { get; }
					public WrapperTypeAttribute(global::System.Type wrappedType, params string[] members) {
						WrappedType = wrappedType;
						Members = members;
					}
				}
			"""
		);

		public const string GeneratedSourceSuffix = ".WrapperType.g.cs";
		public const string BackingFieldName = "__WrapperType_inner";
		public const string CheckedAccessorName = "__WrapperType_checkedInner";
		public static readonly FrozenSet<string> ReservedMemberNames = new HashSet<string>(StringComparer.Ordinal) {
			BackingFieldName,
			CheckedAccessorName,
		}.ToFrozenSet(StringComparer.Ordinal);
	}

	private static string attributeSource(string targets, string decl) =>
		"// <auto-generated/>\n" +
		"#nullable enable\n" +
		"#pragma warning disable CS1591\n" +
		"namespace " + AttributeNamespace + " {\n" +
		"\t[global::Microsoft.CodeAnalysis.EmbeddedAttribute]\n" +
		"\t[global::System.AttributeUsage(" + targets + ", AllowMultiple = false, Inherited = false)]\n" +
		decl + "\n" +
		"}\n";

	private static string closedAttributeSource(string name) => attributeSource(
		"global::System.AttributeTargets.Struct",
		$$"""
			internal sealed class {{name}} : global::System.Attribute {
				public bool {{ClosedType.DefaultIsInvalidName}} { get; init; }
				public bool {{ClosedType.CheckZeroNameName}} { get; init; } = true;
			}
		"""
	);

	private static string closedMirrorAttributeSource(string name) => attributeSource(
		"global::System.AttributeTargets.Struct",
		$$"""
			internal sealed class {{name}} : global::System.Attribute {
				public global::System.Type MirrorType { get; }
				public bool {{ClosedType.MirrorSubsetName}} { get; init; }
				public {{name}}(global::System.Type mirrorType) {
					MirrorType = mirrorType;
				}
			}
		"""
	);
}
