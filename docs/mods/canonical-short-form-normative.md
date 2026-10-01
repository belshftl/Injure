# mods/canonical-short-form-normative.md

This document provides a normative, formal definition of canonical/short forms and shortening.

**Most should refer to [`mods/canonical-short-form.md`](/docs/mods/canonical-short-form.md) instead**; it is an informative document explaining the subject in less formal terms, with examples.

---

In this document, *ECMA-335* denotes the 6th edition (June 2012) of the ECMA-335 standard.

All section references are to ECMA-335.

Statements labeled *Fact* are axiomatic claims. Their factual correctness is verified independently by inspection of ECMA-335, and, where a Fact makes a claim about this document, by inspection of this document.

Statements labeled *Proposition* follow from the definitions and Facts, and this document contains their proofs. This document also proves some claims inline rather than as numbered Propositions. Some proof steps check a finite property of the definitions, tables, or lists of this document; such a step states what it checks, in the form "by inspection of ...".

Statements labeled *Informative* are not normative.

Statements labeled *Behavioral* are informative (not normative) statements about ECMA-335 that relate this document's definitions to how ECMA-335 specifies the storage, execution, and validity of instructions. They are verified by inspection of ECMA-335. A claim about the storage, execution, or validity of instructions is a Behavioral statement rather than a Fact unless a definition or proof depends on it.

## Scope

This document defines encodings, instructions, the layout and encodability of instruction sequences, the canonical form of instructions and instruction sequences, and the short form of instruction sequences and of instructions other than branches. It does not define operand serialization, exception handling clauses, method headers, or debug information.

(Informative: informative notes use "this framework" for the IL-patching framework whose documentation this document is part of, and name its components similarly: "the IR", "the encoder", etc.)

(Informative: the definitions of encodings, prefix encodings, prefixes, instructions, instruction sequences, and their layout define a model of the code of a method body. The model exists so that this document can define the canonical form, equivalence, the short form, shortening, and encodability, which this framework applies, and prove their properties from Facts that are checked by inspection of ECMA-335.)

(Informative: canonicalization and shortening can change offsets, and they do not remap offset-based data outside the instruction stream. After canonicalization, small-format exception handling clauses (16-bit offsets, 8-bit lengths) can overflow, and a tiny method header can become insufficient. By S9, an encodable instruction sequence and its canonical form have the same short form, and no offset or span size of that short form exceeds the corresponding value of the original sequence. An instruction sequence $`I_0, \dots, I_{n-1}`$ decoded from the code of a method body is encodable. Decoding succeeds only if the target of each control transfer that an instruction $`I_i`$ of a target kind encodes is at byte offset $`o_j`$ for some $`0 \le j < n`$, so every position operand is in bounds. By B2 and B3, $`I_i`$ stores the displacement $`o_j - o_{i+1}`$ as a value of a placeholder whose value set is the displacement range of the encoding of $`I_i`$, so every displacement is in range. $`o_n`$ is the code size that the method header specifies, which is at most $`2^{32} - 1`$. Therefore, if a method body is decoded, canonicalized, and shortened with no other changes, and each start or end offset that its exception handling clauses specify is $`o_a`$ for the same $`a`$ before and after, the formats of its original method header and exception handling clauses remain sufficient. The IR does not use such size-bounded formats, and the encoder shortens the instruction sequence before it writes the method header and exception handling clauses, so it can select their formats from the final offsets.)

## Encodings

A *variant table* is a table in §III.2, §III.3, or §III.4 that has exactly three columns, labeled, in order, "Format", "Assembly Format" or "Instruction", and "Description". The first column of a variant table is its *Format column*, and the second is its *Assembly Format column*. A variant table in §III.2 is a *prefix table*. A variant table in §III.3 or §III.4 is an *instruction table*.

(Informative: the second column is labeled "Instruction" only in §III.3.3 (`and`), §III.3.30 (`cpblk`), and §III.3.53 (`or`).)

This document writes each byte as two hexadecimal digits, with uppercase letters and without a prefix: for example, byte `45` has the value 0x45 (decimal 69).

**Fact F1.** Format column entries write each byte as this document does. Each entry of the Format column of a variant table, other than entries that begin with byte `45`, consists of one or more bytes followed by zero or one placeholder in angle brackets, for example `FE 0E <unsigned int16>`.

The *opcode bytes* of a Format column entry are the bytes that it writes outside placeholders, and its *opcode byte sequence* is the sequence of its opcode bytes, in order. By F1, a Format column entry that does not begin with byte `45` begins with its opcode bytes.

**Fact F2.** Among all variant tables, exactly one Format column entry begins with byte `45`. It is in an instruction table, and it is `45 <unsigned int32> <int32>… <int32>`.

The only opcode byte of this entry is `45`, so its opcode byte sequence is `45`.

An *encoding* is the opcode byte sequence of an entry of the Format column of an instruction table. A *prefix encoding* is the opcode byte sequence of an entry of the Format column of a prefix table. The *Format column entries* of an encoding or prefix encoding are the Format column entries whose opcode byte sequence it is.

The *name* of a Format column entry is the first whitespace-delimited word of the Assembly Format column entry in the same row, converted to lowercase, for example `ldarg.s` for `ldarg.s num`. This document names an encoding or prefix encoding by the name of its Format column entries, for example `switch` for the encoding `45`. If its Format column entries have distinct names, F8 and the naming rule that follows it select one. [*Operand kind and domain*](#operand-kind-and-domain), [*Instructions*](#instructions), and [*Layout*](#layout) define the domain, the operand, and the size of `switch`.

**Fact F3.** For each opcode byte sequence other than `45`, either all entries with that sequence have no placeholder, or all have the same placeholder.

**Fact F4.** Each encoding appears in exactly one instruction table.

**Fact F5.** No opcode byte sequence is both an encoding and a prefix encoding.

**Fact F6.** No encoding or prefix encoding is a proper initial segment of another encoding or prefix encoding.

(Informative: F6 is never used by proofs in this document. F5 and F6 are useful properties for a decoder to rely on.)

An encoding other than `switch`, or a prefix encoding, *has placeholder* $`t`$ iff its Format column entries end with $`t`$, and *has no placeholder* iff its Format column entries contain no placeholder. This is well-defined. By F2, the only Format column entry that begins with byte `45` is in an instruction table and has opcode byte sequence `45`. By F1, every other Format column entry begins with its opcode bytes, so its opcode byte sequence does not begin with byte `45`. Therefore `45` is not a prefix encoding, F1 describes every Format column entry of an encoding other than `switch` and of a prefix encoding, and F3 applies to their opcode byte sequences. (Informative: B2 states where the code of a method body stores the value of a placeholder.)

Each placeholder $`t`$ has a *width* and a *value set* $`V(t)`$, given by the following *placeholder table*:

| Placeholder        | Width (bytes) | $`V(t)`$                    |
| ------------------ | ------------- | --------------------------- |
| `<int8>`           | 1             | $`[{-2^{7}}, 2^{7} - 1]`$   |
| `<unsigned int8>`  | 1             | $`[0, 2^{8} - 1]`$          |
| `<int16>`          | 2             | $`[{-2^{15}}, 2^{15} - 1]`$ |
| `<unsigned int16>` | 2             | $`[0, 2^{16} - 1]`$         |
| `<int32>`          | 4             | $`[{-2^{31}}, 2^{31} - 1]`$ |
| `<unsigned int32>` | 4             | $`[0, 2^{32} - 1]`$         |
| `<int64>`          | 8             | $`[{-2^{63}}, 2^{63} - 1]`$ |
| `<float32>`        | 4             | $`[0, 2^{32} - 1]`$         |
| `<float64>`        | 8             | $`[0, 2^{64} - 1]`$         |
| `<T>`              | 4             | $`[0, 2^{32} - 1]`$         |

All of the above intervals are integer intervals. (Informative: `<T>` above denotes a metadata token.)

**Fact F7.** Every placeholder of every encoding and prefix encoding is listed in the placeholder table.

A value of `<float32>` or `<float64>` is an IEEE 754 binary32 or binary64 bit pattern, respectively, read as an unsigned integer. Values compare by bit pattern rather than IEEE 754 equality: for example, `+0.0` and `-0.0` are distinct values, and NaNs with distinct payloads are distinct values. A value of `<T>` is the token read as an unsigned integer.

Two names are *aliases* iff they are distinct names of Format column entries with the same opcode byte sequence. For an opcode byte sequence whose Format column entries have at least two distinct names, the set of these names is an *alias group*.

(Informative: ECMA-335 prints some first words with an initial capital, for example `Ceq` in §III.3.21 and `Or` in §III.3.53, and prints both `ldc.i4.m1` and `ldc.i4.M1` for the encoding `15` in §III.3.40. After conversion to lowercase, both entries for `15` have the name `ldc.i4.m1`, so they are not aliases.)

**Fact F8.** The alias groups are:
- `brfalse`, `brnull`, `brzero`
- `brfalse.s`, `brnull.s`, `brzero.s`
- `brtrue`, `brinst`
- `brtrue.s`, `brinst.s`
- `endfinally`, `endfault`
- `ldind.i8`, `ldind.u8`
- `ldelem.i8`, `ldelem.u8`

This document names each aliased encoding by the first name in its group in F8. For an encoding `X`, the *`X` table* is the instruction table that contains `X`. By F4, this is well-defined.

A name *refers to* an encoding or prefix encoding iff it is the name of a Format column entry of that encoding or prefix encoding.

**Fact F9.** No two distinct encodings or prefix encodings share a name (including aliases). Every name that this document uses to refer to an encoding, directly or through a pattern (for example `ldarg.N` or `X.s`), is the name of an encoding, and every name that it uses to refer to a prefix encoding is the name of a prefix encoding.

**Proposition K1.** Each name that this document uses to refer to an encoding or prefix encoding refers to exactly one encoding or prefix encoding. Two distinct names refer to a common encoding or prefix encoding iff they are in the same alias group. In particular, no other name refers to the encoding or prefix encoding that a name in no alias group refers to.

Proof: by F9, a name that this document uses to refer to an encoding or prefix encoding is the name of one, so it refers to at least one, and no two distinct encodings or prefix encodings share a name, so it refers to at most one. Two distinct names that refer to a common encoding or prefix encoding are names of Format column entries with that opcode byte sequence, so they are in its alias group. Two distinct names in an alias group are names of Format column entries with the opcode byte sequence of that group, so both refer to it. The last statement follows from the second.

The *size* of an encoding other than `switch`, or of a prefix encoding, is its number of opcode bytes plus the width of its placeholder, if any. By F7, every placeholder of an encoding or prefix encoding has a width and a value set, which this definition and the definitions of domains below use.

## Operand kind and domain

Every encoding $`E`$ has a *domain* $`D(E)`$, the set of values that $`E`$ can represent:
- `switch`: the finite sequences over $`V(\texttt{<int32>})`$ of length at most $`2^{32} - 1`$.
- `ldarg.N`, `ldloc.N`, `stloc.N` where $`0 \le N \le 3`$, and `ldc.i4.N` where $`0 \le N \le 8`$: $`\{N\}`$.
- `ldc.i4.m1`: $`\{{-1}\}`$.
- Any other encoding with no placeholder: $`\{\varepsilon\}`$, where $`\varepsilon`$ is a distinguished value that is neither an integer nor a sequence.
- Any other encoding with placeholder $`t`$: $`V(t)`$.

The encodings that the second and third items name are the *fixed-value encodings*. By F8, neither `switch` nor any name in the second and third items is in an alias group, so by K1, these names refer to pairwise distinct encodings, and $`D`$ is well-defined.

Every encoding $`E`$ has an *operand kind* $`K(E)`$, defined by the first of the following rules that matches $`E`$:
1. `switch`: target list.
2. The encodings in the `br`, `brfalse`, `brtrue`, `beq`, `bge`, `bge.un`, `bgt`, `bgt.un`, `ble`, `ble.un`, `blt`, `blt.un`, `bne.un`, and `leave` tables (the *branch tables*): branch target.
3. `ldarg`, `ldarg.s`, `ldarg.0` to `ldarg.3`, `ldarga`, `ldarga.s`, `starg`, `starg.s`: argument index.
4. `ldloc`, `ldloc.s`, `ldloc.0` to `ldloc.3`, `ldloca`, `ldloca.s`, `stloc`, `stloc.s`, `stloc.0` to `stloc.3`: local index.
5. `ldc.i4`, `ldc.i4.s`, `ldc.i4.m1`, `ldc.i4.0` to `ldc.i4.8`: int32. `ldc.i8`: int64. `ldc.r4`: float32. `ldc.r8`: float64.
6. An encoding with placeholder `<T>`: token.
7. Any other encoding: none.

*Branch table `X`* denotes the `X` table, where `X` is one of the names in rule 2.

**Fact F10.** Each branch table `X` contains exactly two encodings: `X`, with placeholder `<int32>`, and `X.s`, with placeholder `<int8>`. Each has exactly one opcode byte.

In math notation, $`X`$ denotes `X` and $`X^{\mathrm{s}}`$ denotes `X.s`. By F10, the size of $`X`$ is 5 and the size of $`X^{\mathrm{s}}`$ is 2.

**Fact F11.** No encoding that matches rule 7 has a placeholder.

**Fact F12.** Every encoding named in rules 3 to 5, other than the fixed-value encodings, has a placeholder.

**Proposition K2.** $`K(E)`$ = none iff $`D(E) = \{\varepsilon\}`$.

Proof: if $`K(E)`$ = none, then $`E`$ matches none of rules 1 to 6. Therefore $`E`$ is not `switch`, and is none of the encodings named in rules 3 to 5, which, by inspection of rules 3 to 5, include every fixed-value encoding. By F11, $`E`$ has no placeholder, so $`D(E) = \{\varepsilon\}`$ by the definition of $`D`$. Conversely, if $`D(E) = \{\varepsilon\}`$, then $`E`$ is not `switch`, has no placeholder, and is not a fixed-value encoding: $`\varepsilon`$ is neither an integer nor a sequence, so the fourth item of the definition of $`D`$ is the only item that yields $`\{\varepsilon\}`$. By F10, every encoding in a branch table has a placeholder. By F12, every encoding named in rules 3 to 5 has a placeholder or is a fixed-value encoding. Every encoding that matches rule 6 has a placeholder. Therefore $`E`$ matches none of rules 1 to 6, so $`K(E)`$ = none.

(Informative: K2 is never used by proofs in this document, and exists as a consistency check. F11 and F12 are used only by the proof of K2.)

**Proposition K3.** $`K(E)`$ = branch target iff $`E`$ is in a branch table.

Proof: if $`E`$ is in a branch table `X`, then by F10, $`E`$ is $`X`$ or $`X^{\mathrm{s}}`$. By inspection of rule 2, neither `X` nor `X.s` is the name `switch`. By F8, `switch` is in no alias group, so by K1, neither `X` nor `X.s` refers to `switch`. Therefore rule 1 does not match $`E`$, and rule 2 does, so $`K(E)`$ = branch target. Conversely, if $`K(E)`$ = branch target, then rule 2 is the first rule that matches $`E`$, since it is the only rule that assigns branch target, and rule 2 matches only encodings in branch tables.

Branch target and target list are the *target kinds*. An encoding is *of kind* branch target, target list, int32, and so on, iff its operand kind is that kind, and *of a target kind* iff its operand kind is a target kind. For an encoding $`E`$ of a target kind, $`D(E)`$ is not the set of operands: [*Instructions*](#instructions) defines the operand of an instruction of a target kind as a position or a sequence of positions, and [*Layout*](#layout) defines the displacements that the positions determine. For any other encoding $`E`$, the definition of instructions in [*Instructions*](#instructions) makes $`D(E)`$ the set of operands of instructions with encoding $`E`$.

The *displacement range* of an encoding $`E`$ of kind branch target is $`D(E)`$. The displacement range of `switch` is $`V(\texttt{<int32>})`$, the value set of each element of a sequence in $`D(\texttt{switch})`$.

**Proposition K4.** For each branch table `X`, the displacement range of $`X`$ is $`V(\texttt{<int32>})`$, and the displacement range of $`X^{\mathrm{s}}`$ is $`V(\texttt{<int8>})`$. Every displacement range is an integer interval that contains 0.

Proof: by F10, $`X`$ and $`X^{\mathrm{s}}`$ are in branch table `X`, with placeholders `<int32>` and `<int8>`, respectively, so by K3, both are of kind branch target, and their displacement ranges are their domains. Neither is `switch`, since rule 1 assigns target list to `switch`. By inspection of rule 2 and the definition of $`D`$, neither `X` nor `X.s` is a name in the second or third item of that definition. By F8, no name in those items is in an alias group, so by K1, neither $`X`$ nor $`X^{\mathrm{s}}`$ is a fixed-value encoding. Therefore, by the last item of the definition of $`D`$, $`D(X) = V(\texttt{<int32>})`$ and $`D(X^{\mathrm{s}}) = V(\texttt{<int8>})`$. By K3 and F10, every encoding of kind branch target is $`X`$ or $`X^{\mathrm{s}}`$ for a branch table `X`, and by definition, the displacement range of `switch` is $`V(\texttt{<int32>})`$, so every displacement range is $`V(\texttt{<int32>})`$ or $`V(\texttt{<int8>})`$. By the placeholder table, both are integer intervals that contain 0.

(Informative: by B2 and B3, the value that an encoding of kind branch target stores, and each `<int32>` value that `switch` stores, is a displacement; the encoder stores displacements accordingly.)

Each prefix encoding $`P`$ has a *domain* $`D(P)`$: $`\{\varepsilon\}`$ if $`P`$ has no placeholder, and $`V(t)`$ if $`P`$ has placeholder $`t`$. By F5, no prefix encoding is an encoding, so this definition does not conflict with the definition of the domain of an encoding.

(Informative: domains describe representability, not ECMA-335 validity; for example, $`D(\texttt{unaligned.})`$ contains values other than 1, 2, and 4 (§III.2.5), and $`D(\texttt{ldloc})`$ contains 65535 (§III.3.43). Domains do not bound argument or local indices, and do not restrict the table of a token.)

## Encoding classes

The following table defines the *encoding classes*. Each row defines a class, which consists of the *canonical encoding* in the first column and its *compact encodings* in the second. Every encoding not listed forms a class of one, whose only member is its canonical encoding. By F4, F8, F10, K1, and inspection of the table below, the classes partition the set of all encodings. An encoding is *canonical* iff it is the canonical encoding of its class.

| Canonical encoding            | Compact encodings                                 |
| ----------------------------- | ------------------------------------------------- |
| `ldarg`                       | `ldarg.s`, `ldarg.0` to `ldarg.3`                 |
| `ldarga`                      | `ldarga.s`                                        |
| `starg`                       | `starg.s`                                         |
| `ldloc`                       | `ldloc.s`, `ldloc.0` to `ldloc.3`                 |
| `ldloca`                      | `ldloca.s`                                        |
| `stloc`                       | `stloc.s`, `stloc.0` to `stloc.3`                 |
| `ldc.i4`                      | `ldc.i4.s`, `ldc.i4.m1`, `ldc.i4.0` to `ldc.i4.8` |
| `X` for each branch table `X` | `X.s`                                             |

**Proposition K5.** Each compact encoding $`E`$ of a canonical encoding $`C`$ satisfies $`K(E) = K(C)`$.

Proof: $`E`$ and $`C`$ are listed in the class table. If $`C`$ is $`X`$ for a branch table `X`, then by inspection of the class table, $`E`$ is $`X^{\mathrm{s}}`$. By F10, both are in branch table `X`, so by K3, $`K(E) = K(C)`$ = branch target. Otherwise, $`C`$ is in no branch table: by F10, each branch table `Y` contains only $`Y`$ and $`Y^{\mathrm{s}}`$. $`C`$ is not $`Y`$ by the case assumption. $`C`$ is not $`Y^{\mathrm{s}}`$, since $`Y^{\mathrm{s}}`$ is in the class of $`Y`$, whose canonical encoding is $`Y \ne Y^{\mathrm{s}}`$, while $`C`$ is the canonical encoding of its class, and the classes partition the set of all encodings. $`E`$ is in no branch table either: if $`E`$ were in a branch table `Y`, then by F10 and the class table, $`E`$ would be in the class of $`Y`$. The classes partition the set of all encodings, so the class of $`C`$, which contains $`E`$, would be the class of $`Y`$, which is $`\{Y, Y^{\mathrm{s}}\}`$ and does not contain $`C`$. Therefore rule 2 matches neither. By inspection of the class table, the name `switch` does not appear in it. By F8, `switch` is in no alias group, so by K1, no name in the class table refers to `switch`. Therefore neither $`E`$ nor $`C`$ is `switch`, and rule 1 matches neither. By K1 and inspection of the class table and rules 3 to 5, $`E`$ and $`C`$ are both named in rule 3, both named in rule 4, or both named in the int32 clause of rule 5. By F8, no name in rules 3 to 5 is in an alias group, so by K1, distinct names in rules 3 to 5 refer to distinct encodings. Therefore no rule among rules 3 to 5 other than the rule that names $`E`$ and $`C`$ matches $`E`$ or $`C`$, and if that rule is rule 5, no clause of rule 5 other than the int32 clause names $`E`$ or $`C`$. The rule that names $`E`$ and $`C`$ is therefore the first rule that matches each, and it assigns both the same operand kind.

**Fact F13.** Each compact encoding $`E`$ of a canonical encoding $`C`$ satisfies $`D(E) \subsetneq D(C)`$, and the size of $`E`$ is less than the size of $`C`$.

**Fact F14.** Distinct encodings of equal size in the same class have disjoint domains.

## Instructions

A *prefix* is a pair $`(P, w)`$ of a prefix encoding $`P`$ and a value $`w \in D(P)`$.

An *instruction* is a triple $`(p, E, v)`$ of a finite sequence $`p`$ of prefixes, an encoding $`E`$, and an *operand* $`v`$, where:
- if $`K(E)`$ = branch target, $`v`$ is a *position*, i.e. a non-negative integer;
- if $`K(E)`$ = target list, $`v`$ is a sequence of positions of length at most $`2^{32} - 1`$;
- otherwise, $`v \in D(E)`$.

(Informative: this document does not restrict which prefixes may precede an encoding, or in what order. §III.2 specifies such restrictions, and they are out of the scope of this document.)

The *operand kind* of an instruction $`(p, E, v)`$ is $`K(E)`$. An instruction is of a given kind, or of a target kind, iff its encoding is. A *branch* is an instruction of kind branch target. For a branch table `X`, a branch $`(p, X, v)`$ is a *canonical branch*, and a branch $`(p, X^{\mathrm{s}}, v)`$ is a *compact branch*.

(Informative: *branch* in this document includes `leave` and `leave.s`, and excludes `switch`. ECMA-335 counts `switch` as a branch (§I.12.4.2.8.2.7), and treats `leave` and `leave.s` as a separate category (§I.12.4.2.8.2.8).)

The *position operands* of $`(p, E, v)`$ are $`v`$ if $`K(E)`$ = branch target, the elements of $`v`$ if $`K(E)`$ = target list, and none otherwise.

An *instruction sequence* is a finite sequence $`I_0, \dots, I_{n-1}`$ of instructions. The position of $`I_i`$ is $`i`$. A position operand $`j`$ is *in bounds* iff $`j < n`$. A position operand $`j`$ that is in bounds *targets* $`I_j`$.

(Informative: a position identifies a particular slot in the instruction sequence, not an instruction value; equal instructions at different positions are distinct targets. A position operand targets an instruction including its prefixes, which matches the IR's representation of prefixes as attributes of the instructions that follow them.)

(Informative: a position operand cannot express a control transfer to the encoding of an instruction that has prefixes, or to a prefix of an instruction other than its first prefix. Code of a method body that contains such a control transfer has no representation as an instruction sequence.)

**Behavioral B1.** Let $`E`$ be a compact encoding of a canonical encoding $`C`$, and let $`p`$ be a finite sequence of prefixes. ECMA-335 permits $`p`$ before $`E`$ iff it permits $`p`$ before $`C`$. If $`E`$ is not of kind branch target, then for each $`u \in D(E)`$, ECMA-335 specifies the same stack transition, effect, exceptions, correctness, and verifiability for $`E`$ representing $`u`$ preceded by $`p`$ as for $`C`$ representing $`u`$ preceded by $`p`$. B4 states the corresponding property for encodings of kind branch target.

## Layout

The *size* of a prefix $`(P, w)`$ is the size of $`P`$. The *size* of an instruction $`(p, E, v)`$ is the sum of the sizes of the elements of $`p`$ plus $`s`$, where $`s = 5 + 4m`$ if $`E`$ is `switch` and $`v`$ has $`m`$ elements, and $`s`$ is the size of $`E`$ otherwise. $`\mathrm{size}(x)`$ denotes the size of $`x`$. F1 describes every Format column entry of an encoding other than `switch` (see [*Encodings*](#encodings)), so every encoding other than `switch` has at least one opcode byte. Since $`5 + 4m \ge 5`$ as well, every instruction has a size of at least 1.

In an instruction sequence $`I_0, \dots, I_{n-1}`$, the *offset* $`o_i`$ for $`0 \le i \le n`$ is the sum of the sizes of $`I_0, \dots, I_{i-1}`$. The *size* of the instruction sequence is $`o_n`$. For $`0 \le a \le b \le n`$, the *size of the span* from $`a`$ to $`b`$ is $`o_b - o_a`$.

**Behavioral B2.** ECMA-335 specifies that the code of a method body contains its instructions in order, with no bytes between them; that a prefix consists of the opcode bytes of its prefix encoding, followed by the value that the prefix encoding stores; that an instruction consists of its prefixes in order, followed by the opcode bytes of its encoding, followed by the values that its encoding stores; that a prefix encoding, or an encoding other than `switch`, stores one value that occupies the width of $`t`$ in bytes if it has placeholder $`t`$, and no value otherwise; and that `switch` stores a count $`m`$ as an `<unsigned int32>`, followed by $`m`$ `<int32>` values.

(Informative: by B2, when an instruction sequence is stored as the code of a method body, the code occupies $`o_n`$ bytes, and $`I_i`$ starts at byte offset $`o_i`$ from the start of the code, at its first prefix if it has prefixes.)

For $`0 \le i < n`$ and $`0 \le j < n`$, the *displacement* of position $`j`$ from position $`i`$ is $`o_j - o_{i+1}`$. A displacement $`d`$ is *forward* iff $`d \ge 0`$, and *backward* iff $`d < 0`$.

**Behavioral B3.** ECMA-335 specifies the value that an encoding of kind branch target stores, and each `<int32>` value that `switch` stores, as the signed byte offset of the target of a control transfer from the first byte after the instruction.

(Informative: by B2 and B3, a value $`d`$ in $`I_i`$ transfers control to byte offset $`o_{i+1} + d`$.)

**Behavioral B4.** For an encoding of kind branch target, and for `switch`, ECMA-335 specifies the stack transition, effect, exceptions, correctness, and verifiability as depending on each value that B3 describes only through the byte offset of the target that B3 derives from it. For each branch table `X` and each finite sequence $`p`$ of prefixes, ECMA-335 specifies these for $`X^{\mathrm{s}}`$ preceded by $`p`$ as the same function of the byte offset of the target as for $`X`$ preceded by $`p`$.

**Proposition L1.** Let $`0 \le i, j < n`$. If $`j > i`$, the displacement of $`j`$ from $`i`$ is $`\sum_{k=i+1}^{j-1} \mathrm{size}(I_k)`$ and is forward. If $`j \le i`$, it is $`-\sum_{k=j}^{i} \mathrm{size}(I_k)`$ and is backward.

Proof: follows from the definition of offsets. The first sum is non-negative, as every size is at least 1; it is empty if $`j = i + 1`$. The second sum is positive, as it contains the term $`\mathrm{size}(I_i)`$, which is at least 1.

**Proposition L2.** Let $`G`$ and $`H`$ be instruction sequences of length $`n`$ such that $`\mathrm{size}(G_k) \le \mathrm{size}(H_k)`$ for $`0 \le k < n`$. For $`0 \le i, j < n`$, let $`d_G`$ and $`d_H`$ be the displacements of $`j`$ from $`i`$ in $`G`$ and $`H`$. Then $`d_G`$ and $`d_H`$ are both forward or both backward, and $`\left|d_G\right| \le \left|d_H\right|`$.

Proof: by L1, whether a displacement is forward depends only on $`i`$ and $`j`$, and each absolute value is a sum over the same set of positions. By hypothesis, each term of the sum for $`G`$ is at most the corresponding term of the sum for $`H`$.

**Proposition L3.** Let $`W`$ be an integer interval that contains 0, and let $`d`$ and $`d'`$ be displacements that are both forward or both backward with $`\left|d\right| \le \left|d'\right|`$. If $`d' \in W`$, then $`d \in W`$.

Proof: $`d`$ lies between 0 and $`d'`$, both of which are in $`W`$.

Let $`I_i`$ be an instruction of a target kind whose position operands are in bounds. The *displacements* of $`I_i`$ are the displacements of its position operands from $`i`$. A displacement of $`I_i`$ is *in range* iff it is in the displacement range of the encoding of $`I_i`$.

**Proposition L4.** Let $`G`$ and $`H`$ be instruction sequences of length $`n`$ such that $`\mathrm{size}(G_k) \le \mathrm{size}(H_k)`$ for $`0 \le k < n`$. If $`G_i`$ and $`H_i`$ have the same encoding, of a target kind, and the same position operands, all in bounds, and every displacement of $`H_i`$ is in range, then every displacement of $`G_i`$ is in range.

Proof: the displacements of $`G_i`$ and $`H_i`$ are the displacements of the same position operands from $`i`$, in $`G`$ and in $`H`$. By L2, each displacement $`d_G`$ of $`G_i`$ and the corresponding displacement $`d_H`$ of $`H_i`$ are both forward or both backward, and $`\left|d_G\right| \le \left|d_H\right|`$. $`G_i`$ and $`H_i`$ have the same encoding, so the same displacement range, which by K4 is an integer interval that contains 0. $`d_H`$ is in this interval, so by L3, $`d_G`$ is in it.

An instruction sequence is *encodable* iff both of the following hold:
- every position operand is in bounds, and every displacement of every instruction of a target kind is in range;
- $`o_n \le 2^{32} - 1`$ (Informative: §II.25.4.3 specifies `CodeSize` in a fat method header as 4 bytes wide without specifying signedness; CoreCLR, the only runtime this framework supports, declares it as `DWORD CodeSize`, which is unsigned).

## Canonical form

An instruction $`(p, E, v)`$ is *canonical* iff $`E`$ is canonical. An instruction sequence is *canonical* iff all its instructions are canonical.

The *canonical form* of $`(p, E, v)`$ is $`(p, C, v)`$, where $`C`$ is the canonical encoding of the class of $`E`$. $`(p, C, v)`$ is an instruction:
- if $`E = C`$: trivial.
- if $`E \ne C`$: $`E`$ is a compact encoding of $`C`$, so by K5, $`K(C) = K(E)`$; if $`K(E)`$ is a target kind, the conditions on $`v`$ are the same for $`E`$ and $`C`$; otherwise, $`v \in D(E) \subseteq D(C)`$ by F13.

The canonical form of an instruction sequence is the sequence of the canonical forms of its instructions. To *canonicalize* an instruction or instruction sequence is to replace it with its canonical form. The encoding of the canonical form of an instruction is canonical, and a canonical instruction is its own canonical form. Therefore the canonical form of an instruction sequence is canonical and is its own canonical form.

Two instruction sequences are *equivalent* iff they have the same canonical form. Each class has exactly one canonical encoding, and the classes are disjoint, so two encodings have the same canonical encoding iff they are in the same class. Therefore two instruction sequences are equivalent iff they have the same length, and at each position have the same prefixes, the same operand, and encodings of the same class.

The *branch positions* of an instruction sequence are the positions of its branches, and its *`switch` positions* are the positions of its instructions with encoding `switch`. By K5, all encodings in a class have the same operand kind, so equivalent instruction sequences have the same operand kind at each position. A branch is an instruction of kind branch target, and by rule 1, an encoding is of kind target list iff it is `switch`. Therefore, equivalent instruction sequences have the same branch positions and the same `switch` positions.

(Informative: equivalence is syntactic. In this note, the *behavior* of an instruction sequence is the stack transition, effect, exceptions, correctness, and verifiability that ECMA-335 specifies for its instructions. By B1 to B4, two equivalent instruction sequences that are both encodable have the same behavior, provided that each start or end offset that their exception handling clauses specify, and each offset in any other offset-based data that affects behavior, is $`o_a`$ for the same $`a`$ in both, and that ECMA-335 specifies behavior as depending on a byte offset in the code only through the instruction that starts at that byte offset. The second condition is an assumption, not a Behavioral statement: B4 limits the dependence of a control transfer on a stored value to the byte offset of its target, and equivalent instruction sequences can place the same position at distinct byte offsets. The effect of `leave` and the constraints of §I.12.4.2.8 depend on the exception handling clauses. At each position, the sequences have the same prefixes, the same operand, and encodings of the same class, and each position operand transfers control to the instruction at the same position in both. For instructions other than branches, B1 applies. The stored displacements can differ between the sequences, but by B4, they affect behavior only through these targets, and a compact branch and a canonical branch of the same branch table have the same behavior for the same target. Constraints that ECMA-335 places on the order of instructions or on the direction of control transfers, for example in §III.1.7.5, hold for both sequences or for neither: the sequences have the same order, and by L1, the direction of each displacement depends only on the positions of the instruction and its target.)

Canonicalization preserves length, prefixes, and operands. By F13, it does not decrease any offset, but it can increase offsets.

**Proposition C1.** An instruction sequence that contains no compact branch, whose position operands are in bounds, and whose size is at most $`2^{31}`$ is encodable.

Proof: by K3 and F10, every branch is canonical or compact, so every branch in the sequence is canonical. Let $`d = o_j - o_{i+1}`$ be a displacement of an instruction $`I_i`$ of a target kind. If $`d \ge 0`$, then $`d \le o_j \le o_n - 1 \le 2^{31} - 1`$, where $`d \le o_j`$ holds since $`o_{i+1} \ge 0`$, and $`o_j \le o_n - 1`$ holds since $`j \le n - 1`$, offsets do not decrease with their index, and $`\mathrm{size}(I_{n-1}) \ge 1`$. If $`d < 0`$, then $`d \ge -o_{i+1} \ge -o_n \ge -2^{31}`$. Therefore $`d \in V(\texttt{<int32>})`$, which by K4 is the displacement range of `switch` and of the encoding $`X`$ of every canonical branch. In addition, $`o_n \le 2^{31} \le 2^{32} - 1`$.

In particular, if the position operands of an instruction sequence are in bounds and the size of its canonical form is at most $`2^{31}`$, its canonical form is encodable, since the canonical form has the same position operands and contains no compact branch.

## Short form

(Informative: ECMA-335 uses "short form" for the `.s` encodings, for example in §III.3.39 and §III.3.40. In this document, the *short form* of an instruction $`(p, E, v)`$ such that $`K(E)`$ is not a target kind is $`(p, M, v)`$, where $`M`$ is the encoding of least size in the class of $`E`$ whose domain contains $`v`$ (S1), and an instruction of kind target list is its own short form. The short form of an instruction sequence, if it exists, is the unique encodable instruction sequence of least size among the instruction sequences equivalent to it (S6), and S4 computes it. For example, the short form of `ldarg 2` is `ldarg.2`, and the short form of `ldarg 300` is `ldarg 300`.)

### Short form of an instruction

**Proposition S1.** Let $`(p, E, v)`$ be an instruction such that $`K(E)`$ is not a target kind. Among the encodings in the class of $`E`$ whose domain contains $`v`$, exactly one, $`M`$, has the least size, and every other has a greater size. $`(p, M, v)`$ is an instruction.

Proof: $`K(\texttt{switch})`$ is a target kind, so $`E`$ is not `switch`. `switch` forms a class of one, so the class of $`E`$ does not contain `switch`, and every encoding in it has a size. $`v \in D(E)`$ since $`K(E)`$ is not a target kind, so the set of encodings in the class whose domain contains $`v`$ is not empty. Sizes are non-negative integers, so the set has a least size. By F14, encodings of equal size in a class have disjoint domains, so exactly one encoding in this set has the least size, and every other encoding in this set has a greater size. By K5, all encodings in a class have the same operand kind, so $`K(M) = K(E)`$. With $`v \in D(M)`$, $`(p, M, v)`$ is an instruction.

For an instruction $`(p, E, v)`$ such that $`K(E)`$ is not a target kind, the *short form* is $`(p, M, v)`$ for the encoding $`M`$ of S1. An instruction of kind target list is its own short form. This document does not define the short form of a branch in isolation; the least-size encoding of a branch depends on the sizes of other instructions. See [*Short form of an instruction sequence*](#short-form-of-an-instruction-sequence).

### Short form of an instruction sequence

Let $`B`$ be an instruction sequence, and let $`B'`$ be $`B`$ with each branch replaced by its canonical form and each other instruction replaced by its short form. Whether an instruction is a branch depends only on the class of its encoding (K5), and the canonical form and the short form of an instruction depend only on its prefixes, the class of its encoding, and its operand, so $`B'`$ depends only on the canonical form of $`B`$.

Canonical forms and short forms preserve prefixes and operands, and they preserve operand kind (K5, S1). Therefore $`B`$ and $`B'`$ have the same operand kind at each position, and so, as in [*Canonical form*](#canonical-form), the same branch positions and the same `switch` positions. By K3, F10, and the class table, the canonical form of a branch $`(p, E, v)`$ is $`(p, X, v)`$ for the branch table `X` that contains $`E`$, so every branch of $`B'`$ has the form $`(p, X, v)`$ for a branch table `X`.

A *branch assignment* of $`B'`$ is an instruction sequence obtained from $`B'`$ by replacing zero or more of its branches $`(p, X, v)`$ with $`(p, X^{\mathrm{s}}, v)`$. $`X^{\mathrm{s}}`$ is in branch table `X`, so by K3, $`K(X^{\mathrm{s}})`$ = branch target, and $`v`$ is a position, so $`(p, X^{\mathrm{s}}, v)`$ is an instruction. This replacement preserves prefixes, operands, and operand kind, so every branch assignment of $`B'`$ has the prefixes, operands, operand kinds, branch positions, `switch` positions, and position operands of $`B`$. For a branch assignment $`A`$, $`Q(A)`$ is the set of positions of the canonical branches in $`A`$.

**Proposition S2.** The map $`A \mapsto Q(A)`$ is a bijection from the branch assignments of $`B'`$ to the subsets of the branch positions of $`B`$. Let $`G`$ and $`H`$ be branch assignments of $`B'`$ such that $`Q(G) \subseteq Q(H)`$. Then $`G`$ and $`H`$ are equal except at the positions $`k \in Q(H) \setminus Q(G)`$, where $`H_k = B'_k`$ and $`G_k = (p, X^{\mathrm{s}}, v)`$ for $`(p, X, v) = B'_k`$. Therefore $`G`$ and $`H`$ have the same position operands, $`\mathrm{size}(G_k) \le \mathrm{size}(H_k)`$ for every $`k`$, and $`\mathrm{size}(H) - \mathrm{size}(G) = 3 \cdot \left|Q(H) \setminus Q(G)\right|`$.

Proof: let $`A`$ be a branch assignment of $`B'`$. $`A`$ equals $`B'`$ at every position that is not a branch position of $`B`$. At a branch position $`k`$ of $`B`$, where $`B'_k = (p, X, v)`$, $`A_k`$ is either $`B'_k`$, which is a canonical branch, or $`(p, X^{\mathrm{s}}, v)`$, which is a compact branch. $`A`$ has branches only at the branch positions of $`B`$. Therefore $`Q(A)`$ is the set of branch positions $`k`$ at which $`A_k = B'_k`$, and $`A`$ is determined by $`Q(A)`$. Conversely, for each subset $`Z`$ of the branch positions of $`B`$, replacing the branches of $`B'`$ at the branch positions not in $`Z`$ yields a branch assignment $`A`$ with $`Q(A) = Z`$. This proves the first statement. If $`Q(G) \subseteq Q(H)`$, then at each branch position $`k`$, where $`B'_k = (p, X, v)`$, $`G_k`$ and $`H_k`$ are both $`B'_k`$ if $`k \in Q(G)`$, both $`(p, X^{\mathrm{s}}, v)`$ if $`k \notin Q(H)`$, and $`H_k = B'_k`$ and $`G_k = (p, X^{\mathrm{s}}, v)`$ if $`k \in Q(H) \setminus Q(G)`$. At every other position, both are $`B'_k`$. $`(p, X, v)`$ and $`(p, X^{\mathrm{s}}, v)`$ have the same prefixes and the same position operand, and by F10, the size of $`(p, X^{\mathrm{s}}, v)`$ is 3 less than the size of $`(p, X, v)`$.

The *short form* of $`B`$ is the encodable branch assignment $`S`$ of $`B'`$ such that $`Q(S) \subseteq Q(A)`$ for every encodable branch assignment $`A`$ of $`B'`$. If two branch assignments satisfy this, their $`Q`$ sets contain each other, so by S2, they are equal. $`B`$ *has a short form* iff such an $`S`$ exists. To *shorten* an instruction sequence is to replace it with its short form; shortening applies only to an instruction sequence that has a short form.

**Proposition S3.** Every branch assignment of $`B'`$ is equivalent to $`B`$. In particular, the short form of $`B`$, if it exists, is equivalent to $`B`$.

Proof: at each position, for the instruction $`(p_A, E_A, v_A)`$ of a branch assignment $`A`$ and the instruction $`(p_B, E_B, v_B)`$ of $`B`$, $`p_A = p_B`$ and $`v_A = v_B`$. At each branch position, each of $`E_A`$ and $`E_B`$ is $`X`$ or $`X^{\mathrm{s}}`$ for the same branch table `X`. At every other position, $`E_A`$ is the encoding of the short form of $`(p_B, E_B, v_B)`$, which is in the same class. Therefore $`A`$ and $`B`$ have the same canonical form.

**Proposition S4.** If a position operand of $`B`$ is not in bounds, no branch assignment of $`B'`$ is encodable, and $`B`$ has no short form. Otherwise, the following procedure terminates after at most $`b`$ iterations of step 2, where $`b`$ is the number of branches in $`B`$:
1. Let $`T`$ be the branch assignment of $`B'`$ in which every branch is compact.
2. While $`T`$ contains a compact branch with a displacement that is not in range, let $`U`$ be the set of positions of such branches in $`T`$, and replace the branches at one or more positions in $`U`$ with their canonical forms.

Let the position operands of $`B`$ be in bounds. If some branch assignment of $`B'`$ is encodable, then:
- after step 1 and after each iteration of step 2, $`o_n`$ of $`T`$ is at most $`2^{32} - 1`$, and every displacement of every canonical branch and every `switch` in $`T`$ is in range;
- the final $`T`$ is encodable and is the short form of $`B`$, regardless of which positions in $`U`$ each iteration of step 2 selects.

If no branch assignment of $`B'`$ is encodable, $`B`$ has no short form, and the final $`T`$ is not encodable. Therefore $`B`$ has a short form iff the final $`T`$ is encodable. In addition, if, after step 1 or after an iteration of step 2, $`o_n`$ of $`T`$ exceeds $`2^{32} - 1`$, or a canonical branch or a `switch` in $`T`$ has a displacement that is not in range, then $`B`$ has no short form.

(Informative: an implementation of the procedure can therefore report that $`B`$ has no short form at the first point at which $`o_n`$ of $`T`$ exceeds $`2^{32} - 1`$, or a canonical branch or a `switch` in $`T`$ has a displacement that is not in range, instead of continuing until step 2 terminates.)

Proof: every branch assignment of $`B'`$ has the position operands of $`B`$. If one of them is not in bounds, no branch assignment is encodable, so $`B`$ has no short form. Otherwise, the displacements in $`T`$ are defined.

$`B'`$ is the branch assignment of $`B'`$ that replaces no branch, so $`Q(B')`$ is the set of branch positions of $`B`$, and $`Q(T) \subseteq Q(B')`$. By S2, $`T`$ equals $`B'`$ except at the positions $`k \in Q(B') \setminus Q(T)`$, where $`T_k = (p, X^{\mathrm{s}}, v)`$ for $`(p, X, v) = B'_k`$; these are the positions of the compact branches of $`T`$. An iteration of step 2 replaces $`T_k`$ at some of these positions with its canonical form $`B'_k`$, so it yields the branch assignment of $`B'`$ whose $`Q`$ is $`Q(T)`$ together with the replaced positions. Therefore $`T`$ remains a branch assignment of $`B'`$ after each iteration, and each iteration adds at least one position to $`Q(T)`$, which is a subset of the branch positions of $`B`$, so the procedure terminates as stated.

If no branch assignment of $`B'`$ is encodable, $`B`$ has no short form, and the final $`T`$, a branch assignment of $`B'`$, is not encodable. Otherwise, let $`A`$ be an encodable branch assignment of $`B'`$. Invariant: $`Q(T) \subseteq Q(A)`$.

While the invariant holds, by S2, $`T`$ and $`A`$ are equal except at the positions $`k \in Q(A) \setminus Q(T)`$, where $`A_k = B'_k`$ and $`T_k = (p, X^{\mathrm{s}}, v)`$ for $`(p, X, v) = B'_k`$; $`\mathrm{size}(T_k) \le \mathrm{size}(A_k)`$ for every $`k`$; and $`T`$ and $`A`$ have the same position operands, all in bounds. Therefore, while the invariant holds:
- $`o_n`$ of $`T`$ is at most $`o_n`$ of $`A`$, which is at most $`2^{32} - 1`$;
- the position of every canonical branch and every `switch` in $`T`$ is not in $`Q(A) \setminus Q(T)`$, so $`A`$ has the same instruction at that position, with displacements in range since $`A`$ is encodable; by L4, the displacements in $`T`$ at that position are in range.

The invariant holds after step 1, as $`Q(T) = \emptyset`$. Suppose an iteration of step 2 replaces the compact branch $`T_i = (p, X^{\mathrm{s}}, v)`$. Its displacement in $`T`$ before the iteration is not in range. If $`i \notin Q(A)`$, then $`i \notin Q(A) \setminus Q(T)`$, so $`A`$ has the same instruction $`(p, X^{\mathrm{s}}, v)`$ at $`i`$, and its displacement in $`A`$ is in range since $`A`$ is encodable. By L4, the displacement in $`T`$ is then in range, which is a contradiction. Therefore $`i \in Q(A)`$ for each replaced branch, and the invariant holds after the iteration.

Therefore the invariant, and with it the two properties above, hold after step 1 and after each iteration of step 2, which proves the first item. On termination, in addition, every displacement of every compact branch in $`T`$ is in range by the loop condition, and the position operands of $`T`$ are in bounds, so the final $`T`$ is encodable and $`Q(T) \subseteq Q(A)`$. This holds for every encodable branch assignment $`A`$ of $`B'`$, so the final $`T`$ is the short form of $`B`$. The short form is unique, so it does not depend on the choices in step 2. This proves the second item.

The last two statements follow from the two cases: if some branch assignment of $`B'`$ is encodable, $`B`$ has a short form, the final $`T`$ is encodable, and by the first item, the conditions of the last statement never occur; otherwise, $`B`$ has no short form, and the final $`T`$ is not encodable.

**Proposition S5.** Let $`S`$ be the short form of $`B`$, and let $`A`$ be an encodable branch assignment of $`B'`$. Then $`\mathrm{size}(S_k) \le \mathrm{size}(A_k)`$ for every $`k`$, and $`\mathrm{size}(A) - \mathrm{size}(S) = 3 \cdot \left|Q(A) \setminus Q(S)\right|`$. This value is positive if $`A \ne S`$ and zero otherwise.

Proof: $`Q(S) \subseteq Q(A)`$ by the definition of the short form, so S2 gives the first two statements. If $`A \ne S`$, then $`Q(A) \ne Q(S)`$ by S2, so $`Q(A) \setminus Q(S)`$ is not empty, as $`Q(S) \subseteq Q(A)`$. If $`A = S`$, then $`Q(A) \setminus Q(S) = \emptyset`$.

**Proposition S6.** The short form of $`B`$ exists iff some encodable instruction sequence is equivalent to $`B`$. If $`R`$ is an encodable instruction sequence equivalent to $`B`$, then the short form $`S`$ of $`B`$ exists, and:
- $`\mathrm{size}(S_k) \le \mathrm{size}(R_k)`$ for every $`k`$, so every offset of $`S`$ and the size of every span of $`S`$ are at most the corresponding values of $`R`$;
- $`\mathrm{size}(S) \le \mathrm{size}(R)`$, with equality iff $`S = R`$.

In other words, $`S`$ is the unique encodable instruction sequence of least size among the instruction sequences equivalent to $`B`$.

Proof: if the short form $`S`$ exists, $`S`$ is encodable and, by S3, equivalent to $`B`$.

Let $`R`$ be an encodable instruction sequence equivalent to $`B`$, and let $`A`$ be $`R`$ with each instruction that is not a branch replaced by its short form. $`R`$ is equivalent to $`B`$, so $`R`$ has the branch positions and `switch` positions of $`B`$, and at each position $`k`$, $`R_k`$ and $`B_k`$ have the same prefixes, the same operand, and encodings of the same class. At each position $`k`$ that is not a branch position, the short form of $`R_k`$ is therefore the short form of $`B_k`$, which is $`B'_k`$. At each branch position $`k`$, let $`(p, X, v) = B'_k`$, the canonical form of $`B_k`$, for a branch table `X`. $`R_k`$, which has the prefixes and operand of $`B_k`$ and an encoding in the class of $`X`$, is $`(p, X, v)`$ or $`(p, X^{\mathrm{s}}, v)`$. Therefore, $`A`$ is a branch assignment of $`B'`$.

At each position $`k`$ that is neither a branch position nor a `switch` position, let $`R_k = (p, E, v)`$. $`K(E)`$ is not a target kind, so $`v \in D(E)`$, and $`E`$ is in the set of encodings that S1 considers for $`R_k`$. $`A_k`$ is $`(p, M, v)`$ for the encoding $`M`$ of S1, so by S1, $`\mathrm{size}(M) \le \mathrm{size}(E)`$, with equality iff $`M = E`$, and since $`M`$ is in the class of $`E`$, $`K(M) = K(E)`$ by K5. At each branch position, $`A_k = R_k`$ by construction, and at each `switch` position, $`A_k = R_k`$, since an instruction of kind target list is its own short form. Therefore $`\mathrm{size}(A_k) \le \mathrm{size}(R_k)`$ for every $`k`$, with equality iff $`A_k = R_k`$. $`A`$ is encodable:
- $`A`$ and $`R`$ differ only at positions that are neither branch positions nor `switch` positions, where $`K(E)`$ and $`K(M)`$ are not target kinds, so neither $`A`$ nor $`R`$ has position operands there. Therefore $`A`$ and $`R`$ have the same position operands, so they are in bounds;
- $`o_n`$ of $`A`$ is at most $`o_n`$ of $`R`$, which is at most $`2^{32} - 1`$;
- $`A`$ and $`R`$ have the same instruction at each branch position and each `switch` position, and $`R`$ has displacements in range there, so by L4, $`A`$ has displacements in range there.

The position operands of $`B`$ are those of $`A`$, which are in bounds, and $`A`$ is an encodable branch assignment of $`B'`$, so by S4, the short form $`S`$ exists. By S5, $`\mathrm{size}(S_k) \le \mathrm{size}(A_k) \le \mathrm{size}(R_k)`$ for every $`k`$, and $`\mathrm{size}(S) \le \mathrm{size}(A) \le \mathrm{size}(R)`$. If $`\mathrm{size}(S) = \mathrm{size}(R)`$, then $`\mathrm{size}(S) = \mathrm{size}(A)`$ and $`\mathrm{size}(A) = \mathrm{size}(R)`$. By S5, the first equality gives $`S = A`$. Each term of $`\mathrm{size}(A)`$ is at most the corresponding term of $`\mathrm{size}(R)`$, so the second equality gives $`\mathrm{size}(A_k) = \mathrm{size}(R_k)`$, and therefore $`A_k = R_k`$, for every $`k`$, so $`A = R`$. Therefore $`\mathrm{size}(S) = \mathrm{size}(R)`$ iff $`S = R`$.

**Proposition S7.** If $`B`$ is encodable, its short form $`S`$ exists, and $`\mathrm{size}(S) \le \mathrm{size}(B)`$, with equality iff $`S = B`$. If the canonical form of $`B`$ is encodable, or $`B'`$ is encodable, the short form of $`B`$ exists. $`\mathrm{size}(B')`$ is at most the size of the canonical form of $`B`$. The short form of $`B`$ exists if the position operands of $`B`$ are in bounds and $`\mathrm{size}(B') \le 2^{31}`$; in particular, it exists if the position operands of $`B`$ are in bounds and the size of the canonical form of $`B`$ is at most $`2^{31}`$.

Proof: $`B`$ is equivalent to itself. The canonical form of $`B`$ is its own canonical form, so it is equivalent to $`B`$. $`B'`$ is the branch assignment of $`B'`$ that replaces no branch, so by S3, it is equivalent to $`B`$. S6 applies to each of these that is encodable.

Let $`B_k = (p, E, v)`$, and let $`C`$ be the canonical encoding of the class of $`E`$. If $`B_k`$ is a branch, $`B'_k`$ is its canonical form $`(p, C, v)`$. If $`E`$ is `switch`, then $`B'_k = B_k`$, and $`C = E`$, since `switch` forms a class of one. Otherwise, $`K(E)`$ is not a target kind, so $`v \in D(E)`$, and $`v \in D(C)`$, by F13 if $`E \ne C`$. Therefore $`C`$ is in the set of encodings that S1 considers for $`B_k`$, and $`B'_k = (p, M, v)`$ with $`\mathrm{size}(M) \le \mathrm{size}(C)`$ by S1. In each case, $`\mathrm{size}(B'_k)`$ is at most the size of $`(p, C, v)`$, which is the canonical form of $`B_k`$. Summing over $`k`$ gives the size bound.

Every branch of $`B'`$ has the form $`(p, X, v)`$ for a branch table `X`, so $`B'`$ contains no compact branch, and $`B'`$ has the position operands of $`B`$. By C1, if these are in bounds and $`\mathrm{size}(B') \le 2^{31}`$, $`B'`$ is encodable. The last statement follows from the size bound.

**Proposition S8.** The short form of $`B`$ depends only on the canonical form of $`B`$. Equivalent instruction sequences have the same short form, or both have none. If $`S`$ is the short form of $`B`$, $`S`$ is the short form of $`S`$.

Proof: the short form of $`B`$ is defined by $`B'`$, which depends only on the canonical form of $`B`$. Equivalent instruction sequences have the same canonical form. By S3, $`S`$ is equivalent to $`B`$.

**Proposition S9.** Let $`B`$ be an encodable instruction sequence of length $`n`$, and let $`S`$ be its short form, which exists by S7. The canonical form of $`B`$ has the short form $`S`$. For $`0 \le a \le n`$, $`o_a`$ of $`S`$ is at most $`o_a`$ of $`B`$, and for $`0 \le a \le b \le n`$, the size of the span from $`a`$ to $`b`$ in $`S`$ is at most the size of the span from $`a`$ to $`b`$ in $`B`$.

Proof: the canonical form of $`B`$ is equivalent to $`B`$, so by S8, its short form is $`S`$. $`B`$ is an encodable instruction sequence equivalent to $`B`$, so S6 applies with $`R = B`$.
