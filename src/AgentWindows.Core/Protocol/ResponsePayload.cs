using System.Text.Json.Serialization;

namespace AgentWindows.Core.Protocol;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(WindowListPayload), "windowList")]
[JsonDerivedType(typeof(WindowPayload), "window")]
[JsonDerivedType(typeof(SnapshotPayload), "snapshot")]
[JsonDerivedType(typeof(ScreenshotPayload), "screenshot")]
[JsonDerivedType(typeof(StatusPayload), "status")]
[JsonDerivedType(typeof(AckPayload), "ack")]
public abstract record ResponsePayload;
