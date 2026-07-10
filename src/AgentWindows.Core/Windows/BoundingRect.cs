using System.Runtime.InteropServices;

namespace AgentWindows.Core.Windows;

[StructLayout(LayoutKind.Auto)]
public readonly record struct BoundingRect(int X, int Y, int Width, int Height);
