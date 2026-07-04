// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw;
using Injure.Primitives;

namespace Injure.Ui;

public sealed class UiPlaced : UiWidget {
	private readonly UiWidget[] children;

	public UiWidget Child { get; }
	public UiPlacement Placement { get; set; }

	public override IReadOnlyList<UiWidget> Children => children;

	public UiPlaced(UiWidget child, UiPlacement placement) {
		Child = child ?? throw new ArgumentNullException(nameof(child));
		Placement = placement;
		children = [child];
		child.AttachToParent(this);
	}

	protected override SizeF MeasureCore(in UiLayoutContext ctx, in UiSizeConstraint constraint) {
		UiSizeConstraint childConstraint = UiPlacementUtil.GetChildMeasureConstraint(in constraint, Placement);
		SizeF childDesired = Child.Measure(in ctx, in childConstraint);
		return UiPlacementUtil.GetSurfaceDesiredSize(in constraint, Placement, childDesired);
	}

	protected override void ArrangeCore(in RectF rect) {
		RectF childRect = UiPlacementUtil.ResolveChildRect(rect, Child.DesiredSize, Placement);
		Child.Arrange(childRect);
	}

	public override void Render(Canvas cv, in UiRenderContext ctx) {
		if (Child.Visible)
			Child.Render(cv, in ctx);
	}
}
