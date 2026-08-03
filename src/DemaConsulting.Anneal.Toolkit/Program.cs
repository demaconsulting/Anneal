namespace DemaConsulting.Anneal.Toolkit;

/// <remarks>
///     Deliberately empty of behavior. Everything a caller can reach lives in <see cref="AnnealTool" />, so
///     that the command line and a test exercise the identical path; a process entry point that did any work
///     of its own would be the one part of the tool no test could reach.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args) => AnnealTool.Run(args, Console.Out);
}
