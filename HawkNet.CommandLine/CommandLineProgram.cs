namespace HawkNet.CommandLine;

public static class CommandLineProgram
{
    public static async Task<int> RunAsync(
        string[] args,
        IEnumerable<IHawkNetCliExtension>? extensions = null,
        string applicationName = "hawknet",
        ICliConsole? console = null,
        CancellationToken token = default)
    {
        console ??= SystemCliConsole.Instance;
        try
        {
            string? mode = args.FirstOrDefault()?.ToLowerInvariant();
            string[] commandArgs = args.Skip(1).ToArray();
            if (commandArgs.Length == 1 && commandArgs[0] is "-h" or "--help")
            {
                return PrintUsage(console, applicationName);
            }
            return mode switch
            {
                "server" or "headless-server" => await ServerCliRunner.RunAsync(commandArgs, extensions, console, token),
                "client" or "headless-client" => await ClientCliRunner.RunAsync(commandArgs, extensions, console, token),
                "-h" or "--help" or "help" or null => PrintUsage(console, applicationName),
                _ => UnknownCommand(mode, console, applicationName)
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            console.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static int PrintUsage(ICliConsole console, string applicationName)
    {
        console.WriteLine("Usage:");
        console.WriteLine($"  {applicationName} server [--config path] [--bind address] [--port number] [--protocol tcp|udp] [UDP options]");
        console.WriteLine($"  {applicationName} client [--config path] [--host address] [--port number] [--protocol tcp|udp] [UDP options]");
        console.WriteLine("UDP options:");
        console.WriteLine("  --udp-max-datagram bytes  --udp-window count  --udp-reorder count");
        console.WriteLine("  --udp-rto-initial ms      --udp-rto-min ms    --udp-rto-max ms");
        console.WriteLine("  --udp-retries count       --udp-ack-delay ms");
        console.WriteLine("Environment prefixes: HAWKNET_SERVER_UDP_* and HAWKNET_CLIENT_UDP_*.");
        return 0;
    }

    private static int UnknownCommand(string mode, ICliConsole console, string applicationName)
    {
        console.WriteLine($"Unknown command '{mode}'.");
        PrintUsage(console, applicationName);
        return 1;
    }
}
