using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;

namespace AgentWindows.Cli;

internal sealed class NamedPipeConnection : IDaemonConnection
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;

    private NamedPipeConnection(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        _reader = new StreamReader(pipe, leaveOpen: true);
        _writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
    }

    public string? ReadLine() => _reader.ReadLine();

    public void WriteLine(string line) => _writer.WriteLine(line);

    public void Dispose()
    {
        _writer.Dispose();
        _reader.Dispose();
        _pipe.Dispose();
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership of the connected pipe transfers to the returned connection."
    )]
    internal static IDaemonConnection Connect(string pipeName, int timeoutMs)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        try
        {
            pipe.Connect(timeoutMs);
            return new NamedPipeConnection(pipe);
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }
}
