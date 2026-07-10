using AgentWindows.Core.Model;
using AgentWindows.Core.Protocol;
using AgentWindows.Core.Session;
using Shouldly;
using Xunit;

namespace AgentWindows.Cli.Tests;

public sealed class OutputRendererTests
{
    private static readonly WindowInfo _window = new()
    {
        Title = "Untitled - Notepad",
        WindowHandle = 0xABC,
        ProcessId = 1234,
        ProcessName = "notepad",
    };

    [Fact]
    public void Render_TextSuccess_WritesPayloadAndReturnsZero()
    {
        var response = DaemonResponse.Success(new AckPayload { Detail = "clicked" });
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = OutputRenderer.Render(response, json: false, output, error);

        exitCode.ShouldBe(0);
        output.ToString().ShouldBe("ok: clicked\n");
        error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Render_TextFailure_WritesToErrorAndReturnsOne()
    {
        var response = DaemonResponse.Failure(ErrorCodes.StaleRef, "take a new snapshot");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = OutputRenderer.Render(response, json: false, output, error);

        exitCode.ShouldBe(1);
        output.ToString().ShouldBeEmpty();
        error.ToString().ShouldContain("error [stale-ref]: take a new snapshot");
    }

    [Fact]
    public void Render_JsonMode_WritesEnvelopeEvenOnFailure()
    {
        var response = DaemonResponse.Failure(ErrorCodes.Timeout, "too slow");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = OutputRenderer.Render(response, json: true, output, error);

        exitCode.ShouldBe(1);
        error.ToString().ShouldBeEmpty();
        var line = output.ToString().TrimEnd();
        line.ShouldContain("\"ok\":false");
        line.ShouldContain("\"errorCode\":\"timeout\"");
        ProtocolSerializer.DeserializeResponse(line).ShouldNotBeNull();
    }

    [Fact]
    public void RenderPayload_WindowList()
    {
        var payload = new WindowListPayload { Windows = [_window] };

        OutputRenderer
            .RenderPayload(payload)
            .ShouldBe("\"Untitled - Notepad\" pid=1234 process=notepad hwnd=0xABC\n");
    }

    [Fact]
    public void RenderPayload_EmptyWindowList()
    {
        var payload = new WindowListPayload { Windows = [] };

        OutputRenderer.RenderPayload(payload).ShouldBe("no top-level windows found\n");
    }

    [Fact]
    public void RenderPayload_ElevatedWindowIsMarked()
    {
        var payload = new WindowPayload { Window = _window with { IsElevated = true } };

        OutputRenderer.RenderPayload(payload).ShouldContain(" elevated");
    }

    [Fact]
    public void RenderPayload_Snapshot_UsesTreeFormatter()
    {
        var payload = new SnapshotPayload
        {
            Root = new UiNode
            {
                Role = "window",
                Name = "Calc",
                Ref = "e1",
            },
            Generation = 1,
        };

        OutputRenderer.RenderPayload(payload).ShouldBe("- window \"Calc\" [@e1]\n");
    }

    [Fact]
    public void RenderPayload_Screenshot()
    {
        var payload = new ScreenshotPayload { Path = @"C:\shots\out.png" };

        OutputRenderer.RenderPayload(payload).ShouldBe("saved: C:\\shots\\out.png\n");
    }

    [Fact]
    public void RenderPayload_Status()
    {
        var payload = new StatusPayload
        {
            Status = new SessionStatus
            {
                DaemonProcessId = 77,
                Target = _window,
                SnapshotGeneration = 2,
                RefCount = 5,
            },
        };

        var text = OutputRenderer.RenderPayload(payload);

        text.ShouldContain("daemon: pid 77");
        text.ShouldContain("snapshot generation: 2 (5 refs)");
        text.ShouldContain("notepad");
    }

    [Fact]
    public void RenderPayload_StatusWithoutTarget_UsesNone()
    {
        var payload = new StatusPayload
        {
            Status = new SessionStatus
            {
                DaemonProcessId = 77,
                SnapshotGeneration = 0,
                RefCount = 0,
            },
        };

        OutputRenderer.RenderPayload(payload).ShouldContain("target: none");
    }

    [Fact]
    public void RenderPayload_AckWithoutDetail_UsesDone() =>
        OutputRenderer.RenderPayload(new AckPayload()).ShouldBe("ok: done\n");

    [Fact]
    public void RenderPayload_NullPayload_IsPlainOk() =>
        OutputRenderer.RenderPayload(null).ShouldBe("ok\n");
}
