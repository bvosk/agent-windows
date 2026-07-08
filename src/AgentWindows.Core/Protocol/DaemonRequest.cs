using System.Text.Json.Serialization;

namespace AgentWindows.Core.Protocol;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "cmd")]
[JsonDerivedType(typeof(ListWindowsRequest), "list")]
[JsonDerivedType(typeof(LaunchRequest), "launch")]
[JsonDerivedType(typeof(AttachRequest), "attach")]
[JsonDerivedType(typeof(SnapshotRequest), "snapshot")]
[JsonDerivedType(typeof(ClickRequest), "click")]
[JsonDerivedType(typeof(FillRequest), "fill")]
[JsonDerivedType(typeof(PressRequest), "press")]
[JsonDerivedType(typeof(SelectRequest), "select")]
[JsonDerivedType(typeof(ExpandRequest), "expand")]
[JsonDerivedType(typeof(ToggleRequest), "toggle")]
[JsonDerivedType(typeof(ScrollRequest), "scroll")]
[JsonDerivedType(typeof(WaitRequest), "wait")]
[JsonDerivedType(typeof(ScreenshotRequest), "screenshot")]
[JsonDerivedType(typeof(WindowActionRequest), "window")]
[JsonDerivedType(typeof(CloseRequest), "close")]
[JsonDerivedType(typeof(StatusRequest), "status")]
[JsonDerivedType(typeof(ShutdownRequest), "shutdown")]
public abstract record DaemonRequest;
