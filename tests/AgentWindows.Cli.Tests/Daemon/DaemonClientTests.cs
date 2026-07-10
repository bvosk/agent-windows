using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using AgentWindows.Cli.Daemon;
using AgentWindows.Core.Protocol.Lifecycle;
using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Session;
using Shouldly;
using Xunit;

namespace AgentWindows.Cli.Tests.Daemon;

[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "Each scripted connection transfers ownership to the DaemonClient under test."
)]
public sealed class DaemonClientTests
{
    [Fact]
    public void ExistingConnection_IsReusedAcrossRequestsAndDisposed()
    {
        var connection = new ScriptedConnection(Success("first"), Success("second"));
        var harness = new ClientHarness();
        harness.QueueConnection(connection);
        var client = harness.CreateClient();

        client.Send(new StatusRequest()).Ok.ShouldBeTrue();
        client.Send(new StatusRequest()).Ok.ShouldBeTrue();

        harness.PipeExistsCalls.ShouldBe(1);
        harness.ConnectTimeouts.ShouldBe([250]);
        connection.Writes.Count.ShouldBe(2);
        client.Dispose();
        connection.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public void MissingPipe_WithoutSpawn_ReturnsNotRunning()
    {
        var harness = new ClientHarness { PipeIsPresent = false };
        using var client = harness.CreateClient();

        var response = client.Send(new ShutdownRequest(), spawnIfMissing: false);

        response.Ok.ShouldBeTrue();
        response.Payload.ShouldBeOfType<AckPayload>().Detail.ShouldBe("daemon not running");
        harness.ConnectTimeouts.ShouldBeEmpty();
        harness.StartInfos.ShouldBeEmpty();
    }

    [Fact]
    public void BusyPipe_WithoutSpawn_ReturnsNotRunning()
    {
        var harness = new ClientHarness();
        harness.QueueConnectFailure(new TimeoutException());
        using var client = harness.CreateClient();

        var response = client.Send(new ShutdownRequest(), spawnIfMissing: false);

        response.Ok.ShouldBeTrue();
        response.Payload.ShouldBeOfType<AckPayload>().Detail.ShouldBe("daemon not running");
        harness.ConnectTimeouts.ShouldBe([250]);
        harness.StartInfos.ShouldBeEmpty();
    }

    [Fact]
    public void BusyPipe_RetriesWithoutSpawningDuplicate()
    {
        var connection = new ScriptedConnection(Success("ready"));
        var harness = new ClientHarness();
        harness.QueueConnectFailure(new IOException("busy"));
        harness.QueueConnection(connection);
        using var client = harness.CreateClient();

        var response = client.Send(new StatusRequest());

        response.Ok.ShouldBeTrue();
        harness.ConnectTimeouts.ShouldBe([250, 500]);
        harness.StartInfos.ShouldBeEmpty();
    }

    [Fact]
    public void MissingPipe_SpawnsHiddenDaemonWithoutRedirectedHandleInheritance()
    {
        var process = new TrackingDisposable();
        var connection = new ScriptedConnection(Success("ready"));
        var harness = new ClientHarness
        {
            PipeIsPresent = false,
            ProcessPath = @"C:\tools\agent-windows.exe",
            StartedProcess = process,
        };
        harness.QueueConnection(connection);
        using var client = harness.CreateClient();

        var response = client.Send(new StatusRequest());

        response.Ok.ShouldBeTrue();
        var startInfo = harness.StartInfos.ShouldHaveSingleItem();
        startInfo.FileName.ShouldBe(@"C:\tools\agent-windows.exe");
        startInfo.UseShellExecute.ShouldBeTrue();
        startInfo.WindowStyle.ShouldBe(ProcessWindowStyle.Hidden);
        startInfo.CreateNoWindow.ShouldBeFalse();
        startInfo.RedirectStandardInput.ShouldBeFalse();
        startInfo.RedirectStandardOutput.ShouldBeFalse();
        startInfo.RedirectStandardError.ShouldBeFalse();
        startInfo.ArgumentList.ShouldBe(["daemon", "run", "--session", "test"]);
        process.IsDisposed.ShouldBeTrue();
        harness.ConnectTimeouts.ShouldBe([500]);
    }

    [Fact]
    public void MissingProcessPath_FailsBeforeConnecting()
    {
        var harness = new ClientHarness { PipeIsPresent = false, ProcessPath = null };
        using var client = harness.CreateClient();

        var exception = Should.Throw<AutomationException>(() => client.Send(new StatusRequest()));

        exception.Code.ShouldBe(ErrorCodes.InternalError);
        exception.Message.ShouldContain("executable path");
        harness.ConnectTimeouts.ShouldBeEmpty();
        harness.StartInfos.ShouldBeEmpty();
    }

    [Fact]
    public void MissingPipe_PrefersSiblingManagedDaemonForNativeClient()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aw-native-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var clientPath = Path.Combine(directory, "agent-windows.exe");
        var daemonPath = Path.Combine(directory, "agent-windows-daemon.exe");
        File.WriteAllBytes(daemonPath, []);
        try
        {
            var harness = new ClientHarness { PipeIsPresent = false, ProcessPath = clientPath };
            harness.QueueConnection(new ScriptedConnection(Success("ready")));
            using var client = harness.CreateClient();

            client.Send(new StatusRequest()).Ok.ShouldBeTrue();

            harness.StartInfos.ShouldHaveSingleItem().FileName.ShouldBe(daemonPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SpawnTimeout_FailsAfterRetryDeadline()
    {
        var harness = new ClientHarness { PipeIsPresent = false, StartedProcess = null };
        harness.ElapsedTimes.Enqueue(TimeSpan.Zero);
        harness.ElapsedTimes.Enqueue(TimeSpan.FromSeconds(10));
        harness.QueueConnectFailure(new TimeoutException());
        using var client = harness.CreateClient();

        var exception = Should.Throw<AutomationException>(() => client.Send(new StatusRequest()));

        exception.Code.ShouldBe(ErrorCodes.InternalError);
        exception.Message.ShouldContain("Could not reach");
        harness.ConnectTimeouts.ShouldBe([500]);
        harness.StartInfos.Count.ShouldBe(1);
    }

    [Fact]
    public void EndOfStream_ReconnectsAndRetriesOnce()
    {
        var first = new ScriptedConnection();
        var second = new ScriptedConnection(Success("reconnected"));
        var harness = new ClientHarness();
        harness.QueueConnection(first);
        harness.QueueConnection(second);
        using var client = harness.CreateClient();

        var response = client.Send(new StatusRequest());

        response.Ok.ShouldBeTrue();
        first.IsDisposed.ShouldBeTrue();
        harness.ConnectTimeouts.ShouldBe([250, 250]);
    }

    [Fact]
    public void RepeatedEndOfStream_ReportsLostConnection()
    {
        var harness = new ClientHarness();
        harness.QueueConnection(new ScriptedConnection());
        harness.QueueConnection(new ScriptedConnection());
        using var client = harness.CreateClient();

        var exception = Should.Throw<AutomationException>(() => client.Send(new StatusRequest()));

        exception.Code.ShouldBe(ErrorCodes.InternalError);
        exception.Message.ShouldContain("Lost the connection");
    }

    [Fact]
    public void MalformedResponse_ReportsProtocolFailure()
    {
        var harness = new ClientHarness();
        harness.QueueConnection(new ScriptedConnection("{not-json}"));
        using var client = harness.CreateClient();

        var exception = Should.Throw<AutomationException>(() => client.Send(new StatusRequest()));

        exception.Code.ShouldBe(ErrorCodes.InternalError);
        exception.Message.ShouldContain("unparseable response");
    }

    [Fact]
    public void WriteFailure_ReconnectsThenReportsLostConnection()
    {
        var first = new ScriptedConnection(Success("unused")) { ThrowOnWrite = true };
        var second = new ScriptedConnection(Success("unused")) { ThrowOnWrite = true };
        var harness = new ClientHarness();
        harness.QueueConnection(first);
        harness.QueueConnection(second);
        using var client = harness.CreateClient();

        var exception = Should.Throw<AutomationException>(() => client.Send(new StatusRequest()));

        exception.Message.ShouldContain("Lost the connection");
        first.IsDisposed.ShouldBeTrue();
        second.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public void ReadFailure_ReconnectsAndRetriesOnce()
    {
        var first = new ScriptedConnection { ThrowOnRead = true };
        var second = new ScriptedConnection(Success("reconnected"));
        var harness = new ClientHarness();
        harness.QueueConnection(first);
        harness.QueueConnection(second);
        using var client = harness.CreateClient();

        var response = client.Send(new StatusRequest());

        response.Ok.ShouldBeTrue();
        first.IsDisposed.ShouldBeTrue();
    }

    private static string Success(string detail) =>
        ProtocolSerializer.SerializeResponse(
            DaemonResponse.Success(new AckPayload { Detail = detail })
        );

    private sealed class ClientHarness
    {
        private readonly Queue<Func<IDaemonConnection>> _connections = [];

        public Queue<TimeSpan> ElapsedTimes { get; } = [];

        public List<int> ConnectTimeouts { get; } = [];

        public List<ProcessStartInfo> StartInfos { get; } = [];

        public bool PipeIsPresent { get; init; } = true;

        public string? ProcessPath { get; init; } = "agent-windows.exe";

        public IDisposable? StartedProcess { get; init; } = new TrackingDisposable();

        public int PipeExistsCalls { get; private set; }

        public void QueueConnection(IDaemonConnection connection) =>
            _connections.Enqueue(() => connection);

        public void QueueConnectFailure(Exception exception) =>
            _connections.Enqueue(() => throw exception);

        public DaemonClient CreateClient() =>
            new(
                "test",
                _ =>
                {
                    PipeExistsCalls++;
                    return PipeIsPresent;
                },
                (_, timeoutMs) =>
                {
                    ConnectTimeouts.Add(timeoutMs);
                    return _connections.Dequeue()();
                },
                () => ProcessPath,
                startInfo =>
                {
                    StartInfos.Add(startInfo);
                    return StartedProcess;
                },
                static () => 42,
                _ => ElapsedTimes.Count == 0 ? TimeSpan.Zero : ElapsedTimes.Dequeue()
            );
    }

    private sealed class ScriptedConnection(params string[] lines) : IDaemonConnection
    {
        private readonly Queue<string> _lines = new(lines);

        public List<string> Writes { get; } = [];

        public bool IsDisposed { get; private set; }

        public bool ThrowOnRead { get; init; }

        public bool ThrowOnWrite { get; init; }

        public string? ReadLine()
        {
            if (ThrowOnRead)
            {
                ThrowReadFailure();
            }

            return _lines.Count == 0 ? null : _lines.Dequeue();
        }

        public void WriteLine(string line)
        {
            if (ThrowOnWrite)
            {
                throw new IOException("write failed");
            }

            Writes.Add(line);
        }

        public void Dispose() => IsDisposed = true;

        private static void ThrowReadFailure() => throw new IOException("read failed");
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
