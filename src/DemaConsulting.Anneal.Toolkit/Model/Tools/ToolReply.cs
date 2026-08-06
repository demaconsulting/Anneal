namespace DemaConsulting.Anneal.Toolkit.Model.Tools;

/// <summary>
///     How a tool answers a model, and how that answer is classified for the transcript.
/// </summary>
/// <remarks>
///     A tool never throws at a model. Every outcome — the work done, the file that was not there, the path that
///     was refused — comes back as text the model can read and act on, because an exception crossing the
///     provider's tool loop is an opaque failure the model cannot repair. What distinguishes the outcomes is
///     therefore a prefix on that text rather than a type, and this is where the prefix is defined once so the
///     tools and the transcription of them cannot disagree about what a refusal looks like.
///     <para>Thread safety: stateless and safe to call concurrently.</para>
/// </remarks>
public static class ToolReply
{
    /// <summary>
    ///     The prefix every refusal carries: the model was denied, rather than having asked for something that
    ///     is simply not there.
    /// </summary>
    public const string RefusedPrefix = "refused: ";

    /// <summary>
    ///     The prefix a refusal carries when what was refused is a write to a protected configuration file or
    ///     repository script.
    /// </summary>
    /// <remarks>
    ///     A distinct prefix because the two refusals mean different things to the process driving the
    ///     conversation. "You asked for a path outside the repository" is a worker mistake it should correct;
    ///     "this file needs the user's approval" is a decision only the user can make, and is the one an
    ///     operation escalates on. Reading them as one refusal would let a stray out-of-bounds request be
    ///     reported to the user as a protected file needing their approval, which would simply be untrue.
    /// </remarks>
    public const string ProtectedPrefix = RefusedPrefix + "protected path - ";

    /// <summary>
    ///     The transcribed classification of a tool call the tool carried out.
    /// </summary>
    public const string Returned = "Returned";

    /// <summary>
    ///     The transcribed classification of a tool call the tool refused because the path escapes the
    ///     repository root.
    /// </summary>
    public const string Refused = "Refused";

    /// <summary>
    ///     The transcribed classification of a tool call the tool refused because it would have written a
    ///     protected configuration file or repository script.
    /// </summary>
    /// <remarks>
    ///     Kept apart from <see cref="Refused" /> so an operation can tell the refusal that means "the user must
    ///     decide" from the one that means "the worker asked for the wrong thing". Both are refusals and both are
    ///     transcribed; only this one is grounds for escalation.
    /// </remarks>
    public const string RefusedProtected = "RefusedProtected";

    /// <summary>
    ///     The transcribed classification of a tool call that threw before it could answer.
    /// </summary>
    /// <remarks>
    ///     Distinct from <see cref="Refused" /> deliberately: a refusal is the Toolkit working as designed and
    ///     is what an operation escalates on, while a fault is the Toolkit failing. Recording both as one would
    ///     make the escalation signal unreadable.
    /// </remarks>
    public const string Faulted = "Faulted";

    /// <summary>
    ///     Builds a refusal for a path that escapes the repository root.
    /// </summary>
    /// <param name="tool">The tool that refused. Must not be null or blank.</param>
    /// <param name="path">The path the model asked for, echoed back so the model can see what it sent.</param>
    /// <returns>The refusal text.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="tool" /> is null, empty or blank.</exception>
    public static string OutsideRepository(string tool, string? path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);

        return RefusedPrefix +
            $"{tool} will not touch '{path}': it does not resolve to a path inside the repository. " +
            "Use a path relative to the repository root.";
    }

    /// <summary>
    ///     Classifies what a tool returned, for the transcript.
    /// </summary>
    /// <param name="reply">What the tool returned to the model, or null when it returned nothing.</param>
    /// <returns>
    ///     <see cref="RefusedProtected" /> when the reply refuses a protected path, <see cref="Refused" /> when
    ///     it is any other refusal, and <see cref="Returned" /> otherwise.
    /// </returns>
    public static string Classify(string? reply) =>
        reply is null ? Returned :
        reply.StartsWith(ProtectedPrefix, StringComparison.Ordinal) ? RefusedProtected :
        reply.StartsWith(RefusedPrefix, StringComparison.Ordinal) ? Refused :
        Returned;

    /// <summary>
    ///     States whether a transcribed classification is one of the refusals.
    /// </summary>
    /// <param name="classification">A classification this type defines, or anything else.</param>
    /// <returns>True for either refusal classification.</returns>
    public static bool IsRefusal(string? classification) =>
        classification is Refused or RefusedProtected;
}
