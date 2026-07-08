using System.Globalization;
using System.Runtime.InteropServices;
using AgentWindows.Core.Model;
using AgentWindows.Core.Snapshot;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
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
    private readonly List<(NodeData Node, AutomationElement Element)> _selectionFixups = [];
    private readonly SnapshotOptions _options;
    private readonly bool _cached;
    private int _nextRefIndex;

    private SnapshotBuilder(SnapshotOptions options, int firstRefIndex, bool cached)
    {
        _options = options;
        _nextRefIndex = firstRefIndex;
        _cached = cached;
    }

    public static SnapshotBuildResult Build(
        AutomationElement root,
        SnapshotOptions options,
        int firstRefIndex
    )
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            return BuildCached(root, options, firstRefIndex);
        }
        catch (Exception ex) when (ex is COMException or NotSupportedException)
        {
            // Some providers reject bulk caching; fall back to a live walk.
            return BuildLive(root, options, firstRefIndex);
        }
    }

    /// <summary>
    /// Fetches the whole subtree (properties and pattern state) in one cross-process
    /// UIA call instead of round-tripping per node per property.
    /// </summary>
    private static SnapshotBuildResult BuildCached(
        AutomationElement root,
        SnapshotOptions options,
        int firstRefIndex
    )
    {
        var builder = new SnapshotBuilder(options, firstRefIndex, cached: true);
        NodeData? data;
        var cacheRequest = CreateCacheRequest(root.Automation);
        using (cacheRequest.Activate())
        {
            var cachedRoot =
                root.FindFirst(TreeScope.Element, TrueCondition.Default)
                ?? throw new NotSupportedException("Bulk-cached root fetch returned nothing.");
            data = builder.BuildNode(cachedRoot, 0, isRoot: true);
        }

        builder.ApplySelectionFixups();
        return builder.ToResult(data);
    }

    private static SnapshotBuildResult BuildLive(
        AutomationElement root,
        SnapshotOptions options,
        int firstRefIndex
    )
    {
        var builder = new SnapshotBuilder(options, firstRefIndex, cached: false);
        var data = builder.BuildNode(root, 0, isRoot: true);
        builder.ApplySelectionFixups();
        return builder.ToResult(data);
    }

    private static CacheRequest CreateCacheRequest(AutomationBase automation)
    {
        var properties = automation.PropertyLibrary;
        var patterns = automation.PatternLibrary;
        var cacheRequest = new CacheRequest
        {
            TreeScope = TreeScope.Subtree,
            TreeFilter = TrueCondition.Default,
            // Keep full live references so refs from the snapshot remain actionable.
            AutomationElementMode = AutomationElementMode.Full,
        };
        cacheRequest.Add(properties.Element.ControlType);
        cacheRequest.Add(properties.Element.Name);
        cacheRequest.Add(properties.Element.AutomationId);
        cacheRequest.Add(properties.Element.IsEnabled);
        cacheRequest.Add(properties.Element.HasKeyboardFocus);
        cacheRequest.Add(properties.Element.IsOffscreen);
        cacheRequest.Add(properties.Element.BoundingRectangle);
        cacheRequest.Add(properties.Element.ProcessId);
        cacheRequest.Add(patterns.ValuePattern);
        cacheRequest.Add(properties.Value.Value);
        cacheRequest.Add(patterns.TogglePattern);
        cacheRequest.Add(properties.Toggle.ToggleState);
        cacheRequest.Add(patterns.ExpandCollapsePattern);
        cacheRequest.Add(properties.ExpandCollapse.ExpandCollapseState);
        cacheRequest.Add(patterns.SelectionItemPattern);
        cacheRequest.Add(properties.SelectionItem.IsSelected);
        cacheRequest.Add(patterns.SelectionPattern);
        return cacheRequest;
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

    private static BoundingRect? ReadBounds(AutomationElement element)
    {
        var rect = element.Properties.BoundingRectangle.ValueOrDefault;
        return rect.IsEmpty ? null : new BoundingRect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>
    /// Reads the selected item names live; selection elements are not part of the
    /// bulk cache, so this must run outside the cache scope.
    /// </summary>
    private static string? ReadSelectionValueLive(AutomationElement element)
    {
        try
        {
            var selected = element.Patterns.Selection.PatternOrDefault?.Selection.ValueOrDefault;
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
        catch (COMException)
        {
            return null;
        }
    }

    private static UiNode ToUiNode(NodeData data) =>
        new()
        {
            Role = data.Role,
            Name = data.Name,
            AutomationId = data.AutomationId,
            Ref = data.Ref,
            Value = data.Value,
            States = data.States,
            Bounds = data.Bounds,
            Children = [.. data.Children.Select(ToUiNode)],
        };

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? null : value[..Math.Min(value.Length, maxLength)];

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private SnapshotBuildResult ToResult(NodeData? data)
    {
        var root = data is null ? new UiNode { Role = "unknown" } : ToUiNode(data);
        return new SnapshotBuildResult
        {
            Root = root,
            Refs = _refs,
            NextRefIndex = _nextRefIndex,
        };
    }

    private void ApplySelectionFixups()
    {
        foreach (var (node, element) in _selectionFixups)
        {
            node.Value = ReadSelectionValueLive(element);
        }
    }

    private NodeData? BuildNode(AutomationElement element, int depth, bool isRoot)
    {
        try
        {
            return BuildNodeCore(element, depth, isRoot);
        }
        catch (COMException) when (!_cached)
        {
            // The element vanished mid-walk; drop it from the snapshot.
            return null;
        }
    }

    private NodeData? BuildNodeCore(AutomationElement element, int depth, bool isRoot)
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

        var node = new NodeData
        {
            Role = RoleMapper.ToRole(controlType),
            Name = Truncate(element.Properties.Name.ValueOrDefault, _maxNameLength),
            AutomationId = NullIfEmpty(element.Properties.AutomationId.ValueOrDefault),
            Ref = elementRef,
            States = CollectStates(element),
            Bounds = ReadBounds(element),
            Children = children,
        };
        PopulateValue(node, element);
        return node;
    }

    private void PopulateValue(NodeData node, AutomationElement element)
    {
        var valuePattern = element.Patterns.Value.PatternOrDefault;
        if (valuePattern is not null)
        {
            node.Value = Truncate(valuePattern.Value.ValueOrDefault, _maxValueLength);
            return;
        }

        // Combo boxes, lists, and trees often lack ValuePattern; surface their
        // selected item names as the value once the cache scope has closed.
        if (element.Patterns.Selection.IsSupported)
        {
            _selectionFixups.Add((node, element));
        }
    }

    private List<NodeData> BuildChildren(AutomationElement element, int depth)
    {
        var maxDepth = _options.MaxDepth ?? _defaultMaxDepth;
        if (depth >= maxDepth)
        {
            return [];
        }

        var children = new List<NodeData>();
        var elements = _cached ? element.CachedChildren : element.FindAllChildren();
        foreach (var child in elements)
        {
            var node = BuildNode(child, depth + 1, isRoot: false);
            if (node is not null)
            {
                children.Add(node);
            }
        }

        return children;
    }

    private sealed class NodeData
    {
        public required string Role { get; init; }

        public string? Name { get; init; }

        public string? AutomationId { get; init; }

        public string? Ref { get; init; }

        public string? Value { get; set; }

        public required List<string> States { get; init; }

        public BoundingRect? Bounds { get; init; }

        public required List<NodeData> Children { get; init; }
    }
}
