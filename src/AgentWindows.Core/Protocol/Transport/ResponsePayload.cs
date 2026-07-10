using System.Text.Json.Serialization;
using AgentWindows.Core.Protocol.Capture;
using AgentWindows.Core.Protocol.Lifecycle;
using AgentWindows.Core.Protocol.Windows;

namespace AgentWindows.Core.Protocol.Transport;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(WindowListPayload), "windowList")]
[JsonDerivedType(typeof(WindowPayload), "window")]
[JsonDerivedType(typeof(SnapshotPayload), "snapshot")]
[JsonDerivedType(typeof(ScreenshotPayload), "screenshot")]
[JsonDerivedType(typeof(StatusPayload), "status")]
[JsonDerivedType(typeof(AckPayload), "ack")]
public abstract record ResponsePayload;
