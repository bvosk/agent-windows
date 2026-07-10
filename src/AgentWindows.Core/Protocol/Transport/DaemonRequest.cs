using System.Text.Json.Serialization;
using AgentWindows.Core.Protocol.Capture;
using AgentWindows.Core.Protocol.Interaction;
using AgentWindows.Core.Protocol.Lifecycle;
using AgentWindows.Core.Protocol.Windows;

namespace AgentWindows.Core.Protocol.Transport;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "cmd")]
[JsonDerivedType(typeof(ListWindowsRequest), "list")]
[JsonDerivedType(typeof(LaunchRequest), "launch")]
[JsonDerivedType(typeof(AttachRequest), "attach")]
[JsonDerivedType(typeof(SnapshotRequest), "snapshot")]
[JsonDerivedType(typeof(FindRequest), "find")]
[JsonDerivedType(typeof(ActivateRequest), "activate")]
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
