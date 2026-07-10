using System.Drawing;
using AgentWindows.Core.Windows;

namespace AgentWindows.Automation.Windows;

public static class RectConversions
{
    public static BoundingRect? ToBoundingRect(Rectangle rect) =>
        rect.IsEmpty ? null : new BoundingRect(rect.X, rect.Y, rect.Width, rect.Height);

    public static Point Center(Rectangle rect) =>
        new(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
}
