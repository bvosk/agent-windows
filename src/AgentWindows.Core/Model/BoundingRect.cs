using System.Runtime.InteropServices;

namespace AgentWindows.Core.Model;

[StructLayout(LayoutKind.Auto)]
public readonly record struct BoundingRect(int X, int Y, int Width, int Height);
