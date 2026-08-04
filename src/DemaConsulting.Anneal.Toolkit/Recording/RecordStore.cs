using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DemaConsulting.Anneal.Toolkit.Recording;

/// <summary>
///     Where a repository's invocation records and model transcripts are appended.
/// </summary>
/// <remarks>
///     Two streams, not one. The invocation record answers "what ran, and what did it decide"; the transcript
///     answers "what exactly was asked of a model, and what came back". Serving both from one sink would tie
///     each one's shape to the other's, and a third stream — the tool's own diagnostic trace — is deliberately
///     outside both because its whole value is being free to change.
///     <para>
///         Both live under <c>.anneal/</c> beside the role configuration, in directories a repository ignores,
///         because both carry repository source: a transcript quotes the files a model was shown, and an
///         invocation record carries the arguments it was given. Committing either by accident is the failure
///         this placement removes.
///     </para>
///     <para>
///         A write that cannot happen is swallowed rather than raised. The alternative is a read-only checkout
///         in which no operation runs at all, which trades a check that works for bookkeeping that does not —
///         and bookkeeping is never worth failing an answer the caller asked for.
///     </para>
///     <para>
///         Thread safety: safe to share and to call concurrently; appends are serialized within the process.
///     </para>
/// </remarks>
public sealed class RecordStore
{
    /// <summary>
    ///     Where invocation records are appended, relative to a repository root.
    /// </summary>
    public const string InvocationsRelativePath = ".anneal/records/invocations.jsonl";

    /// <summary>
    ///     Where model transcripts are appended, relative to a repository root.
    /// </summary>
    public const string TranscriptsRelativePath = ".anneal/transcripts/model-interactions.jsonl";

    /// <remarks>
    ///     One line per record, so an appender never rewrites what is already there and a reader can consume
    ///     the file while it grows. Indented JSON would be friendlier to read and would make every record a
    ///     multi-line edit of a shared document, which is how an interrupted run corrupts the ones before it.
    /// </remarks>
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly Lock Gate = new();

    private readonly string _root;

    /// <summary>
    ///     Opens the store for a repository. Nothing is created until something is appended.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository the records belong to. Must not be null or blank; it need not exist yet.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public RecordStore(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        _root = Path.GetFullPath(repositoryRoot);
    }

    /// <summary>
    ///     Resolves where invocation records are appended for a repository.
    /// </summary>
    /// <param name="repositoryRoot">The repository root. Must not be null or blank.</param>
    /// <returns>The absolute path of the invocation record file.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public static string InvocationsPathFor(string repositoryRoot) =>
        Resolve(repositoryRoot, InvocationsRelativePath);

    /// <summary>
    ///     Resolves where model transcripts are appended for a repository.
    /// </summary>
    /// <param name="repositoryRoot">The repository root. Must not be null or blank.</param>
    /// <returns>The absolute path of the transcript file.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public static string TranscriptsPathFor(string repositoryRoot) =>
        Resolve(repositoryRoot, TranscriptsRelativePath);

    /// <summary>
    ///     Appends one invocation record.
    /// </summary>
    /// <param name="record">The record to append. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is null.</exception>
    public void Append(InvocationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Write(InvocationsPathFor(_root), record);
    }

    /// <summary>
    ///     Appends one model transcript.
    /// </summary>
    /// <param name="transcript">The transcript to append. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transcript" /> is null.</exception>
    public void Append(ModelTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        Write(TranscriptsPathFor(_root), transcript);
    }

    private static string Resolve(string repositoryRoot, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        return Path.Combine(
            Path.GetFullPath(repositoryRoot),
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void Write<T>(string path, T record)
    {
        var line = JsonSerializer.Serialize(record, WriteOptions) + Environment.NewLine;

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Deliberately silent. Recording is evidence about work, never the work itself, so a checkout that
            // cannot be written to loses the evidence rather than the answer.
        }
    }
}
