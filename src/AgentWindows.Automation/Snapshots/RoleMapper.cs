using FlaUI.Core.Definitions;

namespace AgentWindows.Automation.Snapshots;

public static class RoleMapper
{
    private static readonly Dictionary<ControlType, string> _roles = new()
    {
        [ControlType.AppBar] = "appbar",
        [ControlType.Button] = "button",
        [ControlType.Calendar] = "calendar",
        [ControlType.CheckBox] = "checkbox",
        [ControlType.ComboBox] = "combobox",
        [ControlType.Custom] = "custom",
        [ControlType.DataGrid] = "datagrid",
        [ControlType.DataItem] = "dataitem",
        [ControlType.Document] = "document",
        [ControlType.Edit] = "edit",
        [ControlType.Group] = "group",
        [ControlType.Header] = "header",
        [ControlType.HeaderItem] = "headeritem",
        [ControlType.Hyperlink] = "link",
        [ControlType.Image] = "image",
        [ControlType.List] = "list",
        [ControlType.ListItem] = "listitem",
        [ControlType.Menu] = "menu",
        [ControlType.MenuBar] = "menubar",
        [ControlType.MenuItem] = "menuitem",
        [ControlType.Pane] = "pane",
        [ControlType.ProgressBar] = "progressbar",
        [ControlType.RadioButton] = "radiobutton",
        [ControlType.ScrollBar] = "scrollbar",
        [ControlType.SemanticZoom] = "semanticzoom",
        [ControlType.Separator] = "separator",
        [ControlType.Slider] = "slider",
        [ControlType.Spinner] = "spinner",
        [ControlType.SplitButton] = "splitbutton",
        [ControlType.StatusBar] = "statusbar",
        [ControlType.Tab] = "tab",
        [ControlType.TabItem] = "tabitem",
        [ControlType.Table] = "table",
        [ControlType.Text] = "text",
        [ControlType.Thumb] = "thumb",
        [ControlType.TitleBar] = "titlebar",
        [ControlType.ToolBar] = "toolbar",
        [ControlType.ToolTip] = "tooltip",
        [ControlType.Tree] = "tree",
        [ControlType.TreeItem] = "treeitem",
        [ControlType.Window] = "window",
    };

    public static string ToRole(ControlType controlType) =>
        _roles.TryGetValue(controlType, out var role) ? role : "unknown";
}
