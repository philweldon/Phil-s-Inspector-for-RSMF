using System.Runtime.InteropServices;

namespace Rsmf.App;

/// <summary>
/// Lets RsmfInspector.exe double as the command-line tool: started with scan, explain, --version or
/// --help it runs the command and exits without opening a window. Output goes to the console the
/// program was started from, or to wherever its output was redirected.
/// </summary>
internal static class CommandLine
{
    private const int ParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    public static bool IsCommand(string[] args) =>
        args.Length > 0 && args[0] is "scan" or "explain" or "--version" or "--help" or "-h" or "help";

    public static int Run(string[] args)
    {
        // A windowed program has no console of its own. This borrows the one it was started from; it
        // does nothing (and is not needed) when the output is redirected to a file or another program.
        AttachConsole(ParentProcess);
        Rsmf.Cli.Program.CommandName = "RsmfInspector";
        int code;
        try
        {
            code = Rsmf.Cli.Program.Run(args, Console.Out, Console.Error);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Unexpected error: " + ex.GetType().Name + ": " + ex.Message);
            code = 2;
        }
        Console.Out.Flush();
        Console.Error.Flush();
        return code;
    }
}
