# EnsureThat.IncludeParameterNames.Fody

Goal: For all Ensure methods that can take an optional paramName, supply the name of the argument passed as input
```C#
// Original
Ensure.That(input).IsNotNull();
// After compiled
Ensure.That(input, nameof(input)).IsNotNull();
```

Currently supports:
* Ensure.That(T[, string, OptsFn])

Will complain (but should not error) if your call site is 'complicated' (anything more than passing 3 values to the `That` method)
