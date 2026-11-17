namespace HawkNet.CommandLine;

public interface ICliConsole
{
    void Write(string value);
    void WriteLine(string value = "");
    ValueTask<string?> ReadLineAsync(CancellationToken token = default);
}

public sealed class SystemCliConsole : ICliConsole
{
    public static SystemCliConsole Instance { get; } = new();

    private SystemCliConsole()
    {
    }

    public void Write(string value) => Console.Write(value);

    public void WriteLine(string value = "") => Console.WriteLine(value);

    public ValueTask<string?> ReadLineAsync(CancellationToken token = default) => Console.In.ReadLineAsync(token);
}
