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

The required tag shape:

```csharp
/// <summary>Converts a raw reading into a validated measurement.</summary>
/// <remarks>Clamps rather than throws: sensor drift at range boundaries is expected.</remarks>
/// <param name="reading">Raw sensor value. Must be finite.</param>
/// <returns>Corrected value clamped to the calibration range.</returns>
/// <exception cref="ArgumentException">Thrown when <paramref name="reading"/> is not finite.</exception>
public double ProcessReading(double reading, CalibrationProfile calibration)
```

## Interior Members (BY REASON, not by rule)

CS1591 does not cover `private` or `internal` members, and neither does this standard. Document an
interior member when its intent is **not recoverable from the code**, and leave it undocumented when
it is. Both of the following are correct:

```csharp
// No doc comment. The name and the single expression say everything a reader
// needs; a summary here could only restate them.
private static string NormalizeKey(string key) => key.Trim().ToLowerInvariant();

/// <remarks>
/// Retries only on 429 and 503. A 500 is deliberately not retried: this API returns
/// 500 for malformed payloads, so retrying one is guaranteed to fail again and costs
/// the caller their whole timeout budget. The backoff is jittered because the three
/// ingest workers otherwise retry in lockstep and re-create the burst that failed.
/// </remarks>
private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request)
```

The second carries a constraint, a rejected alternative, and a non-local reason — none of which
survive in the code alone, and all of which the next agent would otherwise have to rediscover by
breaking something.

This is a defect, not compliance: `/// <summary>Gets the user identifier.</summary>` on
`private int GetUserId() => _userId;` restates the signature and adds nothing a reader did not
already have. Delete it rather than let a file fill with comments indistinguishable from ones that
carry real intent.

# Code Formatting

- **Format entire solution**: `dotnet format`
- **Format specific project**: `dotnet format MyProject.csproj`
- **Format specific file**: `dotnet format --include MyFile.cs`

# Quality Checks

- [ ] XmlDoc complete on every publicly visible member (CS1591 clean)
- [ ] Interior members documented where intent is not recoverable from the code
- [ ] `dotnet format` applied (run `pwsh ./fix.ps1`)
