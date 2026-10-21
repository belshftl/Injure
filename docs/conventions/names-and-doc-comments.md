# conventions/names-and-doc-comments.md

This document describes the naming and doc comment conventions used throughout Injure. They apply to all code in this repository; code that predates a convention may not follow it yet, and fixing that is tracked in `TODO.md`.

Name parts with a reserved meaning, such as `Ctx` or `Untyped`, are described in [`reserved-names.md`](reserved-names.md).

---

## Casing

Types and namespaces always use PascalCase.

Members use PascalCase if they are accessible from outside the declaring type, i.e. `public`, `internal`, `protected` or `protected internal`, and camelCase otherwise, i.e. `private` or `private protected`. This includes constants and `static readonly` fields:

```csharp
public sealed class Example {
	public const int MaxBufferedReloadFailures;
	public static readonly BlendState PremultipliedAlpha;
	public int InstructionCount { get; }
	public HostTick? EventAt;
	public void PushTop(Layer layer, TickerHandle ticker);

	private const string ownerId;
	private static readonly Regex whitespace;
	private ulong oldestSeq { get; }
	private int inFlight;
	private bool tryFindGamepad(GamepadId id, out int idx);

	internal static readonly HostDuration SpinThreshold;
	internal FontSourceKind SourceKind { get; }
	internal readonly ulong RegistryId;
	internal bool TryConsumeSignal();
}
```

Locals and parameters use camelCase.

The rationale is that the casing of a name tells you at a glance whether anything outside the type can depend on it.

## Abbreviations

Abbreviations and acronyms are ordinary words and follow the casing of their position, with no exceptions:

| Instead of       | Write            |
| ---------------- | ---------------- |
| `AssetID`        | `AssetId`        |
| `XMLParser`      | `XmlParser`      |
| `IOStream`       | `IoStream`       |
| `ILCursor`       | `IlCursor`       |
| `HResult`        | `Hresult`        |
| `WebGPUDevice`   | `WebgpuDevice`   |
| `Texture2D`      | `Texture2d`      |
| `nextBatchID`    | `nextBatchId`    |

Digits can act as either an uppercase or a lowercase letter, whichever reads naturally in context, e.g. `Texture2d`, `StateAxis2dBinding`, `E2eTestFixture`, `Expand24To32`. Single letters that are words on their own stay single uppercase letters, e.g. `XSocd`, `TrimEveryNOperations`. That also includes the `I` prefix for interfaces, e.g. `IIlTokenResolver`.

This intentionally deviates from the [.NET naming guidelines](https://learn.microsoft.com/dotnet/standard/design-guidelines/capitalization-conventions), which keep two-letter acronyms fully uppercase (`IOStream`) and only capitalize only the first letter for longer ones (`XmlParser`), and also special-case `Id` for some reason. The official guidelines make casing depend on the length of the acronym, which is both harder to apply consistently and ambiguous to read: `IOStream` could be the interface prefix + `OStream`, the interface prefix + `O` + `Stream`, `IO` + `Stream`, or a single proper-noun word `IOStream`.

Treating abbreviations as regular words removes the first three ambiguities: `IoStream` can only mean `Io` + `Stream`, and `IOStream` can only mean the interface prefix + `O` + `Stream`. The `IIlTokenResolver` example is also unambiguously the interface prefix + `Il` + `Token` + `Resolver`, or "interface for an IL token resolver".

camelCase/PascalCase proper nouns (JavaScript, FreeType, etc.), including ones whose name contains an abbreviation (OpenSSH, macOS, PostgreSQL, WebGPU, etc.) are single words, not several words split at word boundaries: `Javascript`, `Freetype`, `Openssh`, `Macos`, `Postgresql`, `Webgpu`. This clears the last aforementioned ambiguity; if "IOStream" was a proper noun, it'd be `Iostream` in a type/member name.

Names that come from foreign code, such as the WebGPU and SDL bindings, keep their original spelling (e.g. `WGPUTextureFormat`); only Injure's own names follow this convention. This also applies to names that match external definitions across an FFI boundary, such as `pub type HRESULT` / `pub struct GUID` in the CLR profiler for the modloader, even though by Rust convention and this project's convention it'd be `Hresult` / `Guid`, since COM uses `HRESULT`/`GUID`.

## Shortened names

A ubiquitous part of a name may be shortened if the shorter form is unambiguous and reading or typing the long form every time costs more than it gains. This applies to type and member names alike, when either:

- the abbreviation is itself ubiquitous (example: `HostDuration.FromMs`, where "ms" for "milliseconds" in a time-related context is very common); or
- the type or member is common enough that the ergonomic win is worth it (example: `BoundedCt<L>` instead of `BoundedCancellationToken<L>`; the type is standard and appears commonly in signatures, and would clutter them if it was that long).

The rationale, which holds for members just as much as types, is that a name rarely tells you everything you need to know, so you'll read its docs anyways, but only once; after that, you read and type the name every time you use it. Rust's `Arc<T>` is a good example: `Arc` says almost nothing about what the type does, but even if it was named `AtomicallyRefCounted<T>`, you'd still have to read the docs to learn what it is: a smart pointer that updates a shared reference count on clone and drop, provides no interior mutability, is `Send + Sync` if `T` is, may hold an unsized type, and so on. After you have learned that, there's no more use in a verbose name, and a short one saves effort on every single use. The problem is not so much that reading three words is difficult, but that code becomes cluttered and lower in information density.

This does not apply to rarely used names, where the reader is likely to have forgotten what the abbreviation means.

## Rounding

Rounding to an integer, or to a multiple of some step, explicitly specifies the rounding direction or midpoint behavior. Docs and code comments never use "round up", "round down", or "round to the nearest X" without the midpoint behavior, even if the inputs are always non-negative and "up"/"down" would happen to be unambiguous.

Rounding in a direction uses one of these phrases:
- "towards positive infinity" (`Math.Ceiling`);
- "towards negative infinity" (`Math.Floor`);
- "towards zero" (`Math.Truncate`, and integer division);
- "away from zero".

Rounding to the nearest value states the midpoint behavior, using one of the directions above or "to even":
```csharp
/// Rounds to the nearest nanosecond, with midpoints rounded away from zero.
```

Both forms spell "towards" with the trailing "s".

Alternatively, if it's preferable to name the exact operation (e.g. `floor()`), that can be used instead of the terminology above.

## Doc comments

### Layout

`<summary>` and `<remarks>` have a heavy preference for putting the opening tag, the contents and the closing tag on separate lines:
```csharp
/// <summary>
/// Uses the passed <see cref="IlEmitter"/> to emit a transaction-local instruction fragment.
/// </summary>
/// <param name="emitter">Emitter given to this callback.</param>
/// <remarks>
/// If the callback throws, the entire fragment is discarded.
/// </remarks>
```

The single-line form (`/// <summary>The left stick.</summary>`) is reserved for very short descriptions of self-evident members, such as enum cases and equality members.

Multi-paragraph contents use one `<para>` per paragraph.

### Tag order

Tags appear in this order: `<summary>`, `<typeparam>`, `<param>`, `<returns>`, `<exception>`, `<remarks>`.

### Properties

A property's `<summary>` describes what the property is, not what accessing it does, so it isn't phrased as "Gets ..." or "Sets ...":
```csharp
/// <summary>
/// The number of elements currently stored in the ring.
/// </summary>
public int Count { get; }
```

rather than "Gets the number of elements currently stored in the ring." Methods and operators can still be phrased in terms of "Gets ..." or "Sets ...".

### Exceptions

Each `<exception>` describes the condition under which it is thrown, phrased as "Thrown if ...":
```csharp
/// <exception cref="ArgumentException">
/// Thrown if <paramref name="scope"/> belongs to a different scheduler.
/// </exception>
```

Do **not** document throwing `InternalStateException` (`<exception cref="InternalStateException">`, etc.) on publicly accessible members. On internal/private members, it may be acceptable.

### Default values of structs

(This section is duplicated from [`invalid-default-values.md`](/docs/invalid-default-values.md).)

Every single struct in Injure documents whether its `default` value is valid, as the last paragraph of its `<remarks>`, as one of the following (square brackets denote a placeholder):
- `/// The <see langword="default"/> value is invalid.[ anything]`
- `/// The <see langword="default"/> value is valid[anything other than just a period]`

This applies regardless of accessibility; even one-off private record structs get it.

A struct implements `IHasInvalidDefault` if and only if it documents its `default` value as invalid.

### Internal/private types and members

Internal/private types and members are not exempt from doc comments; unlike public-API ones, there isn't a strict need to document, but if you find yourself writing an explanatory comment for an internal/private type or member targeted at anything other than the immediately surrounding code in the same file, it should most likely become a doc comment.

Additionally, public-API types and members nearly always have a `<summary>` if they have a doc comment at all (not counting `inheritdoc` ones); exemptions can be made for e.g. enum members whose role is obvious from just the name but have additional notes/remarks. Internal/private types are exempt from this, and may have e.g. purely a `<remarks>` or purely a `<returns>` or purely one or more `<exception>`s; that pattern is common for if the general purpose/behavior is already clear but some supplementary info or edge case(s) need to be noted.

Also see the section above; all structs must have a `<remarks>` documenting the validity of the `default` value.
