// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Runtime.Modif.CallDispatch;

namespace Injure.Mods.Runtime.Tests.Modif.CallDispatch;

public sealed class IndirectCallDispatchTests {
	[MethodImpl(MethodImplOptions.NoInlining)] private static int add1(int value) => value + 1;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int add2(int value) => value + 2;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int add3(int value) => value + 3;
	private static MethodInfo method(string name) => typeof(IndirectCallDispatchTests).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

#pragma warning disable CS0626 // method is marked extern and has no attributes
	private static extern void rva0();
#pragma warning restore CS0626 // method is marked extern and has no attributes
#pragma warning disable SYSLIB1054 // mark with LibraryImportAttribute instead of DllImportAttribute
	[DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
	private static extern void abort();
#pragma warning restore SYSLIB1054 // mark with LibraryImportAttribute instead of DllImportAttribute
	[UnsafeAccessor(UnsafeAccessorKind.Method, Name = "GetMethodDescriptor")]
	private static extern RuntimeMethodHandle unsafeAccessor(System.Reflection.Emit.DynamicMethod method);

	private static string selfPath => typeof(IndirectCallDispatchTests).Assembly.Location;

	private static (int Slot, AssemblyLoadContext Alc, Assembly Assembly) allocSeparateAsm(string methodName) {
		AssemblyLoadContext alc = new($"{nameof(IndirectCallDispatchTests)}_{Guid.NewGuid()}", isCollectible: true);
		Assembly asm = alc.LoadFromStream(new MemoryStream(File.ReadAllBytes(selfPath)));
		MethodInfo target = asm
			.GetType(typeof(IndirectCallDispatchTests).FullName!, throwOnError: true)!
			.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
		int slot = IndirectCallDispatch.AllocateSlot(target);
		return (slot, alc, asm);
	}

	private static (int Slot, string Name) allocSeparateAsmAndCleanup(string methodName) {
		AssemblyLoadContext alc = new($"{nameof(IndirectCallDispatchTests)}_{Guid.NewGuid()}", isCollectible: true);
		Assembly asm = alc.LoadFromStream(new MemoryStream(File.ReadAllBytes(selfPath)));
		MethodInfo target = asm
			.GetType(typeof(IndirectCallDispatchTests).FullName!, throwOnError: true)!
			.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
		int slot = IndirectCallDispatch.AllocateSlot(target);
		IndirectCallDispatch.ClearForAssembly(asm);
		alc.Unload();
		return (slot, target.Name);
	}

	private static IlMethodBody makeBody() =>
		IlMethodBody.CreateEmpty(IlRefFactory.Method(typeof(object).GetMethod(
			nameof(ToString), BindingFlags.Instance | BindingFlags.Public
		)!));

	// ==========================================================================================
	// allocation
	[Fact]
	public static void SlotCorrectlyPointsAtItsTarget() {
		MethodInfo target = method(nameof(add1));
		int slot = IndirectCallDispatch.AllocateSlot(target);

		Assert.Equal(target.MethodHandle.GetFunctionPointer(), IndirectCallDispatch.GetTarget(slot));
		Assert.Same(target, IndirectCallDispatch.GetTargetMethod(slot));
		Assert.True(IndirectCallDispatch.IsLive(slot));
	}

	[Fact]
	public static void SameTargetInternsToSameSlot() {
		MethodInfo target = method(nameof(add2));

		Assert.Equal(IndirectCallDispatch.AllocateSlot(target), IndirectCallDispatch.AllocateSlot(target));
	}

	[Fact]
	public static void DifferentTargetsGoIntoDifferentSlots() =>
		Assert.NotEqual(
			IndirectCallDispatch.AllocateSlot(method(nameof(add1))),
			IndirectCallDispatch.AllocateSlot(method(nameof(add2)))
		);

	[Fact]
	public static void TableGrowthWorks() {
		List<(int Slot, MethodInfo Target)> allocated = new();
		foreach (MethodInfo target in typeof(string).GetMethods().Where(static m => !m.ContainsGenericParameters).Take(64))
			allocated.Add((IndirectCallDispatch.AllocateSlot(target), target));
		foreach ((int slot, MethodInfo target) in allocated)
			Assert.Equal(target.MethodHandle.GetFunctionPointer(), IndirectCallDispatch.GetTarget(slot));
	}

	// ==========================================================================================
	// unallocated slots
	[Fact]
	public static void AnOutOfBoundsSlotIsUnavailable() {
		DispatchUnavailableException ex = Assert.Throws<DispatchUnavailableException>(() => IndirectCallDispatch.GetTarget(int.MaxValue));
		Assert.Equal(int.MaxValue, ex.Slot);
		Assert.False(IndirectCallDispatch.IsLive(int.MaxValue));
		Assert.Null(IndirectCallDispatch.GetTargetMethod(int.MaxValue));
	}

	[Fact]
	public static void SlotInsideCapacityButNullIsUnavailable() {
		(int allocated, _) = allocSeparateAsmAndCleanup(nameof(add1));
		while (allocated + 1 == IndirectCallDispatch.TableSize)
			(allocated, _) = allocSeparateAsmAndCleanup(nameof(add1));
		Assert.Throws<DispatchUnavailableException>(() => IndirectCallDispatch.GetTarget(allocated + 1));
	}

	// ==========================================================================================
	// clearing
	[Fact]
	public static void ClearingAnAssemblyMakesItsSlotsThrowAndMentionTheTargetInTheMessage() {
		Assert.SkipWhen(string.IsNullOrEmpty(selfPath), "test assembly has no on-disk location");
		(int slot, string name) = allocSeparateAsmAndCleanup(nameof(add1));
		DispatchUnavailableException ex = Assert.Throws<DispatchUnavailableException>(() => IndirectCallDispatch.GetTarget(slot));
		Assert.Contains(name, ex.Message, StringComparison.Ordinal);
		Assert.False(IndirectCallDispatch.IsLive(slot));
	}

	[Fact]
	public static void ClearingAnAssemblyLeavesOtherSlotsAlone() {
		Assert.SkipWhen(string.IsNullOrEmpty(selfPath), "test assembly has no on-disk location");
		MethodInfo own = method(nameof(add2));
		int keep = IndirectCallDispatch.AllocateSlot(own);
		allocSeparateAsmAndCleanup(nameof(add2));
		Assert.True(IndirectCallDispatch.IsLive(keep));
		Assert.Equal(own.MethodHandle.GetFunctionPointer(), IndirectCallDispatch.GetTarget(keep));
	}

	[Fact]
	public static void AReloadedTargetGetsAFreshSlot() {
		Assert.SkipWhen(string.IsNullOrEmpty(selfPath), "test assembly has no on-disk location");
		(int first, _) = allocSeparateAsmAndCleanup(nameof(add3));
		(int second, AssemblyLoadContext alc, Assembly asm) = allocSeparateAsm(nameof(add3));
		Assert.NotEqual(first, second);
		Assert.True(IndirectCallDispatch.IsLive(second));
		IndirectCallDispatch.ClearForAssembly(asm);
		alc.Unload();
	}

	[Fact]
	public static void ClearingASlotDropsTheTargetButKeepsItReadable() {
		Assert.SkipWhen(string.IsNullOrEmpty(selfPath), "test assembly has no on-disk location");
		(int slot, _) = allocSeparateAsmAndCleanup(nameof(add1));
		Assert.Null(IndirectCallDispatch.GetTargetMethod(slot));
	}

	// ==========================================================================================
	// integration with IlEmitter
	[Fact]
	public static void IndirectCallEmitsExpectedInstructions() {
		MethodInfo m = method(nameof(add1));
		IlMethodBody body = makeBody();
		IlTransactionCore core = new(body, IlTest.OwnerId, IlTest.LocalId, default, IlCallDispatch.Instance);
		core.EmitAtBoundary(0, e => e.IndirectCall(m));
		core.Commit();
		Assert.Equal(3, body.Instructions.Count);
		Assert.Equal(ILOpCode.Ldc_i4, body.Instructions[0].OpCode);
		Assert.Equal(ILOpCode.Call, body.Instructions[1].OpCode);
		Assert.Equal(ILOpCode.Calli, body.Instructions[2].OpCode);
		IlCallSiteOperand operand = Assert.IsType<IlCallSiteOperand>(body.Instructions[2].Operand);
		Assert.Equal(IlRefFactory.Method(m).Signature, operand.Signature);
	}

	[Fact]
	public static void IndirectCallRejectsOpenGenericMethod() {
		MethodInfo onNonGenericType = typeof(Array).GetMethod(
			nameof(Array.Empty), BindingFlags.Static | BindingFlags.Public
		)!;
		MethodInfo onOpenGenericType = typeof(List<>).GetMethod(
			nameof(List<>.Add), BindingFlags.Instance | BindingFlags.Public
		)!;
		IlMethodBody body = makeBody();
		IlTransactionCore core = new(body, IlTest.OwnerId, IlTest.LocalId, default, IlCallDispatch.Instance);
		Assert.Throws<ArgumentException>(() => core.EmitAtBoundary(0, e => e.IndirectCall(onNonGenericType)));
		Assert.Throws<ArgumentException>(() => core.EmitAtBoundary(0, e => e.IndirectCall(onOpenGenericType)));
	}

	[Fact]
	public static void IndirectCallRejectsAbstractMethod() {
		MethodInfo m = typeof(System.Globalization.Calendar).GetMethod(
			nameof(System.Globalization.Calendar.GetYear), BindingFlags.Instance | BindingFlags.Public
		)!;
		IlMethodBody body = makeBody();
		IlTransactionCore core = new(body, IlTest.OwnerId, IlTest.LocalId, default, IlCallDispatch.Instance);
		Assert.Throws<ArgumentException>(() => core.EmitAtBoundary(0, e => e.IndirectCall(m)));
	}

	[Fact]
	public static void IndirectCallRejectsRva0Method() {
		MethodInfo m = method(nameof(rva0));
		IlMethodBody body = makeBody();
		IlTransactionCore core = new(body, IlTest.OwnerId, IlTest.LocalId, default, IlCallDispatch.Instance);
		Assert.Throws<ArgumentException>(() => core.EmitAtBoundary(0, e => e.IndirectCall(m)));
	}

	[Fact]
	public static void IndirectCallAcceptsPInvokeMethod() {
		MethodInfo m = method(nameof(abort));
		IlMethodBody body = makeBody();
		IlTransactionCore core = new(body, IlTest.OwnerId, IlTest.LocalId, default, IlCallDispatch.Instance);
		core.EmitAtBoundary(0, e => e.IndirectCall(m));
		core.Commit();
		Assert.Equal(3, body.Instructions.Count);
		IlCallSiteOperand operand = Assert.IsType<IlCallSiteOperand>(body.Instructions[2].Operand);
		Assert.Equal(IlRefFactory.Method(m).Signature, operand.Signature);
	}

	[Fact]
	public static void IndirectCallAcceptsUnsafeAccessorMethod() {
		MethodInfo m = method(nameof(unsafeAccessor));
		IlMethodBody body = makeBody();
		IlTransactionCore core = new(body, IlTest.OwnerId, IlTest.LocalId, default, IlCallDispatch.Instance);
		core.EmitAtBoundary(0, e => e.IndirectCall(m));
		core.Commit();
		Assert.Equal(3, body.Instructions.Count);
		IlCallSiteOperand operand = Assert.IsType<IlCallSiteOperand>(body.Instructions[2].Operand);
		Assert.Equal(IlRefFactory.Method(m).Signature, operand.Signature);
	}
}
