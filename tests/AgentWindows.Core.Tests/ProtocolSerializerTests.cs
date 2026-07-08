using AgentWindows.Core.Model;
using AgentWindows.Core.Protocol;
using Shouldly;
using Xunit;

namespace AgentWindows.Core.Tests;

public sealed class ProtocolSerializerTests
{
    public static TheoryData<DaemonRequest> AllRequests() =>
        [
            new ListWindowsRequest(),
            new LaunchRequest { Path = "notepad.exe", Arguments = "file.txt" },
            new AttachRequest { Title = "Calc" },
            new SnapshotRequest { InteractiveOnly = true, MaxDepth = 5 },
            new ClickRequest { Ref = "e2", DoubleClick = true },
            new FillRequest { Ref = "e3", Text = "hello" },
            new PressRequest { Keys = "Enter" },
            new SelectRequest { Ref = "e4", Item = "Red" },
            new ExpandRequest { Ref = "e5", Collapse = true },
            new ToggleRequest { Ref = "e6", State = false },
            new ScrollRequest { Direction = ScrollDirection.Down, Amount = 2 },
            new WaitRequest { Text = "Done", Gone = true },
            new ScreenshotRequest { OutputPath = "out.png", Ref = "e7" },
            new WindowActionRequest
            {
                Action = WindowActionKind.Resize,
                Width = 800,
                Height = 600,
            },
            new CloseRequest { Force = true },
            new StatusRequest(),
            new ShutdownRequest(),
        ];

    [Fact]
    public void SerializeRequest_UsesStableWireFormat()
    {
        var json = ProtocolSerializer.SerializeRequest(new PressRequest { Keys = "Ctrl+S" });

        json.ShouldBe("""{"cmd":"press","keys":"Ctrl+S"}""");
    }

    [Fact]
    public void SerializeRequest_EnumsAreCamelCasedStrings()
    {
        var json = ProtocolSerializer.SerializeRequest(
            new ClickRequest { Ref = "e5", Button = MouseButtonKind.Right }
        );

        json.ShouldContain("\"button\":\"right\"");
        json.ShouldContain("\"cmd\":\"click\"");
    }

    [Theory]
    [MemberData(nameof(AllRequests))]
    public void Requests_RoundTripThroughJson(DaemonRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var json = ProtocolSerializer.SerializeRequest(request);
        var back = ProtocolSerializer.DeserializeRequest(json);

        back.ShouldNotBeNull();
        back.GetType().ShouldBe(request.GetType());
        ProtocolSerializer.SerializeRequest(back).ShouldBe(json);
    }

    [Fact]
    public void Responses_RoundTripWithSnapshotPayload()
    {
        var response = DaemonResponse.Success(
            new SnapshotPayload
            {
                Root = new UiNode
                {
                    Role = "window",
                    Name = "Calc",
                    Ref = "e1",
                    States = ["focused"],
                    Bounds = new BoundingRect(0, 0, 800, 600),
                    Children =
                    [
                        new UiNode
                        {
                            Role = "button",
                            Name = "Five",
                            Ref = "e2",
                        },
                    ],
                },
                Generation = 3,
            }
        );

        var json = ProtocolSerializer.SerializeResponse(response);
        var back = ProtocolSerializer.DeserializeResponse(json);

        back.ShouldNotBeNull();
        ProtocolSerializer.SerializeResponse(back).ShouldBe(json);
        back.Ok.ShouldBeTrue();
        var payload = back.Payload.ShouldBeOfType<SnapshotPayload>();
        payload.Generation.ShouldBe(3);
        payload.Root.Children[0].Name.ShouldBe("Five");
    }

    [Fact]
    public void FailureResponse_CarriesErrorCodeAndMessage()
    {
        var json = ProtocolSerializer.SerializeResponse(
            DaemonResponse.Failure("stale-ref", "take a new snapshot")
        );
        var back = ProtocolSerializer.DeserializeResponse(json);

        back.ShouldNotBeNull();
        back.Ok.ShouldBeFalse();
        back.ErrorCode.ShouldBe("stale-ref");
        back.Message.ShouldBe("take a new snapshot");
        back.Payload.ShouldBeNull();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"cmd":"no-such-command"}""")]
    [InlineData("{}")]
    public void DeserializeRequest_ReturnsNullForUnparseableInput(string line) =>
        ProtocolSerializer.DeserializeRequest(line).ShouldBeNull();

    [Fact]
    public void DeserializeResponse_ReturnsNullForUnparseableInput() =>
        ProtocolSerializer.DeserializeResponse("garbage").ShouldBeNull();
}
