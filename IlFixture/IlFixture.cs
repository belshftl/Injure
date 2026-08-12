// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[module: SkipLocalsInit]

namespace IlFixture;

#pragma warning disable IDE0001, IDE0060, CS0168, CS0169, CS0626, CS0649, CS8618, CA2211, CA2255, IDE0360

// ===============================================================================================
// roundtrip tests
[StructLayout(LayoutKind.Explicit, Size = 40, Pack = 8)]
public unsafe struct Stuff {
	[FieldOffset(0)] public fixed byte Bytes[16];
	[FieldOffset(16)] public nint Ptr;
	[FieldOffset(24)] public delegate* unmanaged[Cdecl, SuppressGCTransition]<int, int> Funcptr;
	[FieldOffset(32)] public volatile int Flag;
}

public class InitProps {
	public int X { get; init; }
	public int Y { get; private init; }
	public required int Z { get; init; }
}

[InlineArray(4)]
public struct Four<T> where T : unmanaged {
	private T v;
}

public interface ICursor<TSelf> where TSelf : ICursor<TSelf>, allows ref struct {
	static abstract TSelf From(ref int origin);
	ref readonly int Current { get; }
	int Length { get; }
}

public ref struct Cursor : ICursor<Cursor> {
	private ref int head;
	private int len;

	public Cursor(ref int origin, int len) {
		head = ref origin;
		this.len = len;
	}

	public static Cursor From(ref int origin) => new(ref origin, 1);
	public ref readonly int Current => ref head;
	readonly int ICursor<Cursor>.Length => len;

	public void operator +=(int n) => head = ref Unsafe.Add(ref head, n);

	public static Cursor operator ++(Cursor c) {
		c.len++;
		return c;
	}

	public static Cursor operator checked ++(Cursor c) {
		c.len = checked(c.len + 1);
		return c;
	}
}

public static unsafe class Mechanism {
	public static int Counter { get => field; set => field = value < 0 ? 0 : value; }

	[ModuleInitializer]
	public static void Initializer() => Counter = Sizeof<Stuff>();

	public static int Sizeof<T>() where T : unmanaged => sizeof(T);

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static int two(int x) => x + x;

	private static int three(int x) => x * 3;

	private static ref readonly int identity(in int v) => ref v;

	public static int Funcptrs(int[] data, string tag) {
		delegate* unmanaged[Cdecl]<int, int> native = &two;
		delegate*<int, int> managed = &three;
		delegate*<in int, ref readonly int> byref = &identity;

		int* buf = stackalloc int[8];
		Span<int> span = stackalloc int[4] { 1, 2, 3, 4 };
		fixed (int* a = data)
		fixed (char* b = tag)
		fixed (int* c = span) {
			buf[0] = *a + *b + *c;
		}
		return native(buf[0]) + managed(span[3]) + byref(in span[0]);
	}

	public static int Inline() {
		Four<int> a = default;
		a[0] = 7;
		Span<int> b = a[..];
		var c = Cursor.From(ref b[0]);
		c += 1;
		return Peek(c);
	}

	public static int Peek<T>(T c) where T : ICursor<T>, allows ref struct => c.Current;

	public static int Sum(int seed, __arglist) {
		ArgIterator it = new(__arglist);
		for (int n = it.GetRemainingCount(); n > 0; n--)
			seed += __refvalue(it.GetNextArg(), int);
		return seed;
	}

	public static (int Value, Type Kind) Refany(int x) {
		TypedReference tr = __makeref(x);
		__refvalue(tr, int) = Sum(x, __arglist(1, 2, 3));
		return (__refvalue(tr, int), __reftype(tr));
	}

	public static IEnumerable<int> Walk(int[] data) {
		for (int i = 0; i < data.Length; i++) {
			int v;
			{
				ref int slot = ref data[i];
				ReadOnlySpan<byte> tag = "abc"u8;
				slot += tag.HeadOrZero;
				v = slot;
			}
			try {
				yield return v;
			} finally {
				Counter = v;
			}
		}
	}

	public static async IAsyncEnumerable<int> WalkAsync(int[] data, [EnumeratorCancellation] CancellationToken ct = default) {
		foreach (int v in Walk(data)) {
			{
				Span<int> s = data.AsSpan(0, 1);
				s[0] += v;
			}
			await Task.Yield();
			ct.ThrowIfCancellationRequested();
			yield return v;
		}
	}

	public static int Classify(int n) {
		try {
			switch (n) {
			case 0:
				goto case 2;
			case 1:
				return checked(int.MaxValue - n);
			case 2:
				throw new InvalidOperationException(nameof(Classify));
			default:
				return unchecked(n * 0x7fffffff);
			}
		} catch (Exception ex) when (ex.HResult is < 0 and not -1 || ex.Message.Length > 0) {
			return ex.Message.Length;
		}
	}

	public static int Volatile(ref Stuff s) {
		s.Flag = 1;
		return s.Flag;
	}

	public static int Switch(int n) => n switch {
		0 => 1, 1 => 2, 2 => 4, 3 => 8, 4 => 16, 5 => 32, 6 => 64, 7 => 128,
		8 => 256, 9 => 512, 10 => 1024, 11 => 2048, _ => 0,
	};

	public static int Grid(int[,] g, int i, int j) {
		g[i, j] = g[j, i];
		ref int slot = ref g[i, j];
		return slot + g.GetLength(0);
	}

	public static ref readonly T ElementRef<T>(T[] a, int i) => ref a[i];

	public static Expression<Func<int>> Tokens() {
		int captured = Counter;
		return () => captured + Sizeof<Stuff>();
	}

	public static Expression<Func<int>> FieldTokens() {
		Box<int> h = new();
		return () => h.Value + Box<int>.Shared;
	}

	public static string Str() => "a\0b\U0001F600c";
}

public static class Extensions {
	extension(ReadOnlySpan<byte> sp) {
		public byte HeadOrZero => sp.IsEmpty ? (byte)0 : sp[0];
		public bool LengthAtLeastTwo() => sp.Length >= 2;
	}
}

public static class LargeGeneric<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z>
	where A : B
	where B : C
	where C : D
	where D : E
	where E : F
	where F : G
	where G : H
	where H : I
	where I : J
	where J : K
	where K : L
	where L : M
	where M : N
	where N : O
	where O : P
	where P : Q
	where Q : R
	where R : S
	where S : T
	where T : U
	where U : V
	where V : W
	where W : X
	where X : Y
	where Y : Z {
	public static class Nested<_A> where _A : A {
		public static Z Up<_T>(_T a) where _T : _A => a;
	}
}

public static class Locals {
	private static extern int v();
	private static extern void u(params int[] _);

	public static void M() {
		int _0 = v(); int _1 = v(); int _2 = v(); int _3 = v(); int _4 = v(); int _5 = v(); int _6 = v(); int _7 = v(); int _8 = v();
		int _9 = v(); int _a = v(); int _b = v(); int _c = v(); int _d = v(); int _e = v(); int _f = v(); int _10 = v();
		int _11 = v(); int _12 = v(); int _13 = v(); int _14 = v(); int _15 = v(); int _16 = v(); int _17 = v(); int _18 = v();
		int _19 = v(); int _1a = v(); int _1b = v(); int _1c = v(); int _1d = v(); int _1e = v(); int _1f = v(); int _20 = v();
		int _21 = v(); int _22 = v(); int _23 = v(); int _24 = v(); int _25 = v(); int _26 = v(); int _27 = v(); int _28 = v();
		int _29 = v(); int _2a = v(); int _2b = v(); int _2c = v(); int _2d = v(); int _2e = v(); int _2f = v(); int _30 = v();
		int _31 = v(); int _32 = v(); int _33 = v(); int _34 = v(); int _35 = v(); int _36 = v(); int _37 = v(); int _38 = v();
		int _39 = v(); int _3a = v(); int _3b = v(); int _3c = v(); int _3d = v(); int _3e = v(); int _3f = v(); int _40 = v();
		int _41 = v(); int _42 = v(); int _43 = v(); int _44 = v(); int _45 = v(); int _46 = v(); int _47 = v(); int _48 = v();
		int _49 = v(); int _4a = v(); int _4b = v(); int _4c = v(); int _4d = v(); int _4e = v(); int _4f = v(); int _50 = v();
		int _51 = v(); int _52 = v(); int _53 = v(); int _54 = v(); int _55 = v(); int _56 = v(); int _57 = v(); int _58 = v();
		int _59 = v(); int _5a = v(); int _5b = v(); int _5c = v(); int _5d = v(); int _5e = v(); int _5f = v(); int _60 = v();
		int _61 = v(); int _62 = v(); int _63 = v(); int _64 = v(); int _65 = v(); int _66 = v(); int _67 = v(); int _68 = v();
		int _69 = v(); int _6a = v(); int _6b = v(); int _6c = v(); int _6d = v(); int _6e = v(); int _6f = v(); int _70 = v();
		int _71 = v(); int _72 = v(); int _73 = v(); int _74 = v(); int _75 = v(); int _76 = v(); int _77 = v(); int _78 = v();
		int _79 = v(); int _7a = v(); int _7b = v(); int _7c = v(); int _7d = v(); int _7e = v(); int _7f = v(); int _80 = v();
		int _81 = v(); int _82 = v(); int _83 = v(); int _84 = v(); int _85 = v(); int _86 = v(); int _87 = v(); int _88 = v();
		int _89 = v(); int _8a = v(); int _8b = v(); int _8c = v(); int _8d = v(); int _8e = v(); int _8f = v(); int _90 = v();
		int _91 = v(); int _92 = v(); int _93 = v(); int _94 = v(); int _95 = v(); int _96 = v(); int _97 = v(); int _98 = v();
		int _99 = v(); int _9a = v(); int _9b = v(); int _9c = v(); int _9d = v(); int _9e = v(); int _9f = v(); int _a0 = v();
		int _a1 = v(); int _a2 = v(); int _a3 = v(); int _a4 = v(); int _a5 = v(); int _a6 = v(); int _a7 = v(); int _a8 = v();
		int _a9 = v(); int _aa = v(); int _ab = v(); int _ac = v(); int _ad = v(); int _ae = v(); int _af = v(); int _b0 = v();
		int _b1 = v(); int _b2 = v(); int _b3 = v(); int _b4 = v(); int _b5 = v(); int _b6 = v(); int _b7 = v(); int _b8 = v();
		int _b9 = v(); int _ba = v(); int _bb = v(); int _bc = v(); int _bd = v(); int _be = v(); int _bf = v(); int _c0 = v();
		int _c1 = v(); int _c2 = v(); int _c3 = v(); int _c4 = v(); int _c5 = v(); int _c6 = v(); int _c7 = v(); int _c8 = v();
		int _c9 = v(); int _ca = v(); int _cb = v(); int _cc = v(); int _cd = v(); int _ce = v(); int _cf = v(); int _d0 = v();
		int _d1 = v(); int _d2 = v(); int _d3 = v(); int _d4 = v(); int _d5 = v(); int _d6 = v(); int _d7 = v(); int _d8 = v();
		int _d9 = v(); int _da = v(); int _db = v(); int _dc = v(); int _dd = v(); int _de = v(); int _df = v(); int _e0 = v();
		int _e1 = v(); int _e2 = v(); int _e3 = v(); int _e4 = v(); int _e5 = v(); int _e6 = v(); int _e7 = v(); int _e8 = v();
		int _e9 = v(); int _ea = v(); int _eb = v(); int _ec = v(); int _ed = v(); int _ee = v(); int _ef = v(); int _f0 = v();
		int _f1 = v(); int _f2 = v(); int _f3 = v(); int _f4 = v(); int _f5 = v(); int _f6 = v(); int _f7 = v(); int _f8 = v();
		int _f9 = v(); int _fa = v(); int _fb = v(); int _fc = v(); int _fd = v(); int _fe = v(); int _ff = v();

		int _100 = v();

		u(
			_0, _1, _2, _3, _4, _5, _6, _7, _8, _9, _a, _b, _c, _d, _e, _f, _10,
			_11, _12, _13, _14, _15, _16, _17, _18, _19, _1a, _1b, _1c, _1d, _1e, _1f, _20,
			_21, _22, _23, _24, _25, _26, _27, _28, _29, _2a, _2b, _2c, _2d, _2e, _2f, _30,
			_31, _32, _33, _34, _35, _36, _37, _38, _39, _3a, _3b, _3c, _3d, _3e, _3f, _40,
			_41, _42, _43, _44, _45, _46, _47, _48, _49, _4a, _4b, _4c, _4d, _4e, _4f, _50,
			_51, _52, _53, _54, _55, _56, _57, _58, _59, _5a, _5b, _5c, _5d, _5e, _5f, _60,
			_61, _62, _63, _64, _65, _66, _67, _68, _69, _6a, _6b, _6c, _6d, _6e, _6f, _70,
			_71, _72, _73, _74, _75, _76, _77, _78, _79, _7a, _7b, _7c, _7d, _7e, _7f, _80,
			_81, _82, _83, _84, _85, _86, _87, _88, _89, _8a, _8b, _8c, _8d, _8e, _8f, _90,
			_91, _92, _93, _94, _95, _96, _97, _98, _99, _9a, _9b, _9c, _9d, _9e, _9f, _a0,
			_a1, _a2, _a3, _a4, _a5, _a6, _a7, _a8, _a9, _aa, _ab, _ac, _ad, _ae, _af, _b0,
			_b1, _b2, _b3, _b4, _b5, _b6, _b7, _b8, _b9, _ba, _bb, _bc, _bd, _be, _bf, _c0,
			_c1, _c2, _c3, _c4, _c5, _c6, _c7, _c8, _c9, _ca, _cb, _cc, _cd, _ce, _cf, _d0,
			_d1, _d2, _d3, _d4, _d5, _d6, _d7, _d8, _d9, _da, _db, _dc, _dd, _de, _df, _e0,
			_e1, _e2, _e3, _e4, _e5, _e6, _e7, _e8, _e9, _ea, _eb, _ec, _ed, _ee, _ef, _f0,
			_f1, _f2, _f3, _f4, _f5, _f6, _f7, _f8, _f9, _fa, _fb, _fc, _fd, _fe, _ff, _100
		);

	}
}

// ===============================================================================================
// generics tests
public sealed class Outer<T> {
	private Outer() {}

	public sealed class Inner<U> {
		private Inner() {}

		public static V Pick<V>(T t, U u, V v) => v;
		public static int Arity() => 3;
	}

	public static T Echo(T value) => value;
}

public static class GenericCallers {
	// MemberRef parent is a TypeSpec of Outer`1/Inner`1<int32, int64>, MethodSpec over <string>
	public static string Closed() => Outer<int>.Inner<long>.Pick<string>(1, 2L, "x");

	// similar to `Closed` but the type parameters are the caller's method generic parameters
	public static V Open<A, B, V>(A a, B b, V v) => Outer<A>.Inner<B>.Pick<V>(a, b, v);

	// TypeSpec mixing a method generic parameter with a closed parameter
	public static Type TokenOfOpen<A>() => typeof(Outer<A>.Inner<int>);

	// newobj on a generic instance
	public static List<Outer<A>> Nest<A>() => new(4);

	// generic method whose argument is itself an instantiation
	public static int CountOf<A>(IEnumerable<A> items) => items.Count();
}

public class Box<T> {
	public T Value;
	public static T Shared;
}

public static class FieldCallers {
	// `ldfld !0 Box<!!0>::Value` with TypeSpec parent using the caller's method parameter
	public static V ReadOpen<V>(Box<V> b) => b.Value;

	// similar to `ReadOpen` but with closed parameters
	public static int ReadClosed(Box<int> b) => b.Value + Box<int>.Shared;
}
