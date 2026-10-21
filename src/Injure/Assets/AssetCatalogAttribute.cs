// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Assets;

/// <summary>
/// Makes the <c>Injure.Analyzers</c> sourcegen turn an asset directory into a typed catalog on the
/// marked partial class.
/// </summary>
/// <param name="directory">The asset directory, relative to the project directory.</param>
/// <param name="namespace">The asset namespace the directory's assets are in.</param>
/// <remarks>
/// <para>
/// The project also has to list the directory's files as additional files, e.g.
/// <c>&lt;AdditionalFiles Include="assets/**" /&gt;</c>; the generator only looks at their paths.
/// The catalog doesn't load anything by itself either, so a <see cref="DirectoryAssetSource"/>
/// over the directory has to be separately registered for the namespace via
/// <see cref="AssetStore.RegisterSource(Mods.OwnerOrderedEntry{IAssetSource})"/>.
/// </para>
/// <para>
/// The marked class becomes an <see cref="AssetGroup"/> of every asset in the directory, with a
/// constructor taking the <see cref="AssetStore"/>, so it must not declare a base class itself.
/// Each subdirectory becomes a nested <see cref="AssetGroup"/> class with a property of the same
/// name, and each file whose kind is known (see <see cref="AssetFileKindAttribute"/>) an
/// <see cref="Asset{T}"/> property. A group corresponding to a directory includes the assets of its
/// subdirectories. Names are converted to PascalCase, e.g. <c>player-idle.png</c> becomes
/// <c>PlayerIdle</c>; files and directories whose names start with <c>.</c> are skipped.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AssetCatalogAttribute(string directory, string @namespace) : Attribute {
	/// <summary>
	/// The asset directory, relative to the project directory.
	/// </summary>
	public string Directory { get; } = directory;

	/// <summary>
	/// The asset namespace the directory's assets are in.
	/// </summary>
	public string Namespace { get; } = @namespace;
}

/// <summary>
/// Tells the asset catalog generator which asset type files with a given suffix are, on top of the
/// built-in kinds.
/// </summary>
/// <param name="suffix">
/// The file name suffix, including the leading dot, e.g. <c>.level.json</c>.
/// </param>
/// <param name="assetType">The asset type, e.g. <c>typeof(Level)</c>.</param>
/// <remarks>
/// <para>
/// The builtin kinds are images (<c>.png</c>, <c>.jpg</c>, <c>.jpeg</c>, <c>.bmp</c>, <c>.tga</c>)
/// and <c>.tex.json</c> as <see cref="Draw.Texture2d"/>, and <c>.ttf</c>, <c>.otf</c>, <c>.ttc</c>,
/// and <c>.otc</c> as <see cref="Draw.Text.FontData"/>. A kind given here replaces a builtin one with the
/// same suffix.
/// </para>
/// <para>
/// Suffixes are matched ignoring case, and the longest matching suffix wins, so <c>.tex.json</c>
/// takes precedence over a <c>.json</c> kind. The suffix is removed to get the property name.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class AssetFileKindAttribute(string suffix, Type assetType) : Attribute {
	/// <summary>
	/// The file name suffix, including the leading dot.
	/// </summary>
	public string Suffix { get; } = suffix;

	/// <summary>
	/// The asset type.
	/// </summary>
	public Type AssetType { get; } = assetType;
}
