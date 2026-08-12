using System.Text.Json;
using System.Text.Json.Serialization;
using DemaConsulting.Anneal.Toolkit.Architecture;
using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Workers;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     Architecture-document agreement check that can run either as <see cref="GeneralWorker" />'s absorbed
///     postflight obligation or as Maintain's explicit finish-time gate.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="GeneralWorker" /> uses this machinery as its own absorbed architecture-agreement
///         obligation, and <c>maintain</c> reuses it as an explicit finish-time gate after its Small-effort
///         GeneralWorker run. The same classify/correct/revert behavior applies in both shapes.
///     </para>
///     <para>
///         The gate is deliberately separate from the worker that authored the fix: "judging and doing have
///         different incentives" (<c>.anneal/governance/assumptions.md</c>) — a worker's own self-assessment of
///         its doc impact is not independent verification. The gate reads the actual git diff rather than the
///         worker's self-reported changed-file list, and runs one model-backed oracle question per matched
///         architecture document (never per file or per hunk) to keep cost proportional to the breadth of
///         architectural coverage, not to the mechanical size of the change.
///     </para>
///     <para>
///         For each matched document the gate reaches one of four outcomes: agree (no action), wording-only
///         mismatch outside the Contract section (narrow <see cref="DocumentAuthor" /> correction), contract
///         disagreement (neutral finding persisted under <c>.anneal/logs/findings/</c>, no edit), or cannot
///         classify (same neutral finding path, never a guess).
///     </para>
///     <para>Thread safety: instances are immutable and safe to share, but a run that reaches the wording-only
///     correction path edits the working tree.</para>
/// </remarks>
internal sealed class ArchDocAgreementGate
{
    /// <summary>The system message the per-document agreement oracle carries.</summary>
    private const string AgreementOracleCharter =
        """
        You are checking whether one architecture document still agrees with the code after a completed change.
        You are given the document's full text, the diff of the change, and the list of source files this
        document covers that the diff touched.

        Classify the relationship between the document and the new code as exactly one of:

        - "Agree": the document and the new code are consistent. No discrepancy exists, or any discrepancy is
          too minor to classify honestly.
        - "WordingOnly": the document and the new code disagree, but the disagreement is entirely in wording,
          illustrative detail, a stale symbol name, path, or example OUTSIDE the document's ## Contract section.
          Only classify as WordingOnly when you are confident the mismatch is purely cosmetic — a reader of the
          document and a reader of the code would understand the same intent, just expressed slightly differently.
          If you have any doubt, classify as CannotClassify instead.
        - "ContractDisagreement": the disagreement touches the substance of the document's ## Contract section
          (a clause identifier, a promise, a condition, a Verified-by link) or affects what the system promises
          to its consumers. Neither the document nor the code is presumed correct — report this as a neutral
          finding for a person to resolve.
        - "CannotClassify": you cannot confidently tell whether the disagreement is wording-only or
          contract-level, or whether there is a real disagreement at all. Escalate rather than guess.

        Set HasSufficientEvidence to false only when you cannot reach any of the four classifications above
        because the evidence itself is missing or self-contradictory (e.g. the diff is empty when you expected
        one). The four outcomes above are always reachable from a real document and a real diff, so refusing on
        a genuine document-plus-diff pair is wrong.

        When you classify WordingOnly, describe the specific stale text in the Reason field so the correction
        author knows exactly what to fix — be concrete.
        When you classify ContractDisagreement or CannotClassify, describe what you observed in the Reason field
        so the neutral finding record is useful to the person who will review it.
        """;

    /// <summary>The system message the wording-only correction author carries.</summary>
    private const string CorrectionAuthorCharter =
        """
        You are correcting a stale wording, illustrative detail, symbol name, path, or example in one
        architecture document, outside its ## Contract section. The classification oracle has already decided
        this is a wording-only mismatch — your only job is to apply the narrowest possible edit to bring the
        document's wording in line with the new code. Do not touch the ## Contract section. Do not rewrite
        prose that is not stale. Do not expand scope beyond the single stale passage described in your
        instruction.
        """;

    private readonly string _repositoryRoot;
    private readonly Func<ModelRole, IChatEndpoint>? _endpointFor;
    private readonly RunGitCommand? _runGit;

    /// <summary>
    ///     Binds the gate to a repository and its operating mode.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository root, outside which every tool call is refused. Must not be null or blank.
    /// </param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to use the GitHub Copilot SDK. Injected so the
    ///     gate is exercisable without a network call.
    /// </param>
    /// <param name="runGit">
    ///     Runs one <c>git</c> invocation for the diff-reading step, or null to use the real executable.
    ///     Injected so the gate is exercisable without a real repository.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null or blank.</exception>
    public ArchDocAgreementGate(
        string repositoryRoot,
        Func<ModelRole, IChatEndpoint>? endpointFor = null,
        RunGitCommand? runGit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _endpointFor = endpointFor;
        _runGit = runGit;
    }

    /// <summary>
    ///     Runs the agreement gate: reads the actual git diff, finds which architecture documents any changed
    ///     files fall under, and runs one oracle check per matched document.
    /// </summary>
    /// <param name="output">Console output sink. Must not be null.</param>
    /// <param name="operationName">The calling operation's name (e.g. <c>"route"</c> or <c>"maintain"</c>), used in output messages.</param>
    /// <param name="cancellationToken">Token observed for cooperative cancellation.</param>
    /// <returns>
    ///     A list of neutral disagreement findings (if any) that were persisted, so the caller can include
    ///     them in its output. Empty when all checked documents agree or were corrected inline. Never null.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="output" /> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<ArchDocAgreementOutcome> RunAsync(
        TextWriter output,
        string operationName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);

        cancellationToken.ThrowIfCancellationRequested();

        // Read the actual git diff — never the worker's self-reported file list.
        var diffFinding = await new DiffCheck(_repositoryRoot, runGit: _runGit)
            .TryReadAsync(null, cancellationToken)
            .ConfigureAwait(false);

        if (diffFinding is not { Available: true } || diffFinding.ChangedFiles.Count == 0)
            return new ArchDocAgreementOutcome([], []);

        // Load the architecture tree and find which documents cover any of the changed files.
        var architectureRoot = Path.Combine(_repositoryRoot, ".anneal", "architecture");
        var matchedDocuments = FindMatchedDocuments(_repositoryRoot, architectureRoot, diffFinding.ChangedFiles);
        if (matchedDocuments.Count == 0)
            return new ArchDocAgreementOutcome([], []);

        var findings = new List<ArchDocDisagreementFinding>();
        var correctedDocuments = new List<string>();

        foreach (var (relativePath, markdown) in matchedDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var coversGlobs = ArchitectureCoverage.ReadCoversGlobs(markdown);
            var coveredChangedFiles = ArchitectureCoverage.MatchingFiles(coversGlobs, diffFinding.ChangedFiles);

            if (coveredChangedFiles.Count == 0)
                continue;

            // One oracle call per matched document, not per changed file or per hunk.
            var classification = await ClassifyAgreementAsync(
                relativePath, markdown, diffFinding, coveredChangedFiles, cancellationToken)
                .ConfigureAwait(false);

            switch (classification.Verdict)
            {
                case AgreementVerdict.Agree:
                    // Document and code agree — nothing to do.
                    break;

                case AgreementVerdict.WordingOnly:
                {
                    // Wording-only mismatch outside Contract: attempt a narrow inline correction, then
                    // mechanically verify — never just trust the correcting model's own good behavior —
                    // that the correction it actually wrote stayed outside the ## Contract section.
                    var applied = await ApplyWordingCorrectionAsync(
                        output, operationName, relativePath, classification.Reason ?? string.Empty, cancellationToken)
                        .ConfigureAwait(false);

                    if (!applied)
                    {
                        var rejectedFinding = await PersistFindingAsync(
                            relativePath,
                            new AgreementClassification(
                                AgreementVerdict.CannotClassify,
                                "a wording-only correction was attempted but rejected — either it did not " +
                                "complete, or it touched the ## Contract section it was told not to touch"),
                            cancellationToken).ConfigureAwait(false);
                        if (rejectedFinding is not null)
                        {
                            findings.Add(rejectedFinding);
                            output.WriteLine(
                                $"{operationName}: arch-doc agreement finding — the wording-only correction for " +
                                $"{relativePath} was rejected and reverted; finding persisted to {rejectedFinding.FindingPath}");
                        }
                    }
                    else
                    {
                        correctedDocuments.Add(relativePath);
                    }

                    break;
                }

                case AgreementVerdict.ContractDisagreement:
                case AgreementVerdict.CannotClassify:
                {
                    // Neutral finding — no edit in either direction, but the finding must be persisted
                    // durably so the run cannot present itself as a silent success.
                    var finding = await PersistFindingAsync(
                        relativePath, classification, cancellationToken)
                        .ConfigureAwait(false);
                    if (finding is not null)
                    {
                        findings.Add(finding);
                        output.WriteLine(
                            $"{operationName}: arch-doc agreement finding — {relativePath} and the new code " +
                            $"no longer agree ({classification.Verdict}); " +
                            $"neither is presumed correct — finding persisted to {finding.FindingPath}");
                    }

                    break;
                }
            }
        }

        return new ArchDocAgreementOutcome(findings, correctedDocuments);
    }

    /// <summary>
    ///     Enumerates the architecture documents under <paramref name="architectureRoot" /> that have at least one
    ///     file in <paramref name="changedFiles" /> under their <c>covers:</c> globs, returning the
    ///     repository-relative path and full markdown source of each matched document.
    /// </summary>
    internal static IReadOnlyList<(string RelativePath, string Markdown)> FindMatchedDocuments(
        string repositoryRoot,
        string architectureRoot,
        IReadOnlyList<string> changedFiles)
    {
        var results = new List<(string, string)>();

        var rootDir = new DirectoryInfo(architectureRoot);
        if (!rootDir.Exists) return results;

        var allMarkdownFiles = rootDir
            .EnumerateFiles("*.md", SearchOption.AllDirectories)
            .Where(file => !string.Equals(file.Name, "overview.md", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var file in allMarkdownFiles)
        {
            string markdown;
            try
            {
                markdown = File.ReadAllText(file.FullName);
            }
            catch
            {
                continue;
            }

            var coversGlobs = ArchitectureCoverage.ReadCoversGlobs(markdown);
            if (!ArchitectureCoverage.CoversAnyFile(coversGlobs, changedFiles))
                continue;

            // Build the repository-relative path with forward slashes.
            var relativePath = Path.GetRelativePath(repositoryRoot, file.FullName)
                .Replace('\\', '/');

            results.Add((relativePath, markdown));
        }

        return results;
    }

    private async Task<AgreementClassification> ClassifyAgreementAsync(
        string documentRelativePath,
        string documentMarkdown,
        DiffFinding diffFinding,
        IReadOnlyList<string> coveredChangedFiles,
        CancellationToken cancellationToken)
    {
        var oracle = new Oracle<AgreementOracleDecision>(
            _repositoryRoot,
            AgreementOracleCharter,
            role: ModelRole.Light,
            endpointFor: _endpointFor);

        var question = ComposeOracleQuestion(
            documentRelativePath, documentMarkdown, diffFinding, coveredChangedFiles);

        try
        {
            var result = await oracle.AskAsync(question, [], cancellationToken).ConfigureAwait(false);
            var decision = result.Finding;

            if (decision is null)
                return new AgreementClassification(AgreementVerdict.CannotClassify, "oracle returned no decision");

            if (!decision.HasSufficientEvidence)
                return new AgreementClassification(AgreementVerdict.CannotClassify, "oracle reported insufficient evidence");

            return decision.Verdict switch
            {
                "WordingOnly" => new AgreementClassification(
                    AgreementVerdict.WordingOnly, decision.Reason ?? string.Empty),
                "ContractDisagreement" => new AgreementClassification(
                    AgreementVerdict.ContractDisagreement, decision.Reason ?? string.Empty),
                "CannotClassify" => new AgreementClassification(
                    AgreementVerdict.CannotClassify, decision.Reason ?? string.Empty),
                _ => new AgreementClassification(AgreementVerdict.Agree, string.Empty)
            };
        }
        catch
        {
            // A model failure on the gate oracle is a cannot-classify outcome, not a crash.
            return new AgreementClassification(AgreementVerdict.CannotClassify, "oracle call failed");
        }
    }

    private async Task<bool> ApplyWordingCorrectionAsync(
        TextWriter output,
        string operationName,
        string documentRelativePath,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            var author = new DocumentAuthor(
                _repositoryRoot,
                CorrectionAuthorCharter,
                endpointFor: _endpointFor,
                runGit: _runGit);

            var instruction =
                $"Correct the stale wording in '{documentRelativePath}' outside its ## Contract section. " +
                $"The specific stale passage: {reason}. Apply the narrowest possible fix — do not touch the " +
                "## Contract section, do not rewrite unrelated prose.";

            var result = await author.AuthorAsync(instruction, cancellationToken).ConfigureAwait(false);

            if (result.Outcome != OperationOutcome.Succeeded)
            {
                output.WriteLine(
                    $"{operationName}: wording correction for {documentRelativePath} could not complete " +
                    $"(outcome: {result.Outcome}); a person should review this document.");
                return false;
            }

            // Never trust the correcting model's own good behavior: mechanically re-read the diff it
            // just produced and confirm it stayed outside ## Contract. This is the same discipline
            // TOOLKIT-30/31 already apply to a worker's own self-report — verify the actual result, not
            // the instruction that was given.
            var afterDiff = await new DiffCheck(_repositoryRoot, runGit: _runGit)
                .TryReadAsync(null, cancellationToken)
                .ConfigureAwait(false);

            if (afterDiff is not null &&
                ArchitectureCoverage.PatchTouchesContractSection(afterDiff.Patch, documentRelativePath))
            {
                // The correction touched ## Contract despite being classified WordingOnly and despite
                // being told not to — revert it rather than let an unverified contract-level edit stand.
                var git = _runGit ?? new GitProcess(_repositoryRoot).RunAsync;
                await git(["checkout", "HEAD", "--", documentRelativePath], cancellationToken)
                    .ConfigureAwait(false);
                output.WriteLine(
                    $"{operationName}: wording correction for {documentRelativePath} touched the " +
                    "## Contract section and was reverted; a person should review this document.");
                return false;
            }

            output.WriteLine($"{operationName}: corrected stale wording in {documentRelativePath}");
            return true;
        }
        catch
        {
            output.WriteLine(
                $"{operationName}: wording correction for {documentRelativePath} failed unexpectedly; " +
                "a person should review this document.");
            return false;
        }
    }

    private async Task<ArchDocDisagreementFinding?> PersistFindingAsync(
        string documentRelativePath,
        AgreementClassification classification,
        CancellationToken cancellationToken)
    {
        try
        {
            var findingsDir = Path.Combine(_repositoryRoot, ".anneal", "logs", "findings");
            Directory.CreateDirectory(findingsDir);

            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
            var fileName = $"arch-disagreement-{timestamp}.json";
            var absolutePath = Path.Combine(findingsDir, fileName);
            var relativePath = $".anneal/logs/findings/{fileName}";

            var record = new ArchDocDisagreementRecord(
                documentRelativePath,
                classification.Verdict.ToString(),
                classification.Reason ?? string.Empty,
                DateTimeOffset.UtcNow.ToString("o"));

            var json = JsonSerializer.Serialize(
                record, ArchDocDisagreementRecordJsonContext.Default.ArchDocDisagreementRecord);
            await File.WriteAllTextAsync(absolutePath, json, cancellationToken).ConfigureAwait(false);

            return new ArchDocDisagreementFinding(documentRelativePath, classification.Verdict, relativePath);
        }
        catch
        {
            return null;
        }
    }

    private static string ComposeOracleQuestion(
        string documentPath,
        string documentMarkdown,
        DiffFinding diffFinding,
        IReadOnlyList<string> coveredChangedFiles)
    {
        var fileList = string.Join("\n", coveredChangedFiles);
        // Truncate the diff to avoid overwhelming the context window.
        var diffText = diffFinding.Patch.Length > 8000
            ? diffFinding.Patch[..8000] + "\n[diff truncated]"
            : diffFinding.Patch;

        return $"""
                Architecture document: {documentPath}

                <document>
                {documentMarkdown}
                </document>

                Covered source files touched by this change:
                {fileList}

                <diff>
                {diffText}
                </diff>

                Does the document still agree with the new code?
                """;
    }
}

/// <summary>
///     The verdict an <see cref="ArchDocAgreementGate" /> oracle reaches for one architecture document.
/// </summary>
internal enum AgreementVerdict
{
    /// <summary>The document and the new code agree. No action is needed.</summary>
    Agree,

    /// <summary>
    ///     The disagreement is in wording, illustrative detail, a stale symbol name, path, or example
    ///     outside the document's <c>## Contract</c> section. A narrow inline correction is warranted.
    /// </summary>
    WordingOnly,

    /// <summary>
    ///     The disagreement touches the substance of the document's <c>## Contract</c> section, or affects
    ///     what the system promises. Neither document nor code is presumed correct.
    /// </summary>
    ContractDisagreement,

    /// <summary>
    ///     The mismatch cannot be confidently classified as wording-only or contract-level. Escalate rather
    ///     than guess.
    /// </summary>
    CannotClassify
}

/// <summary>The decoded oracle decision for one architecture document's agreement check.</summary>
/// <param name="Verdict">
///     One of <c>"Agree"</c>, <c>"WordingOnly"</c>, <c>"ContractDisagreement"</c>, or <c>"CannotClassify"</c>.
/// </param>
/// <param name="Reason">
///     A description of the specific stale passage (for WordingOnly) or the nature of the disagreement
///     (for ContractDisagreement and CannotClassify). Empty when the verdict is Agree.
/// </param>
/// <param name="HasSufficientEvidence">
///     Whether the oracle had enough evidence to reach the verdict honestly.
/// </param>
internal sealed record AgreementOracleDecision(
    string Verdict,
    string? Reason,
    bool HasSufficientEvidence) : IOracleDecision;

/// <summary>Internal classification result from the agreement oracle, with the verdict and its supporting reason.</summary>
internal sealed record AgreementClassification(AgreementVerdict Verdict, string? Reason);

/// <summary>
///     Structured result of one architecture-doc agreement pass: the neutral findings it persisted, and which
///     documents it corrected inline as wording-only mismatches.
/// </summary>
internal sealed record ArchDocAgreementOutcome(
    IReadOnlyList<ArchDocDisagreementFinding> Findings,
    IReadOnlyList<string> CorrectedDocuments);

/// <summary>
///     A neutral disagreement finding persisted under <c>.anneal/logs/findings/</c> when an architecture document
///     and the code after a completed change no longer agree at the contract-substance level, or when the gate cannot
///     classify the mismatch confidently.
/// </summary>
/// <param name="DocumentPath">The repository-relative path of the architecture document. Never null.</param>
/// <param name="Verdict">The agreement verdict the gate reached.</param>
/// <param name="FindingPath">The repository-relative path of the persisted JSON finding file. Never null.</param>
internal sealed record ArchDocDisagreementFinding(
    string DocumentPath,
    AgreementVerdict Verdict,
    string FindingPath);

/// <summary>
///     The JSON record persisted to <c>.anneal/logs/findings/arch-disagreement-*.json</c> so a later human or
///     agent can discover the disagreement without having watched the live console output.
/// </summary>
/// <param name="DocumentPath">Repository-relative path of the architecture document. Never null.</param>
/// <param name="Verdict">
///     The agreement verdict: <c>"ContractDisagreement"</c> or <c>"CannotClassify"</c>. Never null.
/// </param>
/// <param name="Reason">
///     A description of what the oracle observed. Never null; may be empty when no reason was produced.
/// </param>
/// <param name="Timestamp">
///     ISO 8601 timestamp of when the finding was persisted. Never null.
/// </param>
internal sealed record ArchDocDisagreementRecord(
    string DocumentPath,
    string Verdict,
    string Reason,
    string Timestamp);

[JsonSerializable(typeof(ArchDocDisagreementRecord))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ArchDocDisagreementRecordJsonContext : JsonSerializerContext;
