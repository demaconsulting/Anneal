using DemaConsulting.Anneal.Toolkit.Architecture;
using DemaConsulting.Anneal.Toolkit.Files;
using DemaConsulting.Anneal.Toolkit.Testing;

namespace DemaConsulting.Anneal.Toolkit.Enforcement;

/// <summary>
///     Checks that every clause of a repository's contract names a boundary test, that the test exists, and
///     that it passed.
/// </summary>
/// <remarks>
///     This is the one mechanically enforced rule of the process: architecture documents are promises, and a
///     promise nobody checks is a description. Every check therefore fails closed. A clause whose identifier
///     does not parse, a document that declares no contract, a discovery pattern matching nothing, a result
///     file that predates the test it describes — each is reported rather than skipped, because a checker
///     that is quietly wrong is worse than no checker at all.
///     <para>Thread safety: stateless and safe for concurrent calls; each call reads the repository as it is then.</para>
/// </remarks>
public static class ContractCheck
{
    /// <summary>
    ///     Runs the check.
    /// </summary>
    /// <param name="options">What to check and how. Must not be null.</param>
    /// <returns>What was found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options" /> is null.</exception>
    public static ContractCheckReport Run(ContractCheckOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        var warnings = new List<string>();

        var profiles = TestDiscoveryProfile.Parse(options.ProfileRecords, options.SuppliedFields, errors);

        // A profile that did not parse cannot be checked against.
        if (errors.Count > 0)
            return new ContractCheckReport(
                ContractCheckStage.ProfilesRejected, options.ArchitectureRoot, 0, 0, warnings, errors);

        var architectureRoot = Path.Combine(options.RepositoryRoot, options.ArchitectureRoot);
        var clauses = ReadClauses(ArchitectureTree.Read(architectureRoot), errors);

        if (clauses.Count == 0 && errors.Count == 0)
            return new ContractCheckReport(
                ContractCheckStage.NothingToCheck, options.ArchitectureRoot, 0, 0, warnings, errors);

        CheckIdentifiersAreUnique(clauses, errors);
        CheckEveryClauseNamesATest(clauses, errors);

        var declarations = CheckDiscoveryFoundTests(options, profiles, clauses, errors, out var discoveryFailed);
        var unresolved = CheckNamedTestsAreBoundaryTests(
            options, profiles, clauses, declarations, discoveryFailed, errors, warnings);

        CheckNamedTestsPassed(options, profiles, clauses, unresolved, errors, warnings);

        return new ContractCheckReport(
            ContractCheckStage.Checked,
            options.ArchitectureRoot,
            clauses.Count,
            clauses.Sum(clause => clause.Verifiers.Count),
            warnings,
            errors);
    }

    /// <remarks>
    ///     Check 1 and part of check 2. A system document with no contract, and a bolded item under a clause
    ///     subsection whose identifier does not parse, are both errors: skipping them in silence let a
    ///     renamed heading or a hyphenated system prefix remove a clause from the check while the run still
    ///     reported success.
    /// </remarks>
    private static List<ContractClause> ReadClauses(ArchitectureTree tree, List<string> errors)
    {
        foreach (var document in tree.Documents)
        {
            foreach (var malformed in document.MalformedClauses)
                errors.Add(
                    $"{document.Name}: '{malformed.Label}' under '{malformed.Section}' " +
                    "is not a well-formed clause ID (expected {SYSTEM}-nn or {SYSTEM}-In)");

            // Only a level 2 system document owns a contract; a section document elaborates one system's
            // interior and is read for the clauses it carries without being expected to declare a heading.
            if (document is { IsSystemDocument: true, DeclaresContract: false })
                errors.Add($"{document.Name}: system document has no '## Contract' section");
        }

        return [.. tree.Clauses];
    }

    /// <remarks>
    ///     Check 2. Two clauses sharing an identifier means one of them is unreachable from any discussion
    ///     that cites it, and a change to "that clause" would land on whichever the reader happened to find.
    /// </remarks>
    private static void CheckIdentifiersAreUnique(List<ContractClause> clauses, List<string> errors)
    {
        foreach (var duplicate in clauses
                     .GroupBy(clause => clause.Id, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
            errors.Add(
                $"Duplicate clause ID '{duplicate.Key}' in: " +
                string.Join(", ", duplicate.Select(clause => clause.DocumentName)));
    }

    /// <remarks>
    ///     Check 3. A clause naming no test is the failure this whole check exists to catch: it reads as a
    ///     promise and behaves as a description.
    /// </remarks>
    private static void CheckEveryClauseNamesATest(List<ContractClause> clauses, List<string> errors)
    {
        foreach (var clause in clauses.Where(clause => clause.Verifiers.Count == 0))
            errors.Add($"{clause.DocumentName}: clause {clause.Id} names no verifying test");
    }

    /// <remarks>
    ///     Check 4. Reported once per profile, naming what matched nothing. Reporting each clause as a
    ///     missing test instead would describe the wrong repair — the tests exist, the patterns point
    ///     elsewhere. A run whose verifiers are all planned obligations is exempt, because a tree being
    ///     bootstrapped is not expected to resolve anything.
    /// </remarks>
    private static TestDeclarationIndex CheckDiscoveryFoundTests(
        ContractCheckOptions options,
        IReadOnlyList<TestDiscoveryProfile> profiles,
        List<ContractClause> clauses,
        List<string> errors,
        out bool discoveryFailed)
    {
        var perProfile = profiles
            .Select(profile => TestDeclarationIndex.Scan(options.RepositoryRoot, profile))
            .ToList();

        var pooled = TestDeclarationIndex.Pool(perProfile);

        var required = clauses
            .SelectMany(clause => clause.Verifiers)
            .Count(verifier => !verifier.IsPlannedObligation);

        for (var index = 0; index < profiles.Count; index++)
        {
            if (perProfile[index].Count > 0 || required == 0) continue;

            var profile = profiles[index];
            var shape = profile.DeclarationPattern.Length > 0
                ? profile.DeclarationPattern
                : $"attribute-marked methods ({string.Join(", ", profile.Attributes)})";

            errors.Add(
                $"{profile.Label}No test declarations found in '{string.Join(", ", profile.Roots)}' " +
                $"matching '{string.Join(", ", profile.FilePatterns)}' - {required} verifiers need one, " +
                $"so fix the discovery patterns rather than the tests. Declaration shape: {shape}");
        }

        // Per-clause "not declared" errors are suppressed only when NOTHING was discovered anywhere; a run
        // where one profile worked still owes a per-clause answer for the tests that profile did not declare.
        discoveryFailed = pooled.Count == 0 && required > 0;
        return pooled;
    }

    /// <remarks>
    ///     Check 5. A clause is satisfied only by a real declared test in a contract test location. Matching
    ///     bare identifiers against the test sources was far too generous: a private helper, or even a string
    ///     literal, could keep a clause's promise alive after its test was gone.
    /// </remarks>
    private static HashSet<string> CheckNamedTestsAreBoundaryTests(
        ContractCheckOptions options,
        IReadOnlyList<TestDiscoveryProfile> profiles,
        List<ContractClause> clauses,
        TestDeclarationIndex declarations,
        bool discoveryFailed,
        List<string> errors,
        List<string> warnings)
    {
        var unresolved = new HashSet<string>(StringComparer.Ordinal);

        var searched = string.Join(
            ", ",
            profiles.SelectMany(profile => profile.Roots).Distinct(StringComparer.OrdinalIgnoreCase));

        var defaultContractFolder = options.SuppliedFields.TryGetValue("ContractTestFolder", out var supplied)
            ? supplied
            : TestDiscoveryProfile.DefaultFields["ContractTestFolder"];

        foreach (var clause in clauses)
            foreach (var verifier in clause.Verifiers)
            {
                // The obligation is the placeholder form, not any verifier mentioning TODO: a genuine test whose
                // name carries the word is checked normally.
                if (verifier.IsPlannedObligation)
                {
                    var message =
                        $"{clause.DocumentName}: clause {clause.Id} has an unfulfilled test obligation '{verifier}'";
                    (options.Strict ? errors : warnings).Add(message);
                    unresolved.Add(verifier.Text);
                    continue;
                }

                var declaration = declarations.Find(verifier.TestName);

                if (declaration is null)
                {
                    unresolved.Add(verifier.Text);

                    // Suppressed when discovery itself failed: check 4 has already said why, and repeating it
                    // per clause would bury that finding.
                    if (!discoveryFailed)
                        errors.Add(
                            $"{clause.DocumentName}: clause {clause.Id} names test '{verifier}' " +
                            $"which is not declared as a test method in {searched}");

                    continue;
                }

                if (declaration.IsContractTest) continue;

                unresolved.Add(verifier.Text);

                var expected = declaration.ExpectedFolders.Count > 0
                    ? string.Join("' or '", declaration.ExpectedFolders)
                    : defaultContractFolder;

                errors.Add(
                    $"{clause.DocumentName}: clause {clause.Id} names test '{verifier}' " +
                    $"which is not in a '{expected}' folder (found in {string.Join(", ", declaration.Files)}) " +
                    "- contract tests must be boundary tests");
            }

        return unresolved;
    }

    /// <remarks>
    ///     Checks 6 and 7. Outcomes pool across profiles exactly as declarations do, and by the same rule
    ///     within the pool as within one profile: the newest result for a name wins, and a failure is never
    ///     overwritten by a pass of the same age. Staleness is compared within a profile, because a fresh
    ///     compiled-test run says nothing about an old script tally.
    /// </remarks>
    private static void CheckNamedTestsPassed(
        ContractCheckOptions options,
        IReadOnlyList<TestDiscoveryProfile> profiles,
        List<ContractClause> clauses,
        HashSet<string> unresolved,
        List<string> errors,
        List<string> warnings)
    {
        var pooled = new TestOutcomeIndex();
        var anyResultsFound = false;

        foreach (var profile in profiles)
        {
            var found = TestOutcomeIndex.Read(
                options.RepositoryRoot, profile.ResultsGlob, profile.Format, warnings);

            if (!found.FoundResultFiles)
            {
                // The single-profile wording says pass verification was skipped, which is true then and
                // false when another profile did record results.
                var consequence = profiles.Count > 1
                    ? "tests recorded by this profile cannot be verified"
                    : "pass verification was skipped";

                var message =
                    $"{profile.Label}No test results matching '{profile.ResultsGlob}' - run build.ps1 first; " +
                    consequence;

                (options.Strict ? errors : warnings).Add(message);
                continue;
            }

            anyResultsFound = true;

            // Stale results are worse than absent ones: they report a clause as verified using an outcome
            // recorded before the test was last changed.
            var newestSource = RepositoryFiles
                .UnderRoots(options.RepositoryRoot, profile.Roots, profile.FilePatterns)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();

            if (newestSource is not null && newestSource.LastWriteTimeUtc > found.NewestResultUtc)
                errors.Add(
                    $"{profile.Label}Test results are stale: '{newestSource.Name}' changed after the newest " +
                    $"result matching '{profile.ResultsGlob}'. Re-run the tests.");

            pooled.Merge(found);
        }

        if (!anyResultsFound) return;

        foreach (var clause in clauses)
            foreach (var verifier in clause.Verifiers)
            {
                // Already reported by check 5; saying so twice buries the findings that need separate action.
                if (unresolved.Contains(verifier.Text)) continue;

                var matched = pooled.Matching(verifier.TestName);

                if (matched.Count == 0)
                {
                    errors.Add(
                        $"{clause.DocumentName}: clause {clause.Id} names test '{verifier}' " +
                        "which has no result - it did not run");
                    continue;
                }

                if (matched.FirstOrDefault(outcome => !outcome.IsPass) is { } failed)
                    errors.Add(
                        $"{clause.DocumentName}: clause {clause.Id} names test '{verifier}' " +
                        $"whose most recent result is '{failed.Outcome}'");
            }
    }
}
