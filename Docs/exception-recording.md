# exception-recording.md

Storing plain `Exception` objects is hostile to mod reloadability; they're oftentimes types originating from foreign ALCs, and as such will count as a root for them and prevent them from being unloaded.

The standard type used to store information about an exception without retaining the exception object itself is `ExceptionSnapshot` from `Injure.Mods`. It attempts to retain any available information about the exception, such as the type name, assembly-qualified name, message, stacktrace, exception text, `Source`/`HelpLink`, and inner exceptions, all converted to their string representations.

These are created with `public static ExceptionSnapshot FromException(Exception ex)`. The dedicated exception type for rethrowing them is `ForeignException`, which is constructed with `.ToException()`, meaning that throwing them is as simple as:
```csharp
throw snapshot.ToException();
```

---

Some engine APIs may take in either user implementations of interfaces or direct callbacks and have special treatment for exceptions thrown out of them, including recording the exception, swallowing it, aborting some operation, etc.

`InternalStateException` is a special exception type thrown by engine-internal code on internal logic bugs or state corruption; see its docs for more deatils. Notably for this document, any of:
- `InternalStateException`
- A `TargetInvocationException` wrapping an `InternalStateException`
- An `AggregateException` containing any of the above, including nested `AggregateException`s

may be exempt from special treatment or be subject to different special treatment if thrown. Such an exception will typically not be recorded and instead be allowed to bubble out, though complete freedom on what exactly may happen is reserved.

`ExceptionSnapshot` will reject any of the above and will instead re-throw the exception that was attempted to be wrapped/snapshotted.
