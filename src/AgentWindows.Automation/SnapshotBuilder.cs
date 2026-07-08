using System.Globalization;
using System.Runtime.InteropServices;
using AgentWindows.Core.Model;
using AgentWindows.Core.Snapshot;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace AgentWindows.Automation;

public sealed class SnapshotBuilder
{
    private const int _defaultMaxDepth = 50;
    private const int _maxNameLength = 120;
    private const int _maxValueLength = 200;

    private static readonly HashSet<ControlType> _interactiveTypes =
    [
        ControlType.Button,
        ControlType.CheckBox,
        ControlType.ComboBox,
        ControlType.DataGrid,
        ControlType.DataItem,
        ControlType.Document,
        ControlType.Edit,
        ControlType.Hyperlink,
        ControlType.List,
        ControlType.ListItem,
        ControlType.MenuItem,
        ControlType.RadioButton,
        ControlType.Slider,
        ControlType.Spinner,
        ControlType.SplitButton,
        ControlType.TabItem,
        ControlType.Table,
        ControlType.Thumb,
        ControlType.Tree,
        ControlType.TreeItem,
    ];

    private readonly Dictionary<string, AutomationElement> _refs = [];
    private readonly SnapshotOptions _options;
    private int _nextRefIndex;

    private SnapshotBuilder(SnapshotOptions options, int firstRefIndex)
    {
        _options = options;
        _nextRefIndex = firstRefIndex;
    }

    public static SnapshotBuildResult Build(
        AutomationElement root,
        SnapshotOptions options,
        int firstRefIndex
    )
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(options);
        var builder = new SnapshotBuilder(options, firstRefIndex);
        var node = builder.BuildNode(root, 0, isRoot: true) ?? new UiNode { Role = "unknown" };
        return new SnapshotBuildResult
        {
            Root = node,
            Refs = builder._refs,
            NextRefIndex = builder._nextRefIndex,
        };
    }

    private static string? ReadValue(AutomationElement element)
    {
        var pattern = element.Patterns.Value.PatternOrDefault;
        return pattern is null
            ? ReadSelectionValue(element)
            : Truncate(pattern.Value.ValueOrDefault, _maxValueLength);
    }

    /// <summary>
    /// Combo boxes, lists, and trees often lack ValuePattern (e.g. non-editable WPF
    /// combos); surface their selected item names as the value instead.
    /// </summary>
    private static string? ReadSelectionValue(AutomationElement element)
    {
        var selection = element.Patterns.Selection.PatternOrDefault;
        var selected = selection?.Selection.ValueOrDefault;
        if (selected is null || selected.Length == 0)
        {
            return null;
        }

        var names = selected
            .Select(item => item.Properties.Name.ValueOrDefault)
            .Where(name => !string.IsNullOrEmpty(name));
        var joined = string.Join(", ", names);
        return joined.Length == 0 ? null : Truncate(joined, _maxValueLength);
    }

    private static List<string> CollectStates(AutomationElement element)
    {
        var states = new List<string>();
        if (!element.Properties.IsEnabled.ValueOrDefault)
        {
            states.Add("disabled");
        }

        if (element.Properties.HasKeyboardFocus.ValueOrDefault)
        {
            states.Add("focused");
        }

        if (element.Properties.IsOffscreen.ValueOrDefault)
        {
            states.Add("offscreen");
        }

        AddToggleState(element, states);
        AddExpandState(element, states);
        AddSelectionState(element, states);
        return states;
    }

    private static void AddToggleState(AutomationElement element, List<string> states)
    {
        var toggle = element.Patterns.Toggle.PatternOrDefault;
        if (toggle is not null)
        {
            states.Add(
                toggle.ToggleState.ValueOrDefault == ToggleState.On ? "checked" : "unchecked"
            );
        }
    }

    private static void AddExpandState(AutomationElement element, List<string> states)
    {
        var expandState = element
            .Patterns
            .ExpandCollapse
            .PatternOrDefault
            ?.ExpandCollapseState
            .ValueOrDefault;
        if (expandState is ExpandCollapseState.Expanded or ExpandCollapseState.PartiallyExpanded)
        {
            states.Add("expanded");
        }
        else if (expandState is ExpandCollapseState.Collapsed)
        {
            states.Add("collapsed");
        }
    }

    private static void AddSelectionState(AutomationElement element, List<string> states)
    {
        var selectionItem = element.Patterns.SelectionItem.PatternOrDefault;
        if (selectionItem is not null && selectionItem.IsSelected.ValueOrDefault)
        {
            states.Add("selected");
        }
    }

    private static BoundingRect? ReadBounds(AutomationElement element)
    {
        var rect = element.Properties.BoundingRectangle.ValueOrDefault;
        return rect.IsEmpty ? null : new BoundingRect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? null : value[..Math.Min(value.Length, maxLength)];

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private UiNode? BuildNode(AutomationElement element, int depth, bool isRoot)
    {
        try
        {
            return BuildNodeCore(element, depth, isRoot);
        }
        catch (COMException)
        {
            // The element vanished mid-walk; drop it from the snapshot.
            return null;
        }
    }

    private UiNode? BuildNodeCore(AutomationElement element, int depth, bool isRoot)
    {
        var controlType = element.Properties.ControlType.ValueOrDefault;
        var interactive = _interactiveTypes.Contains(controlType);
        var children = BuildChildren(element, depth);

        if (_options.InteractiveOnly && !interactive && !isRoot && children.Count == 0)
        {
            return null;
        }

        string? elementRef = null;
        if (interactive || isRoot)
        {
            elementRef = string.Create(CultureInfo.InvariantCulture, $"e{_nextRefIndex}");
            _nextRefIndex++;
            _refs[elementRef] = element;
        }

        return new UiNode
        {
            Role = RoleMapper.ToRole(controlType),
            Name = Truncate(element.Properties.Name.ValueOrDefault, _maxNameLength),
            AutomationId = NullIfEmpty(element.Properties.AutomationId.ValueOrDefault),
            Ref = elementRef,
            Value = ReadValue(element),
            States = CollectStates(element),
            Bounds = ReadBounds(element),
            Children = children,
        };
    }

    private List<UiNode> BuildChildren(AutomationElement element, int depth)
    {
        var maxDepth = _options.MaxDepth ?? _defaultMaxDepth;
        if (depth >= maxDepth)
        {
            return [];
        }

        var children = new List<UiNode>();
        foreach (var child in element.FindAllChildren())
        {
            var node = BuildNode(child, depth + 1, isRoot: false);
            if (node is not null)
            {
                children.Add(node);
            }
        }

        return children;
    }
}
