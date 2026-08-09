namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     What a <c>file-skill</c> run concluded, as data beside its outcome: the file it wrote and the skill it
///     authored there.
/// </summary>
/// <remarks>
///     The written skill is carried back as data so a caller can compose on the structured result rather than
///     re-parsing terminal text. A collision or out-of-bounds path carries no report because no skill was filed.
///     <para>Thread safety: immutable and safe to share.</para>
/// </remarks>
/// <param name="Path">The repository-relative path written. Must not be null.</param>
/// <param name="SkillId">The id of the skill that was written. Must not be null.</param>
public sealed record FileSkillReport(string Path, string SkillId);
