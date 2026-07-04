// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Ui;

public readonly ref struct UiLayoutContext {
	public UiRoot Root { get; }
	public UiCanvasTransform CanvasTransform { get; }
	public float TextScale => CanvasTransform.TextScale;

	internal UiLayoutContext(UiRoot root, UiCanvasTransform canvasTransform) {
		Root = root;
		CanvasTransform = canvasTransform;
	}
}
