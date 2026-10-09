# mods/canonical-short-form.md

This document explains how some IL instructions have several byte encodings, why the IL-authoring APIs represent every instruction in a single *canonical form*, and how the encoder *shortens* code back to its smallest encodings when it writes a patched method body.

This document is informative, not normative. [`mods/canonical-short-form-normative.md`](/docs/mods/canonical-short-form-normative.md) defines these concepts formally and proves the properties that this document states. If the two documents disagree, the normative document applies.

---

## Why one operation can have multiple encodings

In CIL, an instruction consists of an opcode name and an operand: for example, `ldarg 1` pushes argument 1 onto the stack. Instructions, by necessity, have to be stored as bytes inside a method body: an instruction occupies one or two bytes that identify the opcode, followed by the bytes of the operand, if any.

CIL oftentimes includes several opcodes that perform the same operaion and differ only in how many bytes is spent on the operand. The reason behind that will be explained in a bit. Each of these does the exact same thing, push argument 1:

| Instruction | Bytes         | Size    |
| ----------- | ------------- | ------- |
| `ldarg 1`   | `FE 09 01 00` | 4 bytes |
| `ldarg.s 1` | `0E 01`       | 2 bytes |
| `ldarg.1`   | `03`          | 1 byte  |

`ldarg` stores the argument index as a 2-byte unsigned integer, so it can go up to `ldarg 65535`. `ldarg.s` (`.s` for "short") stores the index as a single byte, so it can only go up to `ldarg.s 255`. `ldarg.1` doesn't store an operand at all; the index is hardcoded into the opcode (only `ldarg.0` through `ldarg.3` exist).

Again, all three behave identically and only have different sizes.

Compilers use the smallest encoding that can hold the operand. This saves a lot of memory, since it's extremely rare that methods have that many arguments or locals, and constants often tend to be small. For example, `ldarg.0` is used to load the receiver (`this`) of an instance method, so it appears everywhere in instance methods; if the short `ldarg.0` didn't exist, every single `ldarg 0` would make the method 4 bytes larger (instead of only 1 byte larger). As another example, since it's extremely rare that a method has more than 256 locals, `ldloc.s` is sufficient almost all of the time, and it would be wasteful to always use the 2-byte-larger `ldloc`.

The following *families* of encodings perform the same operation. The long encoding is the one that is the only strictly necessary one; the *compact encodings* are smaller but have fewer encodable operands.

| Operation             | Long encoding                  | Compact encodings                                                                              |
| --------------------- | ------------------------------ | ---------------------------------------------------------------------------------------------- |
| Load argument         | `ldarg` (4 bytes, 0 to 65535)  | `ldarg.s` (2 bytes, 0 to 255), `ldarg.0` to `ldarg.3` (1 byte)                                 |
| Load argument address | `ldarga` (4 bytes, 0 to 65535) | `ldarga.s` (2 bytes, 0 to 255)                                                                 |
| Store argument        | `starg` (4 bytes, 0 to 65535)  | `starg.s` (2 bytes, 0 to 255)                                                                  |
| Load local            | `ldloc` (4 bytes, 0 to 65535)  | `ldloc.s` (2 bytes, 0 to 255), `ldloc.0` to `ldloc.3` (1 byte)                                 |
| Load local address    | `ldloca` (4 bytes, 0 to 65535) | `ldloca.s` (2 bytes, 0 to 255)                                                                 |
| Store local           | `stloc` (4 bytes, 0 to 65535)  | `stloc.s` (2 bytes, 0 to 255), `stloc.0` to `stloc.3` (1 byte)                                 |
| Load `int` constant   | `ldc.i4` (5 bytes, any `int`)  | `ldc.i4.s` (2 bytes, -128 to 127), `ldc.i4.m1` (1 byte, -1), `ldc.i4.0` to `ldc.i4.8` (1 byte) |

Branches also have families, but they're not listed in the table above; see [*How branches are encoded*](#how-branches-are-encoded).

Every other opcode has exactly one encoding and no alternate compact encodings, for example `add`, `call`, `ldfld`, `ldc.i8`, `ldc.r4`, `switch`, etc.

## How branches are encoded

Typically, IL-authoring APIs describe the target of a branch as a label; this includes Injure's `IlLabel` and MonoMod's `ILLabel`. This is much more convenient than what CIL actually does from a developer perspective, which is why patching frameworks opt to use labels.

In pure CIL, there are no labels. Instead, a branch stores the *displacement* to its target. A displacement is the signed number of bytes from the **end** of the branch instruction to the **start** of the target instruction. A positive displacement jumps forward (or downward, if you visualize code as executing top-to-bottom) and a negative displacement jumps backward (or upward).

For an example, consider this C# method:
```cs
public static int Not(bool arg) {
    if (arg)
        return 0;
    return 1;
}
```

When compiled to IL, it can produce something like:

| Offset | Bytes   | Instruction   |
| ------ | ------- | ------------- |
| `0000` | `02`    | `ldarg.0`     |
| `0001` | `2C 02` | `brfalse.s L` |
| `0003` | `16`    | `ldc.i4.0`    |
| `0004` | `2A`    | `ret`         |
| `0005` | `17`    | `L: ldc.i4.1` |
| `0006` | `2A`    | `ret`         |

`brfalse.s` **ends** at offset `0003`, and `L` starts at offset `0005`, so the branch stores the displacement 2.

(Note: a real compiler might emit `ldarg.0`, `ldc.i4.0`, `ceq`, `ret` instead; this is for explanatory purposes.)

Each branch opcode has a long encoding and a compact encoding:
- The long encoding uses a 4-byte signed integer for displacement. As such, it occupies 5 bytes, and it can jump to any instruction in any realistically-sized method body.
- The compact encoding, with the suffix `.s`, uses a single signed byte for displacement. As such, the instruction occupies 2 bytes, and it can jump only to targets from 128 bytes to before the end of the branch to 127 bytes after it.

The pairs are `br`/`br.s`, `brfalse`/`brfalse.s`, `brtrue`/`brtrue.s`, `beq`/`beq.s`, `bne.un`/`bne.un.s`, `bge`/`bge.s`, `bgt`/`bgt.s`, `ble`/`ble.s`, `blt`/`blt.s`, the `.un` variants of the last four, and `leave`/`leave.s`. Notably, `switch` stores one 4-byte displacement for each target and has no compact encoding.

The displacement of a branch is the total size of the instructions between the branch and its target; for a backward branch, this includes the branch itself and the target. Because of that, whether a branch can become compact depends on the encodings of all of the instructions in between, including other branches and sometimes itself.

## Why this complicates patching

What a patch does ultimately boils down to finding instructions and inserting new ones, sometimes also changing/removing existing ones. Having multiple encodings that mean the same thing make almost all of that harder:
- **Finding instructions.** If a patch wants to find "load argument 1", it must match the first occurrence of either `ldarg 1`, `ldarg.s 1`, and `ldarg.1`. If a patch wants to find "load int32 constant 42", it must look for both `ldc.i4 42` and `ldc.i4.s 42`, fish out the operand from whichever it found first, and sign-extend to int32 if necessary. This can technically be made nicer with sugar APIs, but doesn't fix the underlying problem. `ldarg.1` and `ldarg 1` are also easy to confuse.
- **Inserting new instructions.** Even if branches didn't exist, one would also always need to either know what the most efficient encoding for a given instruction is or accidentally emit oversized code. The main problem, though, is with branches. Inserting a new instruction between a compact branch and its target increases the magnitude of its displacement, which can then go over -128/127 bytes, so then the patch would have to rewrite the branch to the long encoding (including correctly computing the new displacement if it's a backward branch), which then increases the displacements of other branches around it, and so on. And messing it up makes the entire method body invalid.
- **Changing instructions.** Changing the constant of `ldc.i4.s 100` to 1000 requires the long encoding `ldc.i4 1000`, so it must be entirely swapped out for a new opcode. Changing the argument of `ldarg.1` to argument 2 also requires swapping out the instruction entirely rather than modifying some kind of operand. Also see the branch problem in the last point; it applies here too.
- **Removing instructions** actually doesn't get harder, though it can make existing long branches now suboptimally-encoded since they could be made compact.

## Canonical form

The solution is using exactly one encoding for each operation. The *canonical encoding* of each family is its long encoding, the one that could in principle take the place of any other encoding in the family at the cost of size. The canonical encodings are `ldarg`, `ldarga`, `starg`, `ldloc`, `ldloca`, `stloc`, `ldc.i4`, and the long encoding of each branch. An opcode with only one encoding is its own canonical encoding.

To *canonicalize* an instruction is to replace its encoding with the canonical encoding of its family, and keep its operand and prefixes (such as `volatile.` or `constrained.`). For example:

| Instruction    | Canonical form |
| -------------- | -------------- |
| `ldarg.1`      | `ldarg 1`      |
| `ldarg.s 4`    | `ldarg 4`      |
| `ldloc.0`      | `ldloc 0`      |
| `ldc.i4.m1`    | `ldc.i4 -1`    |
| `ldc.i4.s 100` | `ldc.i4 100`   |
| `brfalse.s L`  | `brfalse L`    |
| `add`          | `add`          |

This framework canonicalizes every instruction when a method body is decoded. All IL-authoring APIs see and emit only canonical instructions. For example, `MatchIl.Ldarg(1)` unambiguously matches "load argument 1", since any `ldarg.1` or `ldarg.s 1` the method used to have got converted into `ldarg 1` before being presented to the manipulator.

Two instruction sequences that are only different in whether they use a long or compact encoding of the same family at each instruction are *equivalent*: they have the same canonical form, and they behave identically. Their branches store different displacements, but they target the same instructions.

## The elephant in the room

Canonical encodings are large. `ldarg.0` goes from 1 byte to 4, `ldc.i4.0` goes from 1 byte to 5, `br.s` goes from 2 bytes to 5; since most IL in a typical method makes use of compact encodings, canonical code can end up being several times larger than the original. For example, Roslyn compiles

```csharp
public static int Sum(int[] a) {
	int s = 0;
	for (int i = 0; i < a.Length; i++)
		s += a[i];
	return s;
}
```

to 24 bytes of IL in a release build, and its canonical form is 75 bytes, which is over 3 times larger.

If, after a manipulator is done patching a method, the canonical instructions were encoded back into the method body as-is, every single patched method would grow to several times its original size, even if the patch only changed one instruction. This affects more than just memory usage, as the JIT uses IL size as part of its inlining decisions, so a small method that got grown like this could stop being inlined.

## Shortening

Instead, when the encoder writes a method body, it *shortens* the code: it replaces each instruction with the smallest encoding that represents the same operation and operand.

### Shortening instructions other than branches

For an instruction other than a branch, the smallest encoding depends only on its operand, so it's easy to select.

| Canonical    | Shortened      |
| ------------ | -------------- |
| `ldarg 2`    | `ldarg.2`      |
| `ldarg 200`  | `ldarg.s 200`  |
| `ldarg 300`  | `ldarg 300`    |
| `ldc.i4 -1`  | `ldc.i4.m1`    |
| `ldc.i4 100` | `ldc.i4.s 100` |
| `ldc.i4 200` | `ldc.i4 200`   |

### Shortening branches

A branch can use its compact encoding only if its displacement fits in 1 byte, and its displacement depends on the encodings of the other branches. The encoder resolves this circular dependency as follows:
1. Switch every branch to use the compact encoding.
2. Compute the offsets of all instructions, and find the compact branches whose displacements are now out of range because they don't fit in 1 byte.
3. If there are none, stop. Otherwise, change these branches to their long encodings and repeat from step 2.

This terminates after at most one round for each branch, and its result is the smallest possible encoding of the method (see normative Propositions S4 and S6 for a proof). Starting from long branches instead can create circular scenarios where multiple branches cannot become compact purely because the others are not compact.

### Side notes about shortening

- Shortening cannot fail unless literally no encoding of the method exists at all (for instance, if a branch targets an instruction that is out of bounds of the method body, or if the code exceeds 2 GiB even with every instruction other than a branch in its smallest encoding). See normative Proposition S7 for a proof.
- The result of shortening depends only on the canonical form, so regardless of which encodings the compiler selected, it shortens to the same code. See normative Proposition S8.
- An unchanged method cannot increase in size: if a method body is decoded, canonicalized, and shortened, all with no other changes, the result is no larger than the original. Each instruction starts at the same offset as in the original or at a smaller one, and each range of instructions occupies at most as many bytes as in the original. See normative Proposition S9.
