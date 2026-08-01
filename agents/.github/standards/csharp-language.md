---
name: C# Language
description: Follow these standards when developing C# source code.
globs: ["**/*.cs"]
---

# Required Standards

Read these standards first before applying this standard:

- **`coding-principles.md`** - Universal coding principles and quality gates

# API Documentation and Literate Coding Example

## Publicly Visible Members (MANDATORY, enforced by the compiler)

`GenerateDocumentationFile=true` and `TreatWarningsAsErrors=true` in the project file make CS1591 —
missing XmlDoc on a publicly visible member — a **build error**. This is one of the few things in
this process that is mechanically enforced rather than left to judgement, so treat a missing
boundary doc comment as a broken build, not a review comment.

```csharp
/// <summary>
///     Converts a raw sensor reading into a validated measurement ready for downstream consumers.
/// </summary>
/// <remarks>
///     Clamping is preferred over throwing on out-of-range values because sensor drift at
///     range boundaries is expected; clamping produces a usable result where rejection would
///     discard valid near-boundary readings. Stateless and thread-safe; the calibration
///     profile is read but never modified.
/// </remarks>
/// <param name="reading">Raw sensor value. Must be finite (NaN and infinities are rejected).</param>
/// <param name="calibration">Calibration profile providing offset and range. Must not be null.</param>
/// <returns>Corrected value clamped to [calibration.Minimum, calibration.Maximum].</returns>
/// <exception cref="ArgumentException">Thrown when <paramref name="reading"/> is NaN or infinite.</exception>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="calibration"/> is null.</exception>
public double ProcessReading(double reading, CalibrationProfile calibration)
{
    // Reject invalid inputs before any calculation - non-finite readings cannot be
    // corrected, and a null calibration profile provides no offset or range to apply
    if (!double.IsFinite(reading))
        throw new ArgumentException("Reading must be a finite number.", nameof(reading));
    ArgumentNullException.ThrowIfNull(calibration);

    // Apply the calibration offset to convert raw counts to physical units
    var corrected = reading + calibration.Offset;

    // Clamp to the operational range so consumers can rely on the documented contract
    return Math.Clamp(corrected, calibration.Minimum, calibration.Maximum);
}
```

Key qualities demonstrated above:

- **`<summary>`** is a brief one-liner explaining *what* the method does
- **`<remarks>`** sits directly after summary and carries the extended intent -
  *why* it exists, design decisions, thread-safety, and side-effect disclosures
- **`<param>` tags** state constraints (finite, non-null) so callers know what
  is valid without reading the body
- **`<returns>`** documents the boundary guarantee so consumers can rely on the
  contract
- **`<exception>` tags** name every thrown exception and the condition that
  triggers each one
- **Inline block comments** follow the Literate Coding principles from
  `coding-principles.md`, separating logical steps so reviewers can verify each
  step against design intent

## Interior Members (BY REASON, not by rule)

CS1591 does not cover `private` or `internal` members, and neither does this standard. Document an
interior member when its intent is **not recoverable from the code**, and leave it undocumented when
it is. Both of the following are correct:

```csharp
// No doc comment. The name and the single expression say everything a reader
// needs; a summary here could only restate them.
private static string NormalizeKey(string key) => key.Trim().ToLowerInvariant();

/// <remarks>
///     Retries only on 429 and 503. A 500 is deliberately not retried: this API returns
///     500 for malformed payloads, so retrying one is guaranteed to fail again and costs
///     the caller their whole timeout budget. The backoff is jittered because the three
///     ingest workers otherwise retry in lockstep and re-create the burst that failed.
/// </remarks>
private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request)
```

The second carries a constraint, a rejected alternative, and a non-local reason — none of which
survive in the code alone, and all of which the next agent would otherwise have to rediscover by
breaking something.

This is a defect, not compliance:

```csharp
/// <summary>
///     Gets the user identifier.
/// </summary>
/// <returns>The user identifier.</returns>
private int GetUserId() => _userId;
```

It restates the signature, so it adds nothing a reader did not already have. Worse, it is
indistinguishable at a glance from a comment that does carry intent — and once a file is full of
these, a doc comment stops meaning "stop, there is something here you cannot infer." Delete it.

# Code Formatting

- **Format entire solution**: `dotnet format`
- **Format specific project**: `dotnet format MyProject.csproj`
- **Format specific file**: `dotnet format --include MyFile.cs`

# Quality Checks

- [ ] Zero compiler warnings (`TreatWarningsAsErrors=true`)
- [ ] XmlDoc complete on every publicly visible member (CS1591 clean)
- [ ] Interior members documented where intent is not recoverable from the code
- [ ] No doc comment restates the name, parameters, or return of its member
- [ ] `dotnet format` applied (run `pwsh ./fix.ps1`)
