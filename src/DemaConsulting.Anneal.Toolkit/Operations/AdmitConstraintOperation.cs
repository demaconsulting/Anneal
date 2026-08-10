using DemaConsulting.Anneal.Toolkit.Model;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     Appends one approved constraint bullet verbatim under a named section of <c>.anneal/work/constraints.md</c>.
/// </summary>
/// <remarks>
///     This is the deterministic write half of <c>.anneal/architecture/toolkit/intake.md</c> TOOLKIT-47.
///     It makes no model call and performs no classification. The caller — a human who has already seen
///     and approved the exact wording proposed by <c>intake</c> — supplies the text and section; this action writes it.
///     <para>
///         The section designator must be exactly <c>satisfied</c> or <c>not-yet-satisfied</c> (case-insensitive);
///         any other value is a usage error.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, but two concurrent runs against one repository
///         can race if both append to the same file.
///     </para>
/// </remarks>
public sealed class AdmitConstraintOperation : IOperation
{
    private const string ConstraintsRelativePath = ".anneal/work/constraints.md";
    private const string SatisfiedHeader = "## Satisfied";
    private const string NotYetSatisfiedHeader = "## Not Yet Satisfied";

    private readonly string _repositoryRoot;

    /// <summary>
    ///     Creates an operation over the current working directory.
    /// </summary>
    public AdmitConstraintOperation()
        : this(Directory.GetCurrentDirectory())
    {
    }

    /// <summary>
    ///     Creates an operation against an explicit repository root.
    /// </summary>
    /// <param name="repositoryRoot">The repository written into. Must not be null, empty, or blank.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty, or blank.</exception>
    public AdmitConstraintOperation(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
    }

    /// <inheritdoc />
    public string Name => "admit-constraint";

    /// <inheritdoc />
    public OperationCategory Category => OperationCategory.Authoring;

    /// <inheritdoc />
    public string Summary =>
        "Append one approved constraint bullet verbatim under a named section of .anneal/work/constraints.md";

    /// <inheritdoc />
    public ModelRole? RequiredRole => null;

    /// <inheritdoc />
    public string Usage =>
        "usage: dotnet anneal admit-constraint <bullet text> <section> - appends the bullet text verbatim " +
        "under the named section (satisfied|not-yet-satisfied) of .anneal/work/constraints.md. " +
        "Makes no model call and performs no classification.";

    /// <inheritdoc />
    public Task<OperationResult> ExecuteAsync(
        IReadOnlyList<string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        cancellationToken.ThrowIfCancellationRequested();

        if (arguments.Count != 2 || string.IsNullOrWhiteSpace(arguments[0]) || string.IsNullOrWhiteSpace(arguments[1]))
            return Task.FromResult(new OperationResult(OperationOutcome.UsageError));

        var sectionArg = arguments[1].Trim();
        var sectionHeader = sectionArg.ToLowerInvariant() switch
        {
            "satisfied" => SatisfiedHeader,
            "not-yet-satisfied" => NotYetSatisfiedHeader,
            _ => null
        };

        if (sectionHeader is null)
            return Task.FromResult(new OperationResult(OperationOutcome.UsageError));

        var bulletText = IntakeOperation.NormalizeBulletText(arguments[0].Trim());
        var fullPath = Path.Combine(_repositoryRoot, ConstraintsRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(fullPath))
        {
            output.WriteLine(
                $"admit-constraint: escalated - '{ConstraintsRelativePath}' is missing; repair the repository " +
                "layout explicitly before admitting this constraint (for example via template-sync).");
            return Task.FromResult(new OperationResult(
                OperationOutcome.Escalated,
                new IntakeReport(ConstraintsRelativePath, bulletText, string.Empty, sectionArg, ConstraintsRelativePath)));
        }

        AppendUnderSection(fullPath, sectionHeader, bulletText);
        var humanSection = sectionArg.ToLowerInvariant() == "satisfied" ? "Satisfied" : "Not Yet Satisfied";
        output.WriteLine($"admit-constraint: filed in {ConstraintsRelativePath} under {humanSection}");
        output.WriteLine($"  - {bulletText}");

        return Task.FromResult(new OperationResult(
            OperationOutcome.Succeeded,
            new IntakeReport(ConstraintsRelativePath, bulletText, string.Empty, humanSection, null)));
    }

    /// <remarks>
    ///     Inserts the bullet at the <em>end</em> of the target section — immediately before the next
    ///     <c>## </c> heading, or at end of file if the section is last — never right after the header line.
    ///     A constraints section opens with descriptive prose before its bullet list begins, so inserting
    ///     right after the header would land the new bullet ahead of that prose and corrupt the document
    ///     structure. If the section header is not found, the bullet is appended at the end of the file so
    ///     the write never silently drops the item — the caller sees it in the output regardless.
    /// </remarks>
    private static void AppendUnderSection(string fullPath, string sectionHeader, string bulletText)
    {
        var lines = File.ReadAllLines(fullPath).ToList();
        var headerIndex = lines.FindIndex(line => line.Equals(sectionHeader, StringComparison.OrdinalIgnoreCase));

        if (headerIndex < 0)
        {
            lines.Add($"- {bulletText}");
            File.WriteAllLines(fullPath, lines);
            return;
        }

        // Find the next top-level heading after this section's header, or the end of the file.
        var nextHeadingIndex = lines.FindIndex(
            headerIndex + 1, line => line.StartsWith("## ", StringComparison.Ordinal));
        var sectionEnd = nextHeadingIndex >= 0 ? nextHeadingIndex : lines.Count;

        // Back up over trailing blank lines so the new bullet sits immediately after the last real line
        // of the section, not floating below a blank gap before the next heading.
        while (sectionEnd > headerIndex + 1 && string.IsNullOrWhiteSpace(lines[sectionEnd - 1]))
            sectionEnd--;

        lines.Insert(sectionEnd, $"- {bulletText}");
        File.WriteAllLines(fullPath, lines);
    }
}
