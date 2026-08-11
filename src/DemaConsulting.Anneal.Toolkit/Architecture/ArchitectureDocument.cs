using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DemaConsulting.Anneal.Toolkit.Architecture;

/// <summary>
///     One document of the architecture tree, read for the contract it declares.
/// </summary>
/// <remarks>
///     Reading is structural rather than line-based: the document is parsed as CommonMark and the contract is
///     taken from the resulting blocks. Line matching cannot tell a live clause from an illustrative one
///     inside a fenced example, and cannot follow a clause body that wraps across lines — both of which occur
///     in this repository's own tree, and both of which silently mis-report a contract.
///     <para>
///         Reading fails closed. A bolded list item under a clause subsection whose identifier does not parse
///         is reported as malformed rather than skipped, and a system document that declares no contract at
///         all is visible as such. An item this reader cannot understand would otherwise vanish from a report
///         while the run still claimed success, which is worse than no report.
///     </para>
///     <para>Thread safety: immutable and safe to share once read.</para>
/// </remarks>
public sealed partial class ArchitectureDocument
{
    /// <summary>
    ///     Front matter is parsed as front matter rather than left to be read as a thematic break followed by
    ///     prose, so that a document's <c>level:</c> and <c>covers:</c> block can never contribute a block
    ///     that looks like contract content.
    /// </summary>
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseYamlFrontMatter().Build();

    /// <summary>
    ///     Contract subsections whose bolded list items are clauses. <c>Requires</c> is excluded: its entries
    ///     name depended-upon behavior belonging to another system and legitimately carry no identifier.
    /// </summary>
    private static readonly string[] ClauseSections = ["Provides", "Invariants"];

    private ArchitectureDocument(
        string name,
        bool isSystemDocument,
        bool declaresContract,
        IReadOnlyList<ContractClause> clauses,
        IReadOnlyList<MalformedClause> malformedClauses)
    {
        Name = name;
        IsSystemDocument = isSystemDocument;
        DeclaresContract = declaresContract;
        Clauses = clauses;
        MalformedClauses = malformedClauses;
    }

    /// <summary>
    ///     The document's file name, which is how every message about the tree identifies it.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Whether this is a level 2 system document, which owns a contract, rather than a level 3 subsystem
    ///     document, which elaborates one system's interior and owns no contract of its own.
    /// </summary>
    public bool IsSystemDocument { get; }

    /// <summary>
    ///     Whether the document declares a <c>## Contract</c> section at all.
    /// </summary>
    public bool DeclaresContract { get; }

    /// <summary>
    ///     The clauses declared, in the order written.
    /// </summary>
    public IReadOnlyList<ContractClause> Clauses { get; }

    /// <summary>
    ///     Bolded items under a clause subsection whose identifier did not parse, in the order written. These
    ///     are reported rather than skipped: an unresolved template placeholder left in a tree is a clause
    ///     nobody is checking.
    /// </summary>
    public IReadOnlyList<MalformedClause> MalformedClauses { get; }

    /// <summary>
    ///     Reads a document's contract from its Markdown source.
    /// </summary>
    /// <param name="name">The document's file name, used in messages about what was found. Must not be null.</param>
    /// <param name="markdown">The document's Markdown source. Must not be null.</param>
    /// <param name="isSystemDocument">
    ///     True for a level 2 system document, false for a level 3 subsystem document. Only a system document
    ///     is expected to declare a contract; the flag is recorded rather than acted on here so the caller
    ///     decides what an absent contract means.
    /// </param>
    /// <returns>The document as read, whether or not it declared anything.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="name" /> or <paramref name="markdown" /> is null.
    /// </exception>
    public static ArchitectureDocument Read(string name, string markdown, bool isSystemDocument)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(markdown);

        var reading = new Reading(name);
        Walk(Markdown.Parse(markdown, Pipeline), reading);

        return new ArchitectureDocument(
            name, isSystemDocument, reading.DeclaresContract, reading.Clauses, reading.Malformed);
    }

    /// <remarks>
    ///     Descends into every container so that a clause nested inside another list, or inside a quote, is
    ///     still found; a clause the reader walked past would be a promise nobody checks. Fenced code is a
    ///     leaf block and is therefore never descended into, which is what keeps the illustrative clauses in
    ///     this repository's own standards out of the live contract.
    /// </remarks>
    private static void Walk(ContainerBlock container, Reading reading)
    {
        foreach (var block in container)
            switch (block)
            {
                case HeadingBlock { Level: 2 } heading:
                    // A level 2 heading other than Contract closes the contract block.
                    reading.InContract = string.Equals(
                        InlineText(heading.Inline), "Contract", StringComparison.OrdinalIgnoreCase);
                    reading.DeclaresContract |= reading.InContract;
                    reading.Section = string.Empty;
                    break;

                case HeadingBlock { Level: 3 } heading:
                    reading.Section = InlineText(heading.Inline);
                    break;

                case ListBlock list:
                    foreach (var item in list.OfType<ListItemBlock>())
                    {
                        ReadItem(item, reading);
                        Walk(item, reading);
                    }

                    break;

                case ContainerBlock nested:
                    Walk(nested, reading);
                    break;
            }
    }

    /// <remarks>
    ///     Only the item's own paragraphs are read. A nested list inside it is reached by the walk as an item
    ///     in its own right, so its content cannot be mistaken for part of the clause containing it.
    /// </remarks>
    private static void ReadItem(ListItemBlock item, Reading reading)
    {
        if (!reading.InContract) return;

        var paragraphs = item.OfType<ParagraphBlock>().ToList();
        if (paragraphs.Count == 0) return;

        // A clause opens with its identifier in bold. Anything else in this position is prose.
        if (paragraphs[0].Inline?.FirstChild is not EmphasisInline { DelimiterCount: 2, DelimiterChar: '*' } bold)
            return;

        if (!ClauseSections.Contains(reading.Section, StringComparer.OrdinalIgnoreCase)) return;

        var id = InlineText(bold).Trim();
        if (!ClauseId().IsMatch(id))
        {
            reading.Malformed.Add(new MalformedClause(id, reading.Section));
            return;
        }

        reading.Clauses.Add(new ContractClause(id, reading.Section, reading.Name, ReadVerifiers(paragraphs)));
    }

    /// <remarks>
    ///     Verifiers are the code spans following a <c>*Verified by:*</c> marker, up to the end of the line
    ///     that carries it. Bounding the collection at the line end is what stops a later sentence's inline
    ///     code — a file name, a command — from being read as a test the clause promised.
    /// </remarks>
    private static IReadOnlyList<ContractVerifier> ReadVerifiers(IEnumerable<ParagraphBlock> paragraphs)
    {
        var verifiers = new List<ContractVerifier>();

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Inline is null) continue;

            var collecting = false;
            foreach (var inline in paragraph.Inline)
                switch (inline)
                {
                    case EmphasisInline { DelimiterCount: 1, DelimiterChar: '*' } marker:
                        collecting = string.Equals(
                            InlineText(marker).Trim(), "Verified by:", StringComparison.OrdinalIgnoreCase);
                        break;

                    case CodeInline code when collecting:
                        verifiers.Add(new ContractVerifier(code.Content.Trim()));
                        break;

                    case LineBreakInline:
                        collecting = false;
                        break;
                }
        }

        return verifiers;
    }

    /// <remarks>
    ///     Code spans contribute their content because a clause identifier or a heading may legitimately hold
    ///     one, and the reader wants the text a person sees rather than the markup that produced it.
    /// </remarks>
    private static string InlineText(ContainerInline? inline)
    {
        if (inline is null) return string.Empty;

        var text = new StringBuilder();
        foreach (var descendant in inline.Descendants())
            switch (descendant)
            {
                case CodeInline code:
                    text.Append(code.Content);
                    break;
                case LiteralInline literal:
                    text.Append(literal.Content.AsSpan());
                    break;
            }

        return text.ToString();
    }

    [GeneratedRegex(
        @"^[A-Za-z][A-Za-z0-9]*(-[A-Za-z][A-Za-z0-9]*)*-I?\d+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClauseId();

    /// <remarks>
    ///     The heading state a reader carries down the document. Mutable and private because it exists only
    ///     for the duration of one read; nothing outside this type ever sees a half-read document.
    /// </remarks>
    private sealed class Reading(string name)
    {
        public string Name { get; } = name;

        public bool InContract { get; set; }

        public bool DeclaresContract { get; set; }

        public string Section { get; set; } = string.Empty;

        public List<ContractClause> Clauses { get; } = [];

        public List<MalformedClause> Malformed { get; } = [];
    }
}

/// <summary>
///     A bolded item found where a clause belongs, whose identifier does not parse as one.
/// </summary>
/// <param name="Label">The text found in the identifier position, for a message that names what was seen.</param>
/// <param name="Section">The contract subsection it was found under.</param>
public sealed record MalformedClause(string Label, string Section);
