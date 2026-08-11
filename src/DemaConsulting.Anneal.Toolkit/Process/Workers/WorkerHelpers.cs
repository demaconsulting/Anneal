using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;

namespace DemaConsulting.Anneal.Toolkit.Process.Workers;

/// <summary>
///     Pure static helpers shared between <see cref="ContractChangeWorker" /> and
///     <see cref="StructuralChangeWorker" />. Extracted rather than duplicated because both workers
///     share the same contract-check evidence label, the same fixed standard sets, and the same
///     output-merging and reroute-reason logic — a single place to read and change is worth more
///     than two copies that must stay in sync by hand.
/// </summary>
internal static class WorkerHelpers
{
    /// <summary>
    ///     The evidence label recorded for a worker's non-strict contract check. Not a file on disk:
    ///     the default runner calls <see cref="ContractCheckRunner" /> in process, so this names what
    ///     ran rather than a script path.
    /// </summary>
    internal const string ContractCheckScript = "check-contracts";

    /// <summary>
    ///     The fixed standards injected into every <see cref="DocumentAuthor" /> call: architecture-document
    ///     shape and the contract-clause rules, plus general markdown density since a documentation-owner
    ///     pass routinely touches non-architecture prose (README, user guide) alongside the system doc.
    /// </summary>
    internal static readonly string[] DocumentAuthorStandards =
        ["architecture-documentation.md", "system-contracts.md", "technical-documentation.md"];

    /// <summary>
    ///     The fixed standards injected into every <see cref="Developer" /> call: coding and C# language always,
    ///     plus testing and C# testing since each worker's charter has <see cref="Developer" /> implement code
    ///     and tests against the clauses or plan the documentation pass just produced.
    /// </summary>
    internal static readonly string[] DeveloperStandards =
        ["coding-principles.md", "csharp-language.md", "testing-principles.md", "csharp-testing.md"];

    internal static ChangeSetSummary Merge(DocumentChangeSet document, ChangeSetSummary code) =>
        new([.. document.FilesChanged, .. code.FilesChanged], $"{document.Summary} {code.Summary}".Trim());

    internal static ChangeSetBeforeStopping MergeInterrupted(DocumentChangeSet documentation, ChangeSetSummary code)
    {
        var merged = Merge(documentation, code);
        return new ChangeSetBeforeStopping(merged.FilesChanged, merged.Summary);
    }

    internal static string RerouteReason(VerificationFinding? finding) =>
        finding is null || finding.AdvisoryNotes.Count == 0
            ? "the verifier concluded this change needs to be rerouted, with no further reason recorded"
            : string.Join("; ", finding.AdvisoryNotes);

    internal static string ComposeRepairInstruction(string originalInstruction, IReadOnlyList<string> requiredFixes) =>
        requiredFixes.Count == 0
            ? originalInstruction
            : $"""
               {originalInstruction}

               The previous attempt's verification reported these required fixes:
               {string.Join("\n", requiredFixes)}

               Repair the issue.
               """;
}
