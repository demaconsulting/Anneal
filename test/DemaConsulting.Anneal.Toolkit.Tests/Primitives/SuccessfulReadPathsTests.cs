using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Model.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Primitives;

/// <summary>
///     Interior tests for <see cref="ModelSession.SuccessfulReadPaths" />: only successful read-tool calls
///     (read_file, list_files, search_files) contribute path entries; refused and faulted calls do not.
/// </summary>
public class SuccessfulReadPathsTests
{
    [Fact]
    public async Task SuccessfulReadPaths_SuccessfulReadFile_CapturesPath()
    {
        // Arrange: create a real file so the read_file tool succeeds, then let an endpoint invoke it
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "readme.md"), "hello");

            var endpoint = new ToolInvokingEndpoint(
                toolName: "read_file",
                arguments: new Dictionary<string, object?> { ["path"] = "readme.md", ["start"] = 0, ["max"] = 0 },
                runReply: "I read readme.md.");

            var roles = new ModelRoles(root, _ => endpoint);
            var session = new ModelSession(roles, "a charter", new ToolGroups(root).SelectTools([ToolGroups.Read]));

            // Act
            await session.RunAsync("read a file", role: null, TestContext.Current.CancellationToken);

            // Assert: the path is captured
            Assert.Contains("readme.md", session.SuccessfulReadPaths, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SuccessfulReadPaths_RefusedReadCall_DoesNotCapturePath()
    {
        // Arrange: read_file with a path that escapes the repository — the tool refuses
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new ToolInvokingEndpoint(
                toolName: "read_file",
                arguments: new Dictionary<string, object?> { ["path"] = "../escape.txt", ["start"] = 0, ["max"] = 0 },
                runReply: "I tried to read outside.");

            var roles = new ModelRoles(root, _ => endpoint);
            var session = new ModelSession(roles, "a charter", new ToolGroups(root).SelectTools([ToolGroups.Read]));

            // Act
            await session.RunAsync("try to read outside", role: null, TestContext.Current.CancellationToken);

            // Assert: the refused path is not captured
            Assert.Empty(session.SuccessfulReadPaths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SuccessfulReadPaths_MultipleSuccessfulReads_DeduplicatesCaseInsensitively()
    {
        // Arrange: two reads of the same path with different capitalization
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "readme.md"), "hello");

            // Invoke read_file twice: once with lowercase, once with uppercase path
            var endpoint = new MultiToolInvokingEndpoint(
                [
                    ("read_file", new Dictionary<string, object?> { ["path"] = "readme.md", ["start"] = 0, ["max"] = 0 }),
                    ("read_file", new Dictionary<string, object?> { ["path"] = "README.MD", ["start"] = 0, ["max"] = 0 })
                ],
                runReply: "I read it twice.");

            var roles = new ModelRoles(root, _ => endpoint);
            var session = new ModelSession(roles, "a charter", new ToolGroups(root).SelectTools([ToolGroups.Read]));

            // Act
            await session.RunAsync("read the same file twice", role: null, TestContext.Current.CancellationToken);

            // Assert: deduplicated to one entry
            Assert.Single(session.SuccessfulReadPaths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SuccessfulReadPaths_NoReadToolCalled_IsEmpty()
    {
        // Arrange: endpoint returns a plain reply without invoking any tools
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint("I reasoned without reading.");
            var roles = new ModelRoles(root, _ => endpoint);
            var session = new ModelSession(roles, "a charter", new ToolGroups(root).SelectTools([ToolGroups.Read]));

            // Act
            await session.RunAsync("think about it", role: null, TestContext.Current.CancellationToken);

            // Assert
            Assert.Empty(session.SuccessfulReadPaths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-read-paths-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class ToolInvokingEndpoint(
        string toolName,
        Dictionary<string, object?> arguments,
        string runReply) : IChatEndpoint
    {
        private int _calls;

        public async Task<ChatTurnResult> CompleteAsync(ChatTurnRequest request, CancellationToken cancellationToken)
        {
            _calls++;

            if (_calls == 1)
            {
                var tool = request.Tools.OfType<AIFunction>().FirstOrDefault(t => t.Name == toolName);
                if (tool is not null)
                {
                    var args = new AIFunctionArguments(arguments);
                    try
                    {
                        await tool.InvokeAsync(args, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        // refusals are returned as text; swallow to let the recorder capture the outcome
                    }
                }

                return new ChatTurnResult(runReply);
            }

            throw new ModelUnavailableException("no further replies queued");
        }

        public Task<IReadOnlyCollection<string>> AvailableModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);
    }

    private sealed class MultiToolInvokingEndpoint(
        IReadOnlyList<(string ToolName, Dictionary<string, object?> Arguments)> invocations,
        string runReply) : IChatEndpoint
    {
        private int _calls;

        public async Task<ChatTurnResult> CompleteAsync(ChatTurnRequest request, CancellationToken cancellationToken)
        {
            _calls++;

            if (_calls == 1)
            {
                foreach (var (toolName, arguments) in invocations)
                {
                    var tool = request.Tools.OfType<AIFunction>().FirstOrDefault(t => t.Name == toolName);
                    if (tool is not null)
                    {
                        var args = new AIFunctionArguments(arguments);
                        try
                        {
                            await tool.InvokeAsync(args, cancellationToken).ConfigureAwait(false);
                        }
                        catch
                        {
                            // swallow refusals
                        }
                    }
                }

                return new ChatTurnResult(runReply);
            }

            throw new ModelUnavailableException("no further replies queued");
        }

        public Task<IReadOnlyCollection<string>> AvailableModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);
    }
}
