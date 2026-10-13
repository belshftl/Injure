// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Assets;

namespace Injure.Tests.Assets;

public sealed class DerivedAssetTests {
	[Fact]
	public static void DerivedValuesAreComputedLazilyAndRecomputedOnReload() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<PlainValue> a = ts.Get<PlainValue>("a.txt", "v1");
		int computes = 0;
		using DerivedAsset<PlainValue> upper = DerivedAsset.Create(a, v => {
			computes++;
			return new PlainValue(v.Text.ToUpperInvariant());
		});
		Assert.Equal(0, computes);
		Assert.Equal("V1", upper.Borrow().Value.Text);
		Assert.Equal("V1", upper.Borrow().Value.Text);
		Assert.Equal(1, computes);

		ts.Reload(a, "v2");
		AssetBorrow<PlainValue> b = upper.Borrow();
		Assert.Equal("V2", b.Value.Text);
		Assert.Equal(2ul, b.Version);
		Assert.Equal(2, computes);
		Assert.Contains("derived from test::a.txt", upper.DebugName);
	}

	[Fact]
	public static void DerivedVersionsKeepTheirInputsAlive() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<ResourceValue> a = ts.Get<ResourceValue>("a.txt", "v1");
		// the derived value references its input, which must stay alive as long as it does
		using DerivedAsset<PlainValue> wrap = DerivedAsset.Create(a, v => new PlainValue(v.Text + "!"));
		AssetPin<PlainValue> pin = wrap.Pin();
		ResourceValue v1 = a.Borrow().Value;

		ts.Reload(a, "v2");
		att.AtReloadBoundary();
		ts.Store.ApplyQueuedReloads();
		Assert.False(pin.IsCurrent);
		// the derived v1 wasn't recomputed yet, so it still pins the asset's v1
		Assert.False(v1.Disposed);
		Assert.Equal("v2!", wrap.Borrow().Value.Text);
		att.AtReloadBoundary();
		ts.Store.ApplyQueuedReloads();
		// recomputed, but the pin still holds the derived v1, and with it the asset's v1
		Assert.False(v1.Disposed);
		pin.Dispose();
		ts.Store.ApplyQueuedReloads();
		Assert.True(v1.Disposed);
	}

	[Fact]
	public static void DerivedValuesCanDependOnEachOtherAndOnSeveralInputs() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<PlainValue> a = ts.Get<PlainValue>("a.txt", "a1");
		Asset<PlainValue> b = ts.Get<PlainValue>("b.txt", "b1");
		using DerivedAsset<PlainValue> ab = DerivedAsset.Create(a, b, (x, y) => new PlainValue(x.Text + y.Text));
		using DerivedAsset<PlainValue> outer = DerivedAsset.Create(ab, v => new PlainValue($"[{v.Text}]"));
		Assert.Equal("[a1b1]", outer.Borrow().Value.Text);
		ts.Reload(b, "b2");
		// outer's input is ab's old version, so it's stale through ab
		Assert.Equal("[a1b2]", outer.Borrow().Value.Text);

		using DerivedAsset<PlainValue> general = DerivedAsset.Create<PlainValue>(
			[a, b, outer],
			ctx => new PlainValue(ctx.Get(a).Text + ctx.Get(outer).Text)
		);
		Assert.Equal("a1[a1b2]", general.Borrow().Value.Text);
		ts.Reload(a, "a3");
		Assert.Equal("a3[a3b2]", general.Borrow().Value.Text);
	}

	[Fact]
	public static void FailedComputationsKeepThePreviousVersion() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<PlainValue> a = ts.Get<PlainValue>("a.txt", "ok");
		using DerivedAsset<PlainValue> d = DerivedAsset.Create(a, v =>
			v.Text == "boom" ? throw new FormatException("bad") : new PlainValue(v.Text));
		AssetPin<PlainValue> pin = d.Pin();
		ts.Reload(a, "boom");
		Assert.Throws<FormatException>(() => d.Borrow());
		Assert.NotNull(d.LastException);
		Assert.Equal("ok", pin.Value.Text);
		ts.Reload(a, "fine");
		Assert.Equal("fine", d.Borrow().Value.Text);
		Assert.Null(d.LastException);
		pin.Dispose();
	}

	private sealed class BareDisposable : IDisposable {
		public bool Disposed { get; private set; }
		public void Dispose() => Disposed = true;
	}

	[Fact]
	public static void DerivedValuesFollowTheDisposalContract() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<PlainValue> a = ts.Get<PlainValue>("a.txt", "v1");
		BareDisposable? made = null;
		using DerivedAsset<BareDisposable> bad = DerivedAsset.Create(a, _ => made = new BareDisposable());
		Assert.Throws<InvalidOperationException>(() => bad.Borrow());
		Assert.True(made!.Disposed);

		using DerivedAsset<ResourceValue> res = DerivedAsset.Create(a, v => new ResourceValue(v.Text));
		ResourceValue r1 = res.Borrow().Value;
		Assert.True(r1.IsAssetStoreOwned);
		Assert.Null(r1.Custody!.AssetId);
		Assert.Contains("derived value", r1.Custody.ToString());
		Assert.Throws<InvalidOperationException>(r1.Dispose);
		ts.Reload(a, "v2");
		_ = res.Borrow();
		att.AtReloadBoundary();
		ts.Store.ApplyQueuedReloads();
		Assert.True(r1.Disposed);
	}

	[Fact]
	public static void DisposingRetiresAndStoreDisposalReclaims() {
		TestStore ts = new();
		ResourceValue kept, retired;
		DerivedAsset<ResourceValue> d1, d2;
		using (AssetThreadAttachment att = ts.Store.AttachCurrentThread()) {
			Asset<PlainValue> a = ts.Get<PlainValue>("a.txt", "v1");
			d1 = DerivedAsset.Create(a, v => new ResourceValue(v.Text));
			d2 = DerivedAsset.Create(a, v => new ResourceValue(v.Text));
			retired = d1.Borrow().Value;
			kept = d2.Borrow().Value;
			d1.Dispose();
			d1.Dispose();
			Assert.Throws<ObjectDisposedException>(() => d1.Borrow());
			att.AtReloadBoundary();
			ts.Store.ApplyQueuedReloads();
			Assert.True(retired.Disposed);
			Assert.False(kept.Disposed);
		}
		ts.Store.Dispose();
		Assert.True(kept.Disposed);
		Assert.Throws<ObjectDisposedException>(() => d2.Pin());
		d2.Dispose();
	}

	[Fact]
	public static void InputsMustBeNonEmptyAndFromOneStore() {
		using TestStore ts1 = new(), ts2 = new();
		Asset<PlainValue> a = ts1.Get<PlainValue>("a.txt", "x");
		Asset<PlainValue> b = ts2.Get<PlainValue>("b.txt", "y");
		Assert.Throws<ArgumentException>(() => DerivedAsset.Create<PlainValue>([], _ => new PlainValue("")));
		Assert.Throws<ArgumentException>(() => DerivedAsset.Create(a, b, (x, y) => x));
		Assert.Throws<ArgumentException>(() => {
			using DerivedAsset<PlainValue> d = DerivedAsset.Create<PlainValue>([a], ctx => ctx.Get(b));
			using AssetThreadAttachment att = ts1.Store.AttachCurrentThread();
			_ = d.Borrow();
		});
	}
}
