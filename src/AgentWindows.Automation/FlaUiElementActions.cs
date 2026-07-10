using System.Runtime.InteropServices;
using AgentWindows.Core.Input;
using AgentWindows.Core.Model;
using AgentWindows.Core.Session;
using AgentWindows.Core.Snapshot;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Patterns;
using FlaUI.Core.WindowsAPI;

namespace AgentWindows.Automation;

internal static class FlaUiElementActions
{
    internal static void ClickAt(int x, int y, MouseButtonKind button, bool doubleClick)
    {
        Mouse.Position = new System.Drawing.Point(x, y);
        ClickCurrentPosition(ToMouseButton(button), doubleClick);
    }

    internal static void Click(
        AutomationElement element,
        string elementRef,
        MouseButtonKind button,
        bool doubleClick,
        TimeSpan timeout
    )
    {
        WaitUntilActionable(element, elementRef, timeout);
        Mouse.Position = GetClickablePoint(element, elementRef);
        ClickCurrentPosition(ToMouseButton(button), doubleClick);
    }

    internal static void Fill(
        AutomationElement element,
        string elementRef,
        string text,
        TimeSpan timeout
    )
    {
        WaitUntilActionable(element, elementRef, timeout);
        var valuePattern = element.Patterns.Value.PatternOrDefault;
        if (valuePattern is not null && !valuePattern.IsReadOnly.ValueOrDefault)
        {
            valuePattern.SetValue(text);
            return;
        }

        element.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(text);
    }

    internal static void Press(KeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        var modifiers = chord.Modifiers.Select(KeyMapper.ToVirtualKey).ToArray();
        if (KeyMapper.TryMapKey(chord.Key, out var key))
        {
            PressKey(key, modifiers);
            return;
        }

        if (modifiers.Length == 0 && chord.Key.Length == 1)
        {
            Keyboard.Type(chord.Key);
            return;
        }

        throw new AutomationException(
            ErrorCodes.BadRequest,
            $"Unknown key '{chord.Key}'. Use a named key (e.g. Enter, F5, PageDown) "
                + "or a single character."
        );
    }

    internal static void SelectItem(
        AutomationElement element,
        string elementRef,
        string item,
        TimeSpan timeout
    )
    {
        WaitUntilActionable(element, elementRef, timeout);
        try
        {
            SelectItemCore(element, item);
        }
        catch (InvalidOperationException ex)
        {
            throw new AutomationException(
                ErrorCodes.NotFound,
                $"No item '{item}' found in {ElementRef.Display(elementRef)}: {ex.Message}",
                ex
            );
        }
    }

    internal static void Expand(
        AutomationElement element,
        string elementRef,
        bool collapse,
        TimeSpan timeout
    )
    {
        WaitUntilActionable(element, elementRef, timeout);
        var pattern =
            element.Patterns.ExpandCollapse.PatternOrDefault
            ?? throw PatternUnsupported(elementRef, "ExpandCollapse");
        if (collapse)
        {
            pattern.Collapse();
        }
        else
        {
            pattern.Expand();
        }
    }

    internal static void Toggle(
        AutomationElement element,
        string elementRef,
        bool? desiredState,
        TimeSpan timeout
    )
    {
        WaitUntilActionable(element, elementRef, timeout);
        var pattern =
            element.Patterns.Toggle.PatternOrDefault
            ?? throw PatternUnsupported(elementRef, "Toggle");
        var isOn = pattern.ToggleState.ValueOrDefault == ToggleState.On;
        if (desiredState is { } desired && desired == isOn)
        {
            return;
        }

        pattern.Toggle();
    }

    internal static void Scroll(AutomationElement element, ScrollDirection direction, double amount)
    {
        if (TryPatternScroll(element, direction, amount))
        {
            return;
        }

        WheelScroll(element, direction, amount);
    }

    internal static void WaitUntilActionable(
        AutomationElement element,
        string elementRef,
        TimeSpan timeout
    ) =>
        Poller.WaitUntil(
            () => IsActionable(element),
            timeout,
            $"{ElementRef.Display(elementRef)} did not become enabled and on-screen within "
                + $"{timeout.TotalSeconds:0}s."
        );

    internal static void WaitForElement(
        AutomationElement element,
        string elementRef,
        bool untilGone,
        TimeSpan timeout
    )
    {
        var display = ElementRef.Display(elementRef);
        Poller.WaitUntil(
            () => IsActionable(element) != untilGone,
            timeout,
            untilGone
                ? $"{display} was still present after {timeout.TotalSeconds:0}s."
                : $"{display} did not become interactable within {timeout.TotalSeconds:0}s."
        );
    }

    internal static void WaitForText(
        AutomationElement root,
        string text,
        bool untilGone,
        TimeSpan timeout
    ) =>
        Poller.WaitUntil(
            () => ContainsText(root, text) != untilGone,
            timeout,
            untilGone
                ? $"Text '{text}' was still present after {timeout.TotalSeconds:0}s."
                : $"Text '{text}' did not appear within {timeout.TotalSeconds:0}s."
        );

    private static void ClickCurrentPosition(MouseButton button, bool doubleClick)
    {
        if (doubleClick)
        {
            Mouse.DoubleClick(button);
        }
        else
        {
            Mouse.Click(button);
        }
    }

    private static MouseButton ToMouseButton(MouseButtonKind kind) =>
        kind switch
        {
            MouseButtonKind.Left => MouseButton.Left,
            MouseButtonKind.Right => MouseButton.Right,
            MouseButtonKind.Middle => MouseButton.Middle,
            _ => MouseButton.Left,
        };

    private static void PressKey(VirtualKeyShort key, VirtualKeyShort[] modifiers)
    {
        if (modifiers.Length == 0)
        {
            Keyboard.Type(key);
            return;
        }

        using (Keyboard.Pressing(modifiers))
        {
            Keyboard.Type(key);
        }
    }

    private static void SelectItemCore(AutomationElement element, string item)
    {
        var controlType = element.Properties.ControlType.ValueOrDefault;
        if (controlType == ControlType.ComboBox)
        {
            element.AsComboBox().Select(item);
        }
        else if (controlType == ControlType.List)
        {
            element.AsListBox().Select(item);
        }
        else if (controlType == ControlType.Tab)
        {
            element.AsTab().SelectTabItem(item);
        }
        else
        {
            SelectDescendantByName(element, item);
        }
    }

    private static void SelectDescendantByName(AutomationElement element, string item)
    {
        var match =
            element.FindFirstDescendant(cf => cf.ByName(item))
            ?? throw new AutomationException(
                ErrorCodes.NotFound,
                $"No descendant named '{item}' found."
            );
        var pattern =
            match.Patterns.SelectionItem.PatternOrDefault
            ?? throw new AutomationException(
                ErrorCodes.PatternUnsupported,
                $"'{item}' does not support selection."
            );
        pattern.Select();
    }

    private static bool TryPatternScroll(
        AutomationElement element,
        ScrollDirection direction,
        double amount
    )
    {
        var pattern = element.Patterns.Scroll.PatternOrDefault;
        if (pattern is null)
        {
            return false;
        }

        var vertical = IsVertical(direction);
        if (!IsScrollable(pattern, vertical))
        {
            return false;
        }

        ApplyPatternScroll(
            pattern,
            vertical,
            ToPatternScrollAmount(direction),
            Math.Max(1, (int)Math.Round(amount))
        );
        return true;
    }

    private static bool IsVertical(ScrollDirection direction) =>
        direction is ScrollDirection.Up or ScrollDirection.Down;

    private static bool IsScrollable(IScrollPattern pattern, bool vertical) =>
        vertical
            ? pattern.VerticallyScrollable.ValueOrDefault
            : pattern.HorizontallyScrollable.ValueOrDefault;

    private static ScrollAmount ToPatternScrollAmount(ScrollDirection direction) =>
        direction switch
        {
            ScrollDirection.Up => ScrollAmount.SmallDecrement,
            ScrollDirection.Left => ScrollAmount.SmallDecrement,
            ScrollDirection.Down => ScrollAmount.SmallIncrement,
            ScrollDirection.Right => ScrollAmount.SmallIncrement,
            _ => ScrollAmount.SmallIncrement,
        };

    private static void ApplyPatternScroll(
        IScrollPattern pattern,
        bool vertical,
        ScrollAmount amount,
        int steps
    )
    {
        for (var i = 0; i < steps; i++)
        {
            if (vertical)
            {
                pattern.Scroll(ScrollAmount.NoAmount, amount);
            }
            else
            {
                pattern.Scroll(amount, ScrollAmount.NoAmount);
            }
        }
    }

    private static void WheelScroll(
        AutomationElement element,
        ScrollDirection direction,
        double amount
    )
    {
        var rect = element.Properties.BoundingRectangle.ValueOrDefault;
        if (!rect.IsEmpty)
        {
            Mouse.Position = RectConversions.Center(rect);
        }

        var signedAmount = direction is ScrollDirection.Up or ScrollDirection.Right
            ? amount
            : -amount;
        if (IsVertical(direction))
        {
            Mouse.Scroll(signedAmount);
        }
        else
        {
            Mouse.HorizontalScroll(signedAmount);
        }
    }

    private static bool ContainsText(AutomationElement root, string text)
    {
        // One bulk cross-process fetch of every descendant name per poll tick,
        // instead of one COM round-trip per element.
        var cacheRequest = new CacheRequest
        {
            TreeScope = TreeScope.Subtree,
            TreeFilter = TrueCondition.Default,
            AutomationElementMode = AutomationElementMode.None,
        };
        cacheRequest.Add(root.Automation.PropertyLibrary.Element.Name);
        try
        {
            using (cacheRequest.Activate())
            {
                var descendants = root.FindAllDescendants();
                return Array.Exists(
                    descendants,
                    descendant =>
                        descendant.Properties.Name.ValueOrDefault?.Contains(
                            text,
                            StringComparison.OrdinalIgnoreCase
                        ) == true
                );
            }
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static bool IsActionable(AutomationElement element)
    {
        try
        {
            return element.Properties.IsEnabled.ValueOrDefault
                && !element.Properties.IsOffscreen.ValueOrDefault;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static System.Drawing.Point GetClickablePoint(
        AutomationElement element,
        string elementRef
    )
    {
        if (element.TryGetClickablePoint(out var point))
        {
            return point;
        }

        var rect = element.Properties.BoundingRectangle.ValueOrDefault;
        return rect.IsEmpty
            ? throw new AutomationException(
                ErrorCodes.InternalError,
                $"{ElementRef.Display(elementRef)} has no clickable point or bounds."
            )
            : RectConversions.Center(rect);
    }

    private static AutomationException PatternUnsupported(string elementRef, string pattern) =>
        new(
            ErrorCodes.PatternUnsupported,
            $"{ElementRef.Display(elementRef)} does not support the {pattern} pattern."
        );
}
