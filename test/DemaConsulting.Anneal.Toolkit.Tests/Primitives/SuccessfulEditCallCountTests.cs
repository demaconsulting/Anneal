using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Model.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Primitives;

/// <summary>
///     Interior tests for <see cref="ModelSession.SuccessfulEditCallCount" />'s counting behavior: only successful
///     edit-tool calls (create, replace, edit, delete) increment the counter; refused calls do not.
/// </summary>
/// <remarks>
///     These tests drive the count through the session's wrapped tools, the same path the real provider's tool loop
///     uses, so they exercise the recording wiring rather than only the property getter.
/// </remarks>
public class SuccessfulEditCallCountTests
{
    [Fact]
    public async Task SuccessfulEditCallCount_SuccessfulCreateFile_CountsOne()
    {
        // Arrange: an endpoint that, on its first call, invokes the create_file tool from the request — the same
        // path the real SDK tool loop takes — then returns a plain reply
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new ToolInvokingEndpoint(
                toolName: "create_file",
                arguments: new Dictionary<string, object?> { ["path"] = "new-file.txt", ["content"] = "hello" },
                runReply: "I created new-file.txt.");

            var roles = new ModelRoles(root, _ => endpoint);
            var session = new ModelSession(
                roles,
                "a charter",
                new ToolGroups(root).SelectTools([ToolGroups.Read, ToolGroups.Edit]));

            // Act
            await session.RunAsync("create a file", role: null, TestContext.Current.CancellationToken);

            // Assert: one successful edit-tool call was recorded
            Assert.Equal(1, session.SuccessfulEditCallCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SuccessfulEditCallCount_RefusedEditCall_DoesNotCount()
    {
        // Arrange: endpoint invokes create_file with a path that escapes the repository — the tool refuses,
        // which must not increment the counter because a refusal is a deliberate non-mutation
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new ToolInvokingEndpoint(
                toolName: "create_file",
                arguments: new Dictionary<string, object?> { ["path"] = "../escape.txt", ["content"] = "oops" },
                runReply: "I tried to escape but was refused.");

            var roles = new ModelRoles(root, _ => endpoint);
            var session = new ModelSession(
                roles,
                "a charter",
                new ToolGroups(root).SelectTools([ToolGroups.Read, ToolGroups.Edit]));

            // Act
            await session.RunAsync("create a file outside the repo", role: null, TestContext.Current.CancellationToken);

            // Assert: the refused call does not count
            Assert.Equal(0, session.SuccessfulEditCallCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SuccessfulEditCallCount_NoEditToolCalled_CountIsZero()
    {
        // Arrange: endpoint returns a plain reply without invoking any tools
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint("I reasoned about the work.");
            var roles = new ModelRoles(root, _ => endpoint);
            var session = new ModelSession(
                roles,
                "a charter",
                new ToolGroups(root).SelectTools([ToolGroups.Read, ToolGroups.Edit]));

            // Act
            await session.RunAsync("think about it", role: null, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(0, session.SuccessfulEditCallCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-edit-count-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    ///     A fake endpoint that invokes a named tool from the request on its first call before returning a canned
    ///     reply, simulating the real SDK's tool loop without a network call.
    /// </summary>
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
                // Simulate the SDK tool loop: find and invoke the named tool before returning the text reply
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
                        // The tool's refusal text is returned to the model, not thrown; the endpoint swallows
                        // the exception and lets the recorder capture the refusal outcome via the wrapped tool.
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
