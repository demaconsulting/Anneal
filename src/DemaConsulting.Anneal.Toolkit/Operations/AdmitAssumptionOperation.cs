using DemaConsulting.Anneal.Toolkit.Model;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     Appends one approved assumption bullet verbatim to <c>.anneal/governance/assumptions.md</c>.
/// </summary>
/// <remarks>
///     This is the deterministic write half of <c>.anneal/architecture/toolkit/intake.md</c> TOOLKIT-46.
///     It makes no model call and performs no classification. The caller — a human who has already seen
///     and approved the exact wording proposed by <c>intake</c> — supplies the text; this action writes it.
///     <para>
///         Thread safety: instances are immutable and safe to share, but two concurrent runs against one repository
///         can race if both append to the same file.
///     </para>
/// </remarks>
public sealed class AdmitAssumptionOperation : IOperation
{
    private const string AssumptionsRelativePath = ".anneal/governance/assumptions.md";

    private readonly string _repositoryRoot;

    /// <summary>
    ///     Creates an operation over the current working directory.
    /// </summary>
    public AdmitAssumptionOperation()
        : this(Directory.GetCurrentDirectory())
    {
    }

    /// <summary>
    ///     Creates an operation against an explicit repository root.
    /// </summary>
    /// <param name="repositoryRoot">The repository written into. Must not be null, empty, or blank.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty, or blank.</exception>
    public AdmitAssumptionOperation(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
    }

    /// <inheritdoc />
    public string Name => "admit-assumption";

    /// <inheritdoc />
    public OperationCategory Category => OperationCategory.Authoring;

    /// <inheritdoc />
    public string Summary => "Append one approved assumption bullet verbatim to .anneal/governance/assumptions.md";

    /// <inheritdoc />
    public ModelRole? RequiredRole => null;

    /// <inheritdoc />
    public string Usage =>
        "usage: dotnet anneal admit-assumption <bullet text> - appends the exact bullet text verbatim as a " +
        "new bullet to .anneal/governance/assumptions.md. Makes no model call and performs no classification.";

    /// <inheritdoc />
    public Task<OperationResult> ExecuteAsync(
        IReadOnlyList<string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        cancellationToken.ThrowIfCancellationRequested();

        if (arguments.Count != 1 || string.IsNullOrWhiteSpace(arguments[0]))
            return Task.FromResult(new OperationResult(OperationOutcome.UsageError));

        var bulletText = IntakeOperation.NormalizeBulletText(arguments[0].Trim());
        var fullPath = Path.Combine(_repositoryRoot, AssumptionsRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(fullPath))
        {
            output.WriteLine(
                $"admit-assumption: escalated - '{AssumptionsRelativePath}' is missing; repair the repository " +
                "layout explicitly before admitting this assumption (for example via template-sync).");
            return Task.FromResult(new OperationResult(
                OperationOutcome.Escalated,
                new IntakeReport(AssumptionsRelativePath, bulletText, string.Empty, null, AssumptionsRelativePath)));
        }

        IntakeOperation.AppendBullet(fullPath, bulletText);
        output.WriteLine($"admit-assumption: filed in {AssumptionsRelativePath}");
        output.WriteLine($"  - {bulletText}");

        return Task.FromResult(new OperationResult(
            OperationOutcome.Succeeded,
            new IntakeReport(AssumptionsRelativePath, bulletText, string.Empty, null, null)));
    }
}
