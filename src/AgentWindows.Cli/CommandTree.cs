using System.CommandLine;
using AgentWindows.Core.Protocol;

namespace AgentWindows.Cli;

public static class CommandTree
{
    /// <summary>
    /// Parser configuration with response-file expansion disabled so element refs
    /// like '@e5' are passed through as ordinary tokens.
    /// </summary>
    public static ParserConfiguration CreateConfiguration() =>
        new() { ResponseFileTokenReplacer = null };

    public static RootCommand Build() => Build(out _);

    public static RootCommand Build(out CommandContext context)
    {
        var jsonOption = new Option<bool>("--json")
        {
            Description = "Emit the raw JSON response envelope.",
            Recursive = true,
        };
        var sessionOption = new Option<string>("--session")
        {
            Description = "Named daemon session (defaults to AGENT_WINDOWS_SESSION or 'default').",
            Recursive = true,
            DefaultValueFactory = _ =>
                Environment.GetEnvironmentVariable("AGENT_WINDOWS_SESSION")
                ?? PipeNames.DefaultSession,
        };
        context = new CommandContext(jsonOption, sessionOption);

        var root = new RootCommand(
            "Windows-native UI automation for AI agents, built on UI Automation."
        );
        root.Options.Add(jsonOption);
        root.Options.Add(sessionOption);
        root.Subcommands.Add(BuildList(context));
        root.Subcommands.Add(BuildLaunch(context));
        root.Subcommands.Add(BuildAttach(context));
        root.Subcommands.Add(BuildSnapshot(context));
        root.Subcommands.Add(BuildClick(context));
        root.Subcommands.Add(BuildFill(context));
        root.Subcommands.Add(BuildPress(context));
        root.Subcommands.Add(BuildSelect(context));
        root.Subcommands.Add(BuildExpand(context));
        root.Subcommands.Add(BuildToggle(context));
        root.Subcommands.Add(BuildScroll(context));
        root.Subcommands.Add(BuildWait(context));
        root.Subcommands.Add(BuildScreenshot(context));
        root.Subcommands.Add(BuildWindow(context));
        root.Subcommands.Add(BuildClose(context));
        root.Subcommands.Add(BuildStatus(context));
        root.Subcommands.Add(BuildDaemon(context));
        root.Subcommands.Add(BuildRepl(context, root));
        return root;
    }

    private static Command BuildRepl(CommandContext context, RootCommand root)
    {
        var command = new Command(
            "repl",
            "Read commands from stdin (one per line, CLI syntax or raw JSON) and write "
                + "one JSON response per line. Exits non-zero on the first failure."
        );
        command.SetAction(parseResult =>
        {
            var session = context.GetSession(parseResult);
            using var client = new DaemonClient(session);
            var runner = new ReplRunner(root, context, request => client.Send(request));
            return runner.Run(Console.In, Console.Out);
        });
        return command;
    }

    /// <summary>
    /// The daemon resolves relative paths against its own working directory, so
    /// resolve caller-relative paths here. Bare names (e.g. notepad.exe) pass
    /// through for PATH lookup.
    /// </summary>
    private static string ResolveAppPath(string app) =>
        File.Exists(app) ? Path.GetFullPath(app) : app;

    private static Option<int> CreateTimeoutOption() =>
        new("--timeout")
        {
            Description = "Timeout in milliseconds for the action to become possible.",
            DefaultValueFactory = _ => 10_000,
        };

    private static Command BuildList(CommandContext context)
    {
        var command = new Command("list", "List top-level windows on the desktop.");
        context.Attach(command, _ => new ListWindowsRequest());
        return command;
    }

    private static Command BuildLaunch(CommandContext context)
    {
        var appOption = new Option<string>("--app")
        {
            Description = "Path to the executable to launch.",
            Required = true,
        };
        var argsOption = new Option<string?>("--args")
        {
            Description = "Command-line arguments for the application.",
        };
        var timeoutOption = CreateTimeoutOption();
        var command = new Command("launch", "Launch an application and attach to its main window.");
        command.Options.Add(appOption);
        command.Options.Add(argsOption);
        command.Options.Add(timeoutOption);
        context.Attach(
            command,
            parseResult =>
                RequestBuilder.BuildLaunch(
                    ResolveAppPath(parseResult.GetValue(appOption)!),
                    parseResult.GetValue(argsOption),
                    parseResult.GetValue(timeoutOption)
                )
        );
        return command;
    }

    private static Command BuildAttach(CommandContext context)
    {
        var windowOption = new Option<string?>("--window")
        {
            Description = "Attach to the first window whose title contains this text.",
        };
        var pidOption = new Option<int?>("--pid")
        {
            Description = "Attach to the top-level window of this process id.",
        };
        var hwndOption = new Option<long?>("--hwnd")
        {
            Description = "Attach to the window with this native handle.",
        };
        var command = new Command("attach", "Attach to an existing window.");
        command.Options.Add(windowOption);
        command.Options.Add(pidOption);
        command.Options.Add(hwndOption);
        context.Attach(
            command,
            parseResult =>
                RequestBuilder.BuildAttach(
                    parseResult.GetValue(windowOption),
                    parseResult.GetValue(pidOption),
                    parseResult.GetValue(hwndOption)
                )
        );
        return command;
    }

    private static Command BuildSnapshot(CommandContext context)
    {
        var interactiveOption = new Option<bool>("--interactive", "-i")
        {
            Description = "Only include interactive elements (and their ancestors).",
        };
        var depthOption = new Option<int?>("--depth")
        {
            Description = "Maximum tree depth to walk.",
        };
        var scopeOption = new Option<string?>("--scope")
        {
            Description = "Restrict the snapshot to the subtree of a previous ref (e.g. '@e3').",
        };
        var command = new Command(
            "snapshot",
            "Capture the accessibility tree of the target window with element refs."
        );
        command.Options.Add(interactiveOption);
        command.Options.Add(depthOption);
        command.Options.Add(scopeOption);
        context.Attach(
            command,
            parseResult =>
                RequestBuilder.BuildSnapshot(
                    parseResult.GetValue(interactiveOption),
                    parseResult.GetValue(depthOption),
                    parseResult.GetValue(scopeOption)
                )
        );
        return command;
    }

    private static Command BuildClick(CommandContext context)
    {
        var refArgument = new Argument<string?>("ref")
        {
            Description = "Element ref from the latest snapshot, e.g. '@e5'.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var atOption = new Option<string?>("--at")
        {
            Description = "Click at screen coordinates 'x,y' instead of an element.",
        };
        var rightOption = new Option<bool>("--right") { Description = "Right-click." };
        var middleOption = new Option<bool>("--middle") { Description = "Middle-click." };
        var doubleOption = new Option<bool>("--double") { Description = "Double-click." };
        var timeoutOption = CreateTimeoutOption();
        var command = new Command("click", "Click an element or a screen coordinate.");
        command.Arguments.Add(refArgument);
        command.Options.Add(atOption);
        command.Options.Add(rightOption);
        command.Options.Add(middleOption);
        command.Options.Add(doubleOption);
        command.Options.Add(timeoutOption);
        context.Attach(
            command,
            parseResult =>
                RequestBuilder.BuildClick(
                    parseResult.GetValue(refArgument),
                    parseResult.GetValue(atOption),
                    parseResult.GetValue(rightOption),
                    parseResult.GetValue(middleOption),
                    parseResult.GetValue(doubleOption),
                    parseResult.GetValue(timeoutOption)
                )
        );
        return command;
    }

    private static Command BuildFill(CommandContext context)
    {
        var refArgument = new Argument<string>("ref")
        {
            Description = "Element ref from the latest snapshot, e.g. '@e7'.",
        };
        var textArgument = new Argument<string>("text") { Description = "Text to set." };
        var timeoutOption = CreateTimeoutOption();
        var command = new Command("fill", "Replace the value of an editable element.");
        command.Arguments.Add(refArgument);
        command.Arguments.Add(textArgument);
        command.Options.Add(timeoutOption);
        context.Attach(
            command,
            parseResult => new FillRequest
            {
                Ref = parseResult.GetValue(refArgument)!,
                Text = parseResult.GetValue(textArgument)!,
                TimeoutMs = parseResult.GetValue(timeoutOption),
            }
        );
        return command;
    }

    private static Command BuildPress(CommandContext context)
    {
        var keysArgument = new Argument<string>("keys")
        {
            Description = "Key or chord, e.g. 'Enter', 'Ctrl+S', 'Ctrl+Shift+Tab'.",
        };
        var command = new Command("press", "Press a key or key chord.");
        command.Arguments.Add(keysArgument);
        context.Attach(
            command,
            parseResult => new PressRequest { Keys = parseResult.GetValue(keysArgument)! }
        );
        return command;
    }

    private static Command BuildSelect(CommandContext context)
    {
        var refArgument = new Argument<string>("ref")
        {
            Description = "Ref of a combo box, list, or tab control.",
        };
        var itemArgument = new Argument<string>("item")
        {
            Description = "Name of the item to select.",
        };
        var timeoutOption = CreateTimeoutOption();
        var command = new Command("select", "Select an item in a combo box, list, or tab control.");
        command.Arguments.Add(refArgument);
        command.Arguments.Add(itemArgument);
        command.Options.Add(timeoutOption);
        context.Attach(
            command,
            parseResult => new SelectRequest
            {
                Ref = parseResult.GetValue(refArgument)!,
                Item = parseResult.GetValue(itemArgument)!,
                TimeoutMs = parseResult.GetValue(timeoutOption),
            }
        );
        return command;
    }

    private static Command BuildExpand(CommandContext context)
    {
        var refArgument = new Argument<string>("ref")
        {
            Description = "Ref of an expandable element (menu, tree item, combo box).",
        };
        var collapseOption = new Option<bool>("--collapse")
        {
            Description = "Collapse instead of expand.",
        };
        var timeoutOption = CreateTimeoutOption();
        var command = new Command("expand", "Expand (or collapse) an element.");
        command.Arguments.Add(refArgument);
        command.Options.Add(collapseOption);
        command.Options.Add(timeoutOption);
        context.Attach(
            command,
            parseResult => new ExpandRequest
            {
                Ref = parseResult.GetValue(refArgument)!,
                Collapse = parseResult.GetValue(collapseOption),
                TimeoutMs = parseResult.GetValue(timeoutOption),
            }
        );
        return command;
    }

    private static Command BuildToggle(CommandContext context)
    {
        var refArgument = new Argument<string>("ref")
        {
            Description = "Ref of a checkbox or toggle element.",
        };
        var onOption = new Option<bool>("--on") { Description = "Ensure the element is checked." };
        var offOption = new Option<bool>("--off")
        {
            Description = "Ensure the element is unchecked.",
        };
        var timeoutOption = CreateTimeoutOption();
        var command = new Command("toggle", "Toggle a checkbox or toggle element.");
        command.Arguments.Add(refArgument);
        command.Options.Add(onOption);
        command.Options.Add(offOption);
        command.Options.Add(timeoutOption);
        context.Attach(
            command,
            parseResult =>
                RequestBuilder.BuildToggle(
                    parseResult.GetValue(refArgument)!,
                    parseResult.GetValue(onOption),
                    parseResult.GetValue(offOption),
                    parseResult.GetValue(timeoutOption)
                )
        );
        return command;
    }

    private static Command BuildScroll(CommandContext context)
    {
        var directionArgument = new Argument<string>("direction")
        {
            Description = "up, down, left, or right.",
        };
        var refArgument = new Argument<string?>("ref")
        {
            Description = "Ref of the element to scroll; omitted scrolls the target window.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var amountOption = new Option<double>("--amount")
        {
            Description = "Scroll steps (wheel ticks or small increments).",
            DefaultValueFactory = _ => 3,
        };
        var timeoutOption = CreateTimeoutOption();
        var command = new Command("scroll", "Scroll an element or the target window.");
        command.Arguments.Add(directionArgument);
        command.Arguments.Add(refArgument);
        command.Options.Add(amountOption);
        command.Options.Add(timeoutOption);
        context.Attach(
            command,
            parseResult =>
                RequestBuilder.BuildScroll(
                    parseResult.GetValue(refArgument),
                    parseResult.GetValue(directionArgument)!,
                    parseResult.GetValue(amountOption),
                    parseResult.GetValue(timeoutOption)
                )
        );
        return command;
    }

    private static Command BuildWait(CommandContext context)
    {
        var refArgument = new Argument<string?>("ref")
        {
            Description = "Ref to wait for; omit when using --text.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var textOption = new Option<string?>("--text")
        {
            Description = "Wait for an element whose name contains this text.",
        };
        var goneOption = new Option<bool>("--gone")
        {
            Description = "Wait for the element/text to disappear instead.",
        };
        var timeoutOption = CreateTimeoutOption();
        var command = new Command("wait", "Wait until an element or text appears (or disappears).");
        command.Arguments.Add(refArgument);
        command.Options.Add(textOption);
        command.Options.Add(goneOption);
        command.Options.Add(timeoutOption);
        context.Attach(
            command,
            parseResult =>
                RequestBuilder.BuildWait(
                    parseResult.GetValue(refArgument),
                    parseResult.GetValue(textOption),
                    parseResult.GetValue(goneOption),
                    parseResult.GetValue(timeoutOption)
                )
        );
        return command;
    }

    private static Command BuildScreenshot(CommandContext context)
    {
        var outputArgument = new Argument<string>("output")
        {
            Description = "Path of the PNG file to write.",
        };
        var refOption = new Option<string?>("--ref")
        {
            Description = "Capture only this element instead of the whole target window.",
        };
        var command = new Command(
            "screenshot",
            "Save a screenshot of the target window (or an element) as PNG."
        );
        command.Arguments.Add(outputArgument);
        command.Options.Add(refOption);
        context.Attach(
            command,
            parseResult => new ScreenshotRequest
            {
                OutputPath = Path.GetFullPath(parseResult.GetValue(outputArgument)!),
                Ref = parseResult.GetValue(refOption),
            }
        );
        return command;
    }

    private static Command BuildWindow(CommandContext context)
    {
        var actionArgument = new Argument<string>("action")
        {
            Description = "focus, move, resize, maximize, minimize, or restore.",
        };
        var xOption = new Option<int?>("--x") { Description = "Target x position (move)." };
        var yOption = new Option<int?>("--y") { Description = "Target y position (move)." };
        var widthOption = new Option<int?>("--width") { Description = "Target width (resize)." };
        var heightOption = new Option<int?>("--height") { Description = "Target height (resize)." };
        var command = new Command("window", "Manage the target window.");
        command.Arguments.Add(actionArgument);
        command.Options.Add(xOption);
        command.Options.Add(yOption);
        command.Options.Add(widthOption);
        command.Options.Add(heightOption);
        context.Attach(
            command,
            parseResult =>
                RequestBuilder.BuildWindowAction(
                    parseResult.GetValue(actionArgument)!,
                    parseResult.GetValue(xOption),
                    parseResult.GetValue(yOption),
                    parseResult.GetValue(widthOption),
                    parseResult.GetValue(heightOption)
                )
        );
        return command;
    }

    private static Command BuildClose(CommandContext context)
    {
        var forceOption = new Option<bool>("--force")
        {
            Description = "Kill the process instead of closing the window.",
        };
        var command = new Command("close", "Close the target window.");
        command.Options.Add(forceOption);
        context.Attach(
            command,
            parseResult => new CloseRequest { Force = parseResult.GetValue(forceOption) }
        );
        return command;
    }

    private static Command BuildStatus(CommandContext context)
    {
        var command = new Command("status", "Show daemon and target status.");
        context.Attach(command, _ => new StatusRequest());
        return command;
    }

    private static Command BuildDaemon(CommandContext context)
    {
        var daemon = new Command("daemon", "Manage the background automation daemon.");

        var run = new Command("run", "Run the daemon in the foreground (used internally).")
        {
            Hidden = true,
        };
        run.SetAction(parseResult => DaemonHost.Run(context.GetSession(parseResult)));
        daemon.Subcommands.Add(run);

        var stop = new Command("stop", "Stop the daemon for this session.");
        context.Attach(stop, _ => new ShutdownRequest());
        daemon.Subcommands.Add(stop);

        return daemon;
    }
}
