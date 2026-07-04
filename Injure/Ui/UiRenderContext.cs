// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Primitives;

namespace Injure.Ui;

public readonly ref struct UiRenderContext {
	public UiRoot Root { get; }
	public UiCanvasTransform CanvasTransform { get; }
	public float TextScale => CanvasTransform.TextScale;

	internal UiRenderContext(UiRoot root, UiCanvasTransform canvasTransform) {
		Root = root;
		CanvasTransform = canvasTransform;
	}

	public Vector2 LogicalToTarget(Vector2 p) => UiCanvasLayout.LogicalToScreen(CanvasTransform, p);

	public RectI LogicalToScissor(RectF r) => UiCanvasLayout.LogicalToScissor(CanvasTransform, r);

	public SizeI LogicalSizeToTargetPixels(SizeF size) {
		int w = Math.Max(1, (int)MathF.Ceiling(size.Width * CanvasTransform.Scale.X));
		int h = Math.Max(1, (int)MathF.Ceiling(size.Height * CanvasTransform.Scale.Y));
		return new SizeI(w, h);
	}
}
