using System.Diagnostics.CodeAnalysis;
using System.Text;
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
    Justification = "RunLoop assumes ownership of every scripted server returned by its factory."
)]
public sealed class DaemonHostTests
{
    [Fact]
    public void ServeClient_HandlesValidAndInvalidRequestsUntilEndOfInput()
    {
        var status = ProtocolSerializer.SerializeRequest(new StatusRequest());
        using var stream = new DuplexStream($"{status}\n{{not-json}}\n");
        var dispatched = new List<DaemonRequest>();

        var keepRunning = DaemonHost.ServeClient(
            stream,
            request =>
            {
                dispatched.Add(request);
                return DaemonResponse.Success(new AckPayload { Detail = "handled" });
            },
            static () => 21,
            started =>
            {
                started.ShouldBe(21);
                return TimeSpan.FromMilliseconds(12.34);
            }
        );

        keepRunning.ShouldBeTrue();
        dispatched.ShouldHaveSingleItem().ShouldBeOfType<StatusRequest>();
        var responses = DeserializeResponses(stream.Output);
        responses.Length.ShouldBe(2);
        responses[0].Ok.ShouldBeTrue();
        responses[0].ElapsedMs.ShouldBe(12.3);
        responses[1].Ok.ShouldBeFalse();
        responses[1].ErrorCode.ShouldBe(ErrorCodes.BadRequest);
        responses[1].ElapsedMs.ShouldBe(12.3);
    }

    [Fact]
    public void ServeClient_ShutdownRespondsBeforeStopping()
    {
        var shutdown = ProtocolSerializer.SerializeRequest(new ShutdownRequest());
        using var stream = new DuplexStream($"{shutdown}\n");
        DaemonRequest? dispatched = null;

        var keepRunning = DaemonHost.ServeClient(
            stream,
            request =>
            {
                dispatched = request;
                return DaemonResponse.Success(new AckPayload { Detail = "shutting down" });
            },
            static () => 1,
            static _ => TimeSpan.Zero
        );

        keepRunning.ShouldBeFalse();
        dispatched.ShouldBeOfType<ShutdownRequest>();
        DeserializeResponses(stream.Output).ShouldHaveSingleItem().Ok.ShouldBeTrue();
    }

    [Fact]
    public void ServeClient_EndOfInputKeepsDaemonRunning()
    {
        using var stream = new DuplexStream(string.Empty);

        var keepRunning = DaemonHost.ServeClient(
            stream,
            _ => throw new InvalidOperationException("No request should be dispatched."),
            static () => throw new InvalidOperationException("No timestamp should be taken."),
            static _ => throw new InvalidOperationException("No elapsed time should be read.")
        );

        keepRunning.ShouldBeTrue();
        stream.Output.ShouldBeEmpty();
    }

    [Fact]
    public void ServeClient_ReadFailureKeepsDaemonRunning()
    {
        using var stream = new DuplexStream(string.Empty) { ThrowOnRead = true };

        var keepRunning = DaemonHost.ServeClient(
            stream,
            _ => throw new InvalidOperationException("No request should be dispatched."),
            static () => 0,
            static _ => TimeSpan.Zero
        );

        keepRunning.ShouldBeTrue();
    }

    [Fact]
    public void ServeClient_WriteFailureKeepsDaemonRunning()
    {
        var status = ProtocolSerializer.SerializeRequest(new StatusRequest());
        using var stream = new DuplexStream($"{status}\n") { ThrowOnNextWrite = true };

        var keepRunning = DaemonHost.ServeClient(
            stream,
            _ => DaemonResponse.Success(new AckPayload { Detail = "handled" }),
            static () => 0,
            static _ => TimeSpan.Zero
        );

        keepRunning.ShouldBeTrue();
    }

    [Fact]
    public void RunLoop_ContinuesAfterDisconnectThenStopsOnShutdown()
    {
        var disconnected = new DuplexStream(string.Empty);
        var shutdownLine = ProtocolSerializer.SerializeRequest(new ShutdownRequest());
        var shutdown = new DuplexStream($"{shutdownLine}\n");
        var servers = new Queue<Stream>([disconnected, shutdown]);
        var waits = 0;

        var exitCode = DaemonHost.RunLoop(
            "agent-windows-test",
            name =>
            {
                name.ShouldBe("agent-windows-test");
                return servers.Dequeue();
            },
            _ => waits++,
            _ => DaemonResponse.Success(new AckPayload { Detail = "ok" }),
            static () => 0,
            static _ => TimeSpan.Zero
        );

        exitCode.ShouldBe(0);
        waits.ShouldBe(2);
        disconnected.IsDisposed.ShouldBeTrue();
        shutdown.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public void RunLoop_DuplicateDaemonExitsWithoutWaiting()
    {
        var waited = false;

        var exitCode = DaemonHost.RunLoop(
            "agent-windows-test",
            _ => null,
            _ => waited = true,
            _ => throw new InvalidOperationException("No request should be dispatched."),
            static () => 0,
            static _ => TimeSpan.Zero
        );

        exitCode.ShouldBe(0);
        waited.ShouldBeFalse();
    }

    [Fact]
    public void CreateServer_ReturnsCreatedStream()
    {
        var expected = new MemoryStream();

        using var actual = DaemonHost.CreateServer(
            "pipe-name",
            name =>
            {
                name.ShouldBe("pipe-name");
                return expected;
            }
        );

        actual.ShouldBeSameAs(expected);
    }

    [Fact]
    public void CreateServer_IoFailureMeansAnotherDaemonOwnsPipe()
    {
        using var actual = DaemonHost.CreateServer(
            "pipe-name",
            _ => throw new IOException("already owned")
        );

        actual.ShouldBeNull();
    }

    private static DaemonResponse[] DeserializeResponses(string output) =>
        [
            .. output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => ProtocolSerializer.DeserializeResponse(line)!),
        ];

    private sealed class DuplexStream(string input) : Stream
    {
        private readonly MemoryStream _input = new(Encoding.UTF8.GetBytes(input));
        private readonly MemoryStream _output = new();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public string Output => Encoding.UTF8.GetString(_output.ToArray());

        public bool IsDisposed { get; private set; }

        public bool ThrowOnRead { get; init; }

        public bool ThrowOnNextWrite { get; set; }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (ThrowOnRead)
            {
                ThrowReadFailure();
            }

            return _input.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (ThrowOnNextWrite)
            {
                ThrowOnNextWrite = false;
                throw new IOException("write failed");
            }

            _output.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _input.Dispose();
                _output.Dispose();
            }

            IsDisposed = true;
            base.Dispose(disposing);
        }

        private static void ThrowReadFailure() => throw new IOException("read failed");
    }
}
