using System.ComponentModel;
using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Primitives;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     Files one Intake-mode work item into the backlog or assumptions register, or escalates a proposed
///     constraint for user admission.
/// </summary>
/// <remarks>
///     <c>.anneal/architecture/toolkit/intake.md</c> is the contract this implements. The action asks one narrow
///     typed question — backlog, assumption, or constraint, with the bullet text to use — and then applies the
///     user-admitted-constraint rule mechanically: backlog and assumption entries are appended directly, while a
///     constraint classification never writes <c>.anneal/work/constraints.md</c> and instead escalates with the
///     proposed bullet and target section.
///     <para>
///         It declares <see cref="OperationCategory.Authoring" /> because filing backlog and assumption work edits
///         repository content for later use. A constraint proposal still reports through the same action because
///         the question it answered was authoring-related even when the safe result is "stop and ask for
///         admission."
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, but two concurrent runs against one repository
///         can race if both append to the same register.
///     </para>
/// </remarks>
public sealed class IntakeOperation : IOperation
{
    /// <summary>The oracle charter carrying the Intake admission test and the safer-side bias.</summary>
    private const string Charter =
        """
        You are classifying one Intake work item for Anneal's filing path.

        Apply exactly this admission test:
        - If the item completes and stays finished, it belongs in backlog.
        - If it holds rather than completes, and reality could prove it wrong without anyone changing their
          mind, it is an assumption.
        - If it holds rather than completes, and only a decision could change it, it is a constraint.

        Return the bullet text exactly as it should appear after "- " in the chosen destination, with no
        leading bullet marker. For a constraint, also name whether it belongs under Satisfied or NotYetSatisfied.

        Refusing is a correct answer when the wording does not provide enough evidence to tell whether the
        item completes, is an assumption, or is a constraint. When the wording could plausibly be a standing
        condition rather than a discrete piece of finished work, prefer Constraint over silently filing it as
        backlog or assumption.
        """;

    private const string BacklogRelativePath = ".anneal/work/backlog.md";
    private const string AssumptionsRelativePath = ".anneal/governance/assumptions.md";
    private const string ConstraintsRelativePath = ".anneal/work/constraints.md";

    private readonly string _repositoryRoot;
    private readonly Func<ModelRole, IChatEndpoint>? _endpointFor;

    /// <summary>
    ///     Creates an operation over the current working directory, consulting the configured models.
    /// </summary>
    public IntakeOperation()
        : this(Directory.GetCurrentDirectory())
    {
    }

    /// <summary>
    ///     Creates an operation against an explicit repository root and, optionally, a substituted endpoint
    ///     provider.
    /// </summary>
    /// <param name="repositoryRoot">The repository written into. Must not be null, empty, or blank.</param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK.
    ///     Injected so this operation's whole behavior is exercisable without a network call.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public IntakeOperation(string repositoryRoot, Func<ModelRole, IChatEndpoint>? endpointFor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _endpointFor = endpointFor;
    }

    /// <inheritdoc />
    public string Name => "intake";

    /// <inheritdoc />
    public OperationCategory Category => OperationCategory.Authoring;

    /// <inheritdoc />
    public string Summary => "File one Intake work item into backlog or assumptions, or escalate a constraint proposal";

    /// <inheritdoc />
    public ModelRole? RequiredRole => ModelRole.Light;

    /// <inheritdoc />
    public string Usage =>
        "usage: dotnet anneal intake <work item> - classifies <work item> through the Intake admission test " +
        "and either appends one bullet to .anneal/work/backlog.md or .anneal/governance/assumptions.md, or " +
        "escalates a proposed constraint for .anneal/work/constraints.md without writing it.";

    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(
        IReadOnlyList<string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        cancellationToken.ThrowIfCancellationRequested();

        if (arguments.Count != 1 || string.IsNullOrWhiteSpace(arguments[0]))
            return new OperationResult(OperationOutcome.UsageError);

        var workItem = arguments[0].Trim();
        output.WriteLine($"intake: classifying \"{workItem}\"...");

        var decisionResult = await AskAsync(workItem, cancellationToken).ConfigureAwait(false);
        if (decisionResult.Outcome == OperationOutcome.Failed || decisionResult.Finding is null)
        {
            output.WriteLine("intake: failed - no intake decision was obtained.");
            return new OperationResult(OperationOutcome.Failed);
        }

        var decision = decisionResult.Finding;
        var bulletText = NormalizeBulletText(decision.BulletText);

        if (decisionResult.Outcome == OperationOutcome.Refused)
        {
            output.WriteLine($"intake: refused - {decision.Why}");
            return new OperationResult(
                OperationOutcome.Refused,
                new IntakeReport(string.Empty, bulletText, decision.Why, null, null));
        }

        return decision.Kind switch
        {
            IntakeDecisionKind.Backlog => AppendToRegister(
                output, BacklogRelativePath, workItem, bulletText, decision.Why, cancellationToken),
            IntakeDecisionKind.Assumption => AppendToRegister(
                output, AssumptionsRelativePath, workItem, bulletText, decision.Why, cancellationToken),
            IntakeDecisionKind.Constraint => EscalateConstraint(output, bulletText, decision),
            _ => FailedDecision(output, decision)
        };
    }

    private async Task<StepResult<IntakeDecisionEnvelope>> AskAsync(string workItem, CancellationToken cancellationToken)
    {
        var oracle = new Oracle<IntakeDecisionEnvelope>(
            _repositoryRoot, Charter, role: ModelRole.Light, endpointFor: _endpointFor);

        return await oracle
            .AskAsync(
                $"Classify this Intake item and provide the filing bullet text: {workItem}",
                [],
                cancellationToken)
            .ConfigureAwait(false);
    }

    private OperationResult AppendToRegister(
        TextWriter output,
        string relativePath,
        string workItem,
        string bulletText,
        string why,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.Combine(_repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            output.WriteLine(
                $"intake: escalated - '{relativePath}' is missing; repair the repository layout explicitly " +
                "before filing this item (for example via template-sync).");
            return new OperationResult(
                OperationOutcome.Escalated,
                new IntakeReport(relativePath, bulletText, why, null, relativePath));
        }

        AppendBullet(fullPath, bulletText);
        output.WriteLine($"intake: filed in {relativePath}");
        output.WriteLine($"  - {bulletText}");
        output.WriteLine($"intake: completed - {why}");

        return new OperationResult(
            OperationOutcome.Succeeded,
            new IntakeReport(relativePath, bulletText, why, null, null));
    }

    private static OperationResult EscalateConstraint(
        TextWriter output, string bulletText, IntakeDecisionEnvelope decision)
    {
        if (decision.ConstraintSection == IntakeConstraintSection.None)
        {
            output.WriteLine("intake: failed - the constraint classification named no target section.");
            return new OperationResult(OperationOutcome.Failed);
        }

        var section = Describe(decision.ConstraintSection);
        output.WriteLine(
            $"intake: escalated - proposed constraint for {ConstraintsRelativePath} under {section}:");
        output.WriteLine($"  - {bulletText}");
        output.WriteLine($"intake: reason - {decision.Why}");

        return new OperationResult(
            OperationOutcome.Escalated,
            new IntakeReport(ConstraintsRelativePath, bulletText, decision.Why, section, null));
    }

    private static OperationResult FailedDecision(TextWriter output, IntakeDecisionEnvelope decision)
    {
        output.WriteLine("intake: failed - the intake decision could not be applied.");
        return new OperationResult(
            OperationOutcome.Failed,
            new IntakeReport(string.Empty, NormalizeBulletText(decision.BulletText), decision.Why, null, null));
    }

    private static void AppendBullet(string fullPath, string bulletText)
    {
        var prefix = string.Empty;
        if (new FileInfo(fullPath).Length > 0)
        {
            using var stream = File.OpenRead(fullPath);
            stream.Seek(-1, SeekOrigin.End);
            var trailingByte = stream.ReadByte();
            if (trailingByte != '\n')
                prefix = Environment.NewLine;
        }

        File.AppendAllText(fullPath, $"{prefix}- {bulletText}{Environment.NewLine}");
    }

    private static string NormalizeBulletText(string bulletText)
    {
        var trimmed = bulletText.Trim();
        return trimmed.StartsWith("- ", StringComparison.Ordinal) ? trimmed[2..].TrimStart() : trimmed;
    }

    private static string Describe(IntakeConstraintSection section) => section switch
    {
        IntakeConstraintSection.Satisfied => "Satisfied",
        IntakeConstraintSection.NotYetSatisfied => "Not Yet Satisfied",
        _ => string.Empty
    };

    private enum IntakeDecisionKind
    {
        [Description("the item completes and should be filed as backlog work")]
        Backlog,

        [Description("the item is a disprovable design assumption")]
        Assumption,

        [Description("the item is a durable condition requiring user admission as a constraint")]
        Constraint
    }

    private enum IntakeConstraintSection
    {
        [Description("no constraint section applies")]
        None,

        [Description("the current design already satisfies the constraint")]
        Satisfied,

        [Description("the current design does not yet satisfy the constraint")]
        NotYetSatisfied
    }

    private sealed record IntakeDecisionEnvelope : IOracleDecision
    {
        public required IntakeDecisionKind Kind { get; init; }

        public required string Why { get; init; }

        public required string BulletText { get; init; }

        public required IntakeConstraintSection ConstraintSection { get; init; }

        public required bool HasSufficientEvidence { get; init; }
    }
}
