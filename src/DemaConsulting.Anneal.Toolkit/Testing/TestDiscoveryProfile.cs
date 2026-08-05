namespace DemaConsulting.Anneal.Toolkit.Testing;

/// <summary>
///     How one test framework's tests are found and how its results are read.
/// </summary>
/// <remarks>
///     A repository using more than one framework — compiled boundary tests beside script-level suites, say —
///     describes each with its own profile. Every profile contributes its declarations and results to one
///     shared pool, so a clause is satisfied by whichever framework declares its test; but each profile is
///     held to the discovery and result checks separately, or a mistyped pattern in one would be covered up
///     by the framework that still works.
///     <para>Thread safety: immutable and safe to share.</para>
/// </remarks>
/// <param name="Index">The profile's position in the supplied set, counting from one, used to name it in messages.</param>
/// <param name="Roots">Roots searched for test source files, relative to the repository root.</param>
/// <param name="FilePatterns">File-name patterns selecting the test sources within those roots.</param>
/// <param name="ContractFolder">
///     Directory name marking the contract test location, or empty for a repository whose layout has no
///     interior and boundary split — in which case every discovered declaration counts as a boundary test.
/// </param>
/// <param name="Attributes">
///     Attribute names marking a method as a test. Applies to the default declaration shape only.
/// </param>
/// <param name="DeclarationPattern">
///     A regular expression with a named group <c>name</c> holding the declared test name, replacing the
///     attribute shape outright, or empty to use the attribute shape.
/// </param>
/// <param name="ResultsGlob">Glob for the result files, matched against the whole repository-relative path.</param>
/// <param name="Format">The form those result files take.</param>
public sealed record TestDiscoveryProfile(
    int Index,
    IReadOnlyList<string> Roots,
    IReadOnlyList<string> FilePatterns,
    string ContractFolder,
    IReadOnlyList<string> Attributes,
    string DeclarationPattern,
    string ResultsGlob,
    TestResultFormat Format)
{
    /// <summary>
    ///     Field names a profile record may set. These are the option names deliberately: a profile is a way
    ///     to say the same things more than once, not a configuration language of its own.
    /// </summary>
    public static readonly IReadOnlyList<string> FieldNames =
    [
        "TestRoots", "TestFilePatterns", "ContractTestFolder", "TestAttributes",
        "TestDeclarationPattern", "TestResults", "TestResultFormat"
    ];

    /// <summary>
    ///     What each field means when a caller says nothing. Together they describe a C# xUnit repository, so
    ///     a caller supplying nothing gets the C# behavior.
    /// </summary>
    private static readonly Dictionary<string, string> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TestRoots"] = "test,tests",
        ["TestFilePatterns"] = "*.cs",
        ["ContractTestFolder"] = "Contract",
        ["TestAttributes"] = "Fact,Theory",
        ["TestDeclarationPattern"] = "",
        ["TestResults"] = "artifacts/**/*.trx",
        ["TestResultFormat"] = "trx"
    };

    /// <summary>
    ///     A prefix naming this profile in a message, set only when there is more than one, so a
    ///     single-framework repository's output reads as though profiles did not exist.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    ///     The default set of fields, as a caller supplying no options would get them.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DefaultFields => Defaults;

    /// <summary>
    ///     Builds the profiles a run will check against.
    /// </summary>
    /// <remarks>
    ///     Records and individually supplied fields are rejected together rather than merged. Which one won
    ///     would be invisible at the call site that is meant to document the repository's layout.
    ///     <para>
    ///         An unrecognized field is an error rather than an ignored line: a misspelled field would
    ///         otherwise take its default in silence, and the profile would check the wrong thing while
    ///         reporting success. For the same reason a record that parses to nothing is an error, not an
    ///         empty profile.
    ///     </para>
    /// </remarks>
    /// <param name="records">
    ///     Profile records, each a <c>;</c>-separated list of <c>Field=Value</c> pairs. Must not be null.
    ///     Empty yields exactly one profile built from <paramref name="suppliedFields" />.
    /// </param>
    /// <param name="suppliedFields">
    ///     Fields the caller supplied individually rather than through a record, keyed by field name. Must
    ///     not be null.
    /// </param>
    /// <param name="errors">Collects a message for each problem found. Must not be null.</param>
    /// <returns>
    ///     The profiles, labelled when there is more than one. Empty when the supplied set could not be
    ///     understood, in which case <paramref name="errors" /> says why.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public static IReadOnlyList<TestDiscoveryProfile> Parse(
        IReadOnlyList<string> records,
        IReadOnlyDictionary<string, string> suppliedFields,
        IList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(suppliedFields);
        ArgumentNullException.ThrowIfNull(errors);

        var lines = records
            .SelectMany(record => record.Split(['\r', '\n']))
            .Select(record => record.Trim())
            .Where(record => record.Length > 0)
            .ToList();

        if (lines.Count == 0)
        {
            var only = Build(suppliedFields, 1, errors);
            return only is null ? [] : [only];
        }

        var conflicting = FieldNames
            .Where(field => suppliedFields.ContainsKey(field))
            .ToList();

        if (conflicting.Count > 0)
        {
            errors.Add(
                $"-TestProfiles cannot be combined with -{string.Join(", -", conflicting)}" +
                " - move those values into a profile record");
            return [];
        }

        var profiles = new List<TestDiscoveryProfile>();
        var index = 0;

        foreach (var record in lines)
        {
            index++;
            var fields = ParseRecord(record, index, errors);
            if (fields is null) continue;

            var built = Build(fields, index, errors);
            if (built is not null) profiles.Add(built);
        }

        return profiles.Count > 1
            ? [.. profiles.Select(item => item with { Label = $"profile {item.Index}: " })]
            : profiles;
    }

    private static Dictionary<string, string>? ParseRecord(string record, int index, IList<string> errors)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in record.Split(';'))
        {
            if (field.Trim().Length == 0) continue;

            var split = field.IndexOf('=', StringComparison.Ordinal);
            if (split < 1)
            {
                errors.Add($"profile {index}: '{field.Trim()}' is not a Key=Value field");
                continue;
            }

            var key = field[..split].Trim();
            var known = FieldNames.FirstOrDefault(
                name => string.Equals(name, key, StringComparison.OrdinalIgnoreCase));

            if (known is null)
            {
                errors.Add(
                    $"profile {index}: unknown field '{key}' - expected one of: {string.Join(", ", FieldNames)}");
                continue;
            }

            if (!fields.TryAdd(known, field[(split + 1)..].Trim()))
                errors.Add($"profile {index}: field '{known}' is set more than once");
        }

        if (fields.Count > 0) return fields;

        errors.Add($"profile {index}: no recognized fields in '{record}'");
        return null;
    }

    /// <remarks>
    ///     Returns null rather than a profile carrying an unusable format, because a profile that did not
    ///     parse cannot be checked against and running the remaining checks with a partial set would report
    ///     clauses as unverified for a reason that is not theirs.
    /// </remarks>
    private static TestDiscoveryProfile? Build(
        IReadOnlyDictionary<string, string> fields, int index, IList<string> errors)
    {
        var format = Field(fields, "TestResultFormat");

        if (!string.Equals(format, "trx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"profile {index}: TestResultFormat '{format}' is not one of: trx, text");
            return null;
        }

        return new TestDiscoveryProfile(
            index,
            SplitList(Field(fields, "TestRoots")),
            SplitList(Field(fields, "TestFilePatterns")),
            Field(fields, "ContractTestFolder"),
            SplitList(Field(fields, "TestAttributes")),
            Field(fields, "TestDeclarationPattern"),
            Field(fields, "TestResults"),
            string.Equals(format, "text", StringComparison.OrdinalIgnoreCase)
                ? TestResultFormat.Text
                : TestResultFormat.Trx);
    }

    private static string Field(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var value) ? value : Defaults[name];

    /// <remarks>
    ///     List-valued fields are comma-separated, so that the same syntax works whether the value arrives as
    ///     one argument or inside a profile record.
    /// </remarks>
    private static IReadOnlyList<string> SplitList(string value) =>
    [
        .. value
            .Split(',')
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
    ];
}
