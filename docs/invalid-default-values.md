# invalid-default-values.md

This document describes Injure's concept of a "default-is-invalid" struct, how they're marked, what happens when such values are used, related analyzer diagnostics, and guidance for making ones of your own.

---

## The `default` problem, and what an invalid default value is

C# has a particular quirk where every single type must have some kind of "zero"/sentinel value that the language can use in spots where there's something of that type that hasn't been assigned yet, such as array elements or unassigned fields. It's a pretty poor solution to the problem of unassigned values, but that doesn't change the fact we have to deal with it.

That value is `default`. For example, reference types, it's `null`, and that by itself was problematic enough that C# gained a language-level feature to track which reference types may be null, that being nullable annotations. Structs suffer from the exact same problem, but have no such luxury.

For structs, the `default` value is just a value of that struct with the all-zero bit pattern, which is usually meaningless for anything more complex than a blittable primitive. A `default(AssetId)` has a null namespace/path despite the constructor validating inputs to be non-null and as such doesn't name any asset, a `default(Texture2dSlice)` has its internal texture value set to null and as such can't be drawn, an enum-like type whose members start at 1+ has no member with value 0, etc.

Injure calls such a `default` value *invalid*, and such a struct *default-is-invalid*.

Some structs' `default` values happen to be perfectly valid, though: for example, `default(SrgbStraight32)` has all four of the R/G/B/A fields at 0, so the result is just the color `#00000000`, i.e. `SrgbStraight32.Transparent`. For another example, `default(Vector2Int)` has the X/Y fields at 0, which is just the origin point (0, 0). These don't take part in anything this document describes; their docs say what their `default` value represents.

## The solution

Just like nullable annotations remedy the problem via compile-time warnings, Injure remedies this with compile-time errors from a custom Roslyn analyzer.

A default-is-invalid struct implements `IHasInvalidDefault`:

```csharp
public readonly struct AssetId : /* ... */, IHasInvalidDefault {
    // ...
    bool IHasInvalidDefault.IsDefault => ns is null;
}
```

All default-is-invalid Injure types implement it via an explicit interface implementation, so it's only reachable through the interface, usually as a type constraint on a generic parameter (`where T : struct, IHasInvalidDefault`).

The `Injure.Analyzers` analyzer then prevents you from creating `default` values of that struct, reported as `IJ0100`:

```csharp
Asset<Texture2d> t = Assets.GetAsset<Texture2d>(default); // error IJ0100: The default value of 'AssetId' is invalid; create a valid value instead, or use 'AssetId?' if you need an optional value
```

with some exemptions, like equality checks (`x == default` and `x != default`), assigning it to an `out` parameter, and code inside the type itself.

If you're not aware, C# also forces every single struct to have a parameterless constructor, and generates one implicitly that produces `default` if you don't manually declare one. As such, `new T()` counts as creating the default value unless `T` has an explicitly declared parameterless constructor. It counts even with an object initializer, like `new T { X = 1 }`, since the analyzer can't tell which members determine `IsDefault`. For a struct with a constructor that takes parameters, this also catches initializers that bypass that constructor, and with it any field/property initializers, which only constructors run.

There are two more diagnostics. `IJ0101` reports `= default` optional parameters of default-is-invalid types:

```csharp
public void DoSomething(AssetId id = default) { // error IJ0101: Parameter 'id' defaults to the default value of 'AssetId', whose default value is invalid; make it required, or use 'AssetId?' with a default of null
    // ...
}
```

and, notably, `IJ0102` reports structs that contain default-is-invalid fields but don't themselves implement `IHasInvalidDefault`:

```csharp
public readonly struct Something {
	public readonly AssetId Id; // error IJ0102: 'Id' is a 'AssetId', whose default value is invalid, so the default value of 'Something' is invalid too; implement IHasInvalidDefault on 'Something'
}
```

If a default value is intentional, e.g. when clearing an array slot or filling the unused payload of a tagged union, suppress the diagnostic with `#pragma warning disable` and a comment saying why.

The analyzer isn't perfect; notably, it currently misses cases that would require flow tracking, like `new T[n]`, unassigned fields, etc.

## Making your own struct default-is-invalid

Implement `IHasInvalidDefault` on your struct; the `Injure.Analyzers` analyzer reports a class implementing it as `IJ0109`. It only has one member, `bool IsDefault { get; }`. Additionally:
1. Implement it through an explicit interface implementation rather than putting it directly onto the struct, i.e. `bool IHasInvalidDefault.IsDefault => /* ... */`.
2. Make sure `IsDefault` never throws.
3. Make sure `IsDefault` is cheap to call, since every argument check calls it.
4. Make every member that can't work on the default value throw `InvalidDefaultException.For<T>()`. In cases with a single or a handful of commonly-accessed fields, you can reduce boilerplate a lot by using private properties that do the check; for instance, for `private Foo data;` where `Foo` is a reference type, you can use `private Foo d => data ?? throw InvalidDefaultException.For<T>();`.
5. Make sure `Equals`, `GetHashCode`, `==`, `!=`, and `ToString` never throw. A common trap is the auto-generated `ToString` of `record struct`s, which for some reason lists every single property with a publicly accessible getter, including computed properties that throw on the default value.

If you're running into `IJ0100` false-positives with `new T { ... }` and e.g. `required` members: an explicitly declared parameterless constructor, i.e. `public T() {}`, exempts `new T()` and `new T { ... }` for that struct. Only declare one if the struct has `required` members, whose initializers the compiler forces every creation to set (the constructor must not have `[SetsRequiredMembers]`), or if the constructor itself makes the value valid.

When writing a public method that takes such a type, check the argument:

```csharp
public Asset<T> GetAsset<T>(AssetId id) where T : class {
	ArgumentInvalidDefaultException.ThrowIfDefault(id);
	// ...
}
```

`ThrowIfDefault` takes the parameter name from the argument expression through `[CallerArgumentExpression(...)]`. For verbatim identifiers (leading `@`), that expression has the `@`, so pass in the name explicitly like `ThrowIfDefault(@params, nameof(@params))`.

Methods Injure calls itself, such as overrides and callbacks, don't need to check the arguments Injure passes in.

## Conventions for Injure's default-is-invalid types

### Members of the type

Members of an `IHasInvalidDefault` type may throw `InvalidDefaultException` when used on the default value, without ever mentioning it in the member's docs.

The following members never throw on the default value:

- `IsDefault`;
- `Equals` and `GetHashCode`;
- `==` and `!=`;
- `ToString`.

### Parameters

A method that has a parameter of a default-is-invalid type throws `ArgumentInvalidDefaultException` if the passed argument is the default value. This isn't documented per member either; members only document it for cases where it's not obvious from the parameter type, e.g. when a field of a non-default argument is itself an invalid default value.

The check also extends to the following:

- A `T?` parameter where `T` is a default-is-invalid type: the argument may be `null`, but a non-null default value is rejected.
- A collection of `T` where `T` is a default-is-invalid type: every element is checked, and the exception message specifies the first found bad element, e.g. `gamepads[2]`. An iterator is checked during enumeration, since enumerating twice for the sake of the check would be unacceptable and `Reset()` may be unsupported on it, so a default element may be rejected after earlier elements were already processed.

Constructors and `init` accessors of plain data types (for example, records and parameter structs such as `CanvasParams`) may accept default values, with validity checks happening in APIs that take in the values.

`ArgumentInvalidDefaultException` derives from `ArgumentException`, so existing `catch (ArgumentException)` blocks catch it.

### Docs

Every single struct in Injure documents whether its `default` value is valid, as the last paragraph of its `<remarks>`, as one of the following (square brackets denote a placeholder):
- `/// The <see langword="default"/> value is invalid.[ anything]`
- `/// The <see langword="default"/> value is valid[anything other than just a period]`

This applies regardless of accessibility; even one-off private record structs get it.

A struct implements `IHasInvalidDefault` if and only if it documents its `default` value as invalid.
