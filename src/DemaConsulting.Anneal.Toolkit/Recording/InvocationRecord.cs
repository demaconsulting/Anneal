using DemaConsulting.Anneal.Toolkit.Model;

namespace DemaConsulting.Anneal.Toolkit.Recording;

/// <summary>
///     The record of one invocation of the tool: what was asked for, what it concluded, and what the answer
///     cost.
/// </summary>
/// <remarks>
///     Written so that a later query can aggregate runs without parsing prose. The figures this whole system
///     rests on were originally recovered by scraping agent reports with regular expressions, which had already
///     produced one plausible wrong answer; a record whose fields are named and typed removes that step rather
///     than making it more careful.
///     <para>
///         <see cref="Outcome" /> is a name and never a position. Aggregation is across runs, and runs span
///         releases, so a record written by one version is read against another: identifying an outcome by an
///         ordinal that shifts when a member is inserted mid-set would quietly change what an old record means.
///     </para>
/// </remarks>
/// <param name="At">When the invocation started.</param>
/// <param name="ToolVersion">The Anneal version the tool was built from. Never null.</param>
/// <param name="Action">The action as the caller named it, or the empty string when none was named.</param>
/// <param name="Arguments">The arguments the action was given, in order. Never null.</param>
/// <param name="Outcome">
///     The name of the <see cref="OperationOutcome" /> reached, never its numeric position.
/// </param>
/// <param name="Category">
///     The name of the <see cref="OperationCategory" /> the action declared, or null when no action ran.
/// </param>
/// <param name="ExitCode">The process exit code this invocation mapped to.</param>
/// <param name="ModelInteractions">How many model interactions the invocation made; zero when none.</param>
/// <param name="Usage">What those interactions consumed in total, or null when there were none.</param>
/// <param name="DurationMilliseconds">How long the invocation took.</param>
public sealed record InvocationRecord(
    DateTimeOffset At,
    string ToolVersion,
    string Action,
    IReadOnlyList<string> Arguments,
    string Outcome,
    string? Category,
    int ExitCode,
    int ModelInteractions,
    ModelUsage? Usage,
    double DurationMilliseconds);
