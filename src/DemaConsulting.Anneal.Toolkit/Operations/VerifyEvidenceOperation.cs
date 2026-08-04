using System.Text.RegularExpressions;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     Checks every evidence locator cited in an agent report against the file and line it names, and reports
///     which quotations are really there.
/// </summary>
/// <remarks>
///     This is the half of "a judging agent must show the basis for its verdict" that a machine can settle.
///     It deliberately reaches no verdict about the report's conclusion and consults no model: whether a
///     quotation supports the argument built on it is judgement, while whether the quotation exists at all is
///     a fact, and mixing the two would make a deterministic check appear to certify an opinion.
///     <para>
///         It declares <see cref="OperationCategory.Enforcement" /> because its answer is decidable from the
///         repository alone and cannot change on unchanged input, which is what makes it safe to gate on.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, though a single instance reads the file
///         system and therefore sees whatever is on disk at the moment it runs.
///     </para>
/// </remarks>
public sealed class VerifyEvidenceOperation : IOperation
{
    /// <summary>
    ///     Whitespace differences are not citation errors. A report re-wraps a quotation to its own line
    ///     width, so comparing raw text would report a correct citation as absent — the one failure that
    ///     would teach a reader to stop believing this check.
    /// </summary>
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _sourceRoot;

    /// <summary>
    ///     Creates an operation that resolves cited paths against the current working directory, which is the
    ///     repository root when the tool is invoked as a repository's own tool.
    /// </summary>
    public VerifyEvidenceOperation()
        : this(Directory.GetCurrentDirectory())
    {
    }

    /// <summary>
    ///     Creates an operation that resolves cited paths against an explicit root.
    /// </summary>
    /// <param name="sourceRoot">
    ///     Directory that cited relative paths are resolved against, and outside which a citation is refused
    ///     as unresolvable. Must not be null or blank; it need not exist, in which case every citation is
    ///     reported absent.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourceRoot" /> is null, empty or blank.</exception>
    public VerifyEvidenceOperation(string sourceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        _sourceRoot = Path.GetFullPath(sourceRoot);
    }

    /// <inheritdoc />
    public string Name => "verify-evidence";

    /// <inheritdoc />
    public OperationCategory Category => OperationCategory.Enforcement;

    /// <inheritdoc />
    public string Summary => "Check that each quotation cited in an agent report is at the file and line named";

    /// <inheritdoc />
    public string Usage =>
        "usage: dotnet anneal verify-evidence <report-path> - checks every evidence locator cited in the " +
        "agent report at <report-path> against the file and line it names.";

    /// <inheritdoc />
    /// <remarks>
    ///     Expects exactly one argument: the path of the report to check, given positionally. Reports
    ///     <see cref="OperationOutcome.UsageError" /> when that argument is missing or accompanied by anything
    ///     else, and <see cref="OperationOutcome.Failed" /> when any cited quotation is not where the report
    ///     says it is, when the report cannot be read, and when the report cites nothing at all — the last of
    ///     those because a check that silently passes on finding no work to do is the failure mode this whole
    ///     process treats as worse than no check.
    /// </remarks>
    public OperationOutcome Execute(IReadOnlyList<string> arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        // No usage line is written here: the dispatcher renders Usage - the single declared source - on the
        // usage-error path, so the text a caller sees after a misuse cannot drift from what help prints.
        if (arguments.Count != 1)
            return OperationOutcome.UsageError;

        var reportPath = arguments[0];
        var resolvedReport = Resolve(reportPath);
        if (resolvedReport is null || !File.Exists(resolvedReport))
        {
            output.WriteLine($"verify-evidence: report not found: {reportPath}");
            return OperationOutcome.Failed;
        }

        var locators = EvidenceLocator.ParseAll(File.ReadAllText(resolvedReport));
        output.WriteLine($"verify-evidence: {reportPath}");

        if (locators.Count == 0)
        {
            output.WriteLine("  no evidence locators cited - nothing in this report can be checked.");
            return OperationOutcome.Failed;
        }

        // Each locator is reported on its own line whether or not it holds, so the output is a record of what
        // was checked rather than only of what went wrong.
        var absent = 0;
        foreach (var locator in locators)
        {
            var problem = Check(locator);
            if (problem is null)
            {
                output.WriteLine($"  present  {locator}");
                continue;
            }

            absent++;
            output.WriteLine($"  absent   {locator} - {problem}");
        }

        output.WriteLine($"  {locators.Count} locators: {locators.Count - absent} present, {absent} absent.");
        return absent == 0 ? OperationOutcome.Succeeded : OperationOutcome.Failed;
    }

    /// <returns>Null when the quotation is present as cited, otherwise the reason it is not.</returns>
    private string? Check(EvidenceLocator locator)
    {
        var path = Resolve(locator.Path);
        if (path is null)
            return "cited path is outside the repository";

        if (!File.Exists(path))
            return "file not found";

        var lines = File.ReadAllLines(path);
        if (locator.Line > lines.Length)
            return $"file has only {lines.Length} lines";

        return Normalize(lines[locator.Line - 1]).Contains(Normalize(locator.Quote), StringComparison.Ordinal)
            ? null
            : $"line {locator.Line} does not contain the quoted text";
    }

    /// <remarks>
    ///     Returns null rather than throwing for a path that escapes the root. A report is written by an agent
    ///     and is therefore untrusted input: a citation of an absolute system path must be reported as
    ///     unresolvable, not read.
    /// </remarks>
    private string? Resolve(string path)
    {
        var normalized = path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        var full = Path.GetFullPath(Path.Combine(_sourceRoot, normalized));

        var root = _sourceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.Ordinal) ? full : null;
    }

    private static string Normalize(string text) => WhitespaceRun.Replace(text, " ").Trim();
}
