namespace DemaConsulting.Anneal.Toolkit;

/// <remarks>
///     Almost empty of behavior. Everything a caller can reach lives in <see cref="AnnealTool" />, so that the
///     command line and a test exercise the identical path; a process entry point that did work of its own would
///     be the one part of the tool no test could reach.
///     <para>
///         The one thing it must own is the cancellation signal, because a signal has to originate somewhere and
///         this is where a process learns it is being interrupted. Ctrl+C becomes a cancellation of the
///         invocation rather than an abrupt kill, which is what makes the token threaded downward a real one
///         rather than a placeholder nothing ever triggers.
///     </para>
/// </remarks>
internal static class Program
{
    /// <remarks>
    ///     The conventional shell code for a run ended by an interrupt. Deliberately not one of
    ///     <see cref="AnnealTool" />'s codes: those map from an outcome, and an interrupted invocation reached
    ///     none.
    /// </remarks>
    private const int ExitInterrupted = 130;

    private static async Task<int> Main(string[] args)
    {
        using var interrupt = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            interrupt.Cancel();
        };

        try
        {
            return await AnnealTool.RunAsync(args, Console.Out, interrupt.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.Out.WriteLine("anneal: interrupted.");
            return ExitInterrupted;
        }
    }
}
