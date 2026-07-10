using System.Text;

namespace AgentWindows.E2E.Tests.Infrastructure;

/// <summary>Continuously drains a reader into a buffer that can be snapshotted mid-read.</summary>
public sealed class DrainingReader
{
    private readonly StringBuilder _buffer = new();
    private readonly Lock _lock = new();

    public DrainingReader(StreamReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _ = DrainAsync(reader);
    }

    public string Snapshot()
    {
        lock (_lock)
        {
            return _buffer.ToString();
        }
    }

    private async Task DrainAsync(StreamReader reader)
    {
        var chunk = new char[4096];
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(chunk);
                if (read <= 0)
                {
                    return;
                }

                lock (_lock)
                {
                    _buffer.Append(chunk, 0, read);
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // The process (and its streams) were disposed; nothing left to drain.
        }
        catch (IOException)
        {
            // Broken pipe on process teardown.
        }
    }
}
