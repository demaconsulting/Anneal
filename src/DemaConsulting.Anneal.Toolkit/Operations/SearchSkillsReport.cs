using DemaConsulting.Anneal.Toolkit.Skills;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     What a <c>search-skills</c> run found, as data beside its outcome: the ranked matches in the shared skill
///     shape.
/// </summary>
/// <remarks>
///     The full skill bodies travel in the returned matches so a caller consumes the ranking result without
///     re-parsing terminal text. Zero matches is a normal successful answer, not a failure payload.
///     <para>Thread safety: immutable and safe to share.</para>
/// </remarks>
/// <param name="Matches">The ranked skill matches. Must not be null.</param>
public sealed record SearchSkillsReport(IReadOnlyList<Skill> Matches);
