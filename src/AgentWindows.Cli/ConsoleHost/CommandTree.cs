using System.CommandLine;
using System.CommandLine.Parsing;
using AgentWindows.Cli.Daemon;
using AgentWindows.Core.Elements;
using AgentWindows.Core.Protocol.Capture;
using AgentWindows.Core.Protocol.Interaction;
using AgentWindows.Core.Protocol.Lifecycle;
using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Protocol.Windows;
using AgentWindows.Core.Session;

namespace AgentWindows.Cli.ConsoleHost;

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
        root.Subcommands.Add(BuildFind(context));
        root.Subcommands.Add(BuildActivate(context));
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
        root.Subcommands.Add(BuildSkills());
        return root;
    }

    private static Command BuildSkills()
    {
        var skills = new Command("skills", "Bundled skill documentation for agents.");

        var list = new Command("list", "List the bundled skills.");
        list.SetAction(_ =>
        {
            foreach (var name in SkillCatalog.SkillNames)
            {
                Console.Out.WriteLine(name);
            }
        });
        skills.Subcommands.Add(list);

        var nameArgument = new Argument<string?>("name")
        {
            Description = "Skill name; defaults to the only bundled skill.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var fullOption = new Option<bool>("--full")
        {
            Description = "Append the skill's reference files to the output.",
        };
        var get = new Command("get", "Print a bundled skill as markdown.");
        get.Arguments.Add(nameArgument);
        get.Options.Add(fullOption);
        get.SetAction(parseResult =>
        {
            var name = parseResult.GetValue(nameArgument) ?? SkillCatalog.DefaultSkillName;
            if (name is null)
            {
                Console.Error.WriteLine("no skill name given and no single default is available.");
                return 1;
            }

            var text = SkillCatalog.Read(name, parseResult.GetValue(fullOption));
            if (text is null)
            {
                Console.Error.WriteLine(
                    $"unknown skill '{name}'. Available: {string.Join(", ", SkillCatalog.SkillNames)}"
                );
                return 1;
            }

            Console.Out.Write(text);
            return 0;
        });
        skills.Subcommands.Add(get);

        return skills;
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
            var runner = new ReplRunner(
                root,
                context,
                session,
                request => client.Send(request),
                request => client.SendRaw(request)
            );
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

    private static string GetRequiredValue(ParseResult parseResult, Argument<string> argument) =>
        parseResult.GetValue(argument)
        ?? throw new AutomationException(
            ErrorCodes.BadRequest,
            $"Missing required argument '{argument.Name}'."
        );

    private static string GetRequiredValue(ParseResult parseResult, Option<string> option) =>
        parseResult.GetValue(option)
        ?? throw new AutomationException(
            ErrorCodes.BadRequest,
            $"Missing required option '{option.Name}'."
        );

    private static Option<int> CreateTimeoutOption() =>
        new("--timeout")
        {
            Description = "Timeout in milliseconds for the action to become possible.",
            DefaultValueFactory = _ => ProtocolDefaults.TimeoutMs,
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
            parseResult => new LaunchRequest
            {
                Path = ResolveAppPath(GetRequiredValue(parseResult, appOption)),
                Arguments = parseResult.GetValue(argsOption),
                TimeoutMs = parseResult.GetValue(timeoutOption),
            }
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
            parseResult => new AttachRequest
            {
                Title = parseResult.GetValue(windowOption),
                ProcessId = parseResult.GetValue(pidOption),
                WindowHandle = parseResult.GetValue(hwndOption),
            }
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
        var viewOption = new Option<string?>("--view")
        {
            Description = "UIA tree view: raw or control (-i defaults to control).",
        };
        var command = new Command(
            "snapshot",
            "Capture the accessibility tree of the target window with element refs."
        );
        command.Options.Add(interactiveOption);
        command.Options.Add(depthOption);
        command.Options.Add(scopeOption);
        command.Options.Add(viewOption);
        context.Attach(
            command,
            parseResult => new SnapshotRequest
            {
                InteractiveOnly = parseResult.GetValue(interactiveOption),
                MaxDepth = parseResult.GetValue(depthOption),
                ScopeRef = parseResult.GetValue(scopeOption),
                View = RequestBuilder.ParseSnapshotView(
                    parseResult.GetValue(viewOption),
                    parseResult.GetValue(interactiveOption)
                ),
            }
        );
        return command;
    }

    private static Command BuildFind(CommandContext context)
    {
        var selectors = new SelectorOptions();
        var allOption = new Option<bool>("--all")
        {
            Description = "Return every match instead of the first match.",
        };
        var command = new Command("find", "Find elements with a provider-side UIA selector.");
        selectors.AddTo(command);
        command.Options.Add(allOption);
        context.Attach(
            command,
            parseResult => new FindRequest
            {
                Selector = selectors.Read(parseResult) ?? new ElementSelector(),
                All = parseResult.GetValue(allOption),
            }
        );
        return command;
    }

    private static Command BuildActivate(CommandContext context)
    {
        var refArgument = OptionalRefArgument("Ref of the element to activate.");
        var selectors = new SelectorOptions();
        var timeoutOption = CreateTimeoutOption();
        var command = new Command(
            "activate",
            "Activate an element through a semantic UIA pattern."
        );
        command.Arguments.Add(refArgument);
        selectors.AddTo(command);
        command.Options.Add(timeoutOption);
        context.Attach(
            command,
            parseResult => new ActivateRequest
            {
                Ref = parseResult.GetValue(refArgument),
                Selector = selectors.Read(parseResult),
                TimeoutMs = parseResult.GetValue(timeoutOption),
            }
        );
        return command;
    }

    private static Argument<string?> OptionalRefArgument(string description) =>
        new("ref") { Description = description, Arity = ArgumentArity.ZeroOrOne };

    private static Command BuildClick(CommandContext context)
    {
        var refArgument = OptionalRefArgument("Element ref from the latest snapshot, e.g. '@e5'.");
        var selectors = new SelectorOptions();
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
        selectors.AddTo(command);
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
                    selectors.Read(parseResult),
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
        var refArgument = OptionalRefArgument("Element ref from the latest snapshot, e.g. '@e7'.");
        var textArgument = new Argument<string?>("text")
        {
            Description = "Text to set when using a positional ref.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var valueOption = new Option<string?>("--value")
        {
            Description = "Text to set when targeting with selector options.",
        };
        var selectors = new SelectorOptions();
        var timeoutOption = CreateTimeoutOption();
        var command = new Command("fill", "Replace the value of an editable element.");
        command.Arguments.Add(refArgument);
        command.Arguments.Add(textArgument);
        command.Options.Add(valueOption);
        selectors.AddTo(command);
        command.Options.Add(timeoutOption);
        context.Attach(
            command,
            parseResult => new FillRequest
            {
                Ref = parseResult.GetValue(refArgument),
                Selector = selectors.Read(parseResult),
                Text = RequestBuilder.RequireValue(
                    parseResult.GetValue(textArgument),
                    parseResult.GetValue(valueOption),
                    "value"
                ),
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
            parseResult => new PressRequest { Keys = GetRequiredValue(parseResult, keysArgument) }
        );
        return command;
    }

    private static Command BuildSelect(CommandContext context)
    {
        var refArgument = OptionalRefArgument("Ref of a combo box, list, or tab control.");
        var itemArgument = new Argument<string?>("item")
        {
            Description = "Name of the item when using a positional ref.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var itemOption = new Option<string?>("--item")
        {
            Description = "Name of the item when targeting with selector options.",
        };
        var selectors = new SelectorOptions();
        var timeoutOption = CreateTimeoutOption();
        var command = new Command("select", "Select an item in a combo box, list, or tab control.");
        command.Arguments.Add(refArgument);
        command.Arguments.Add(itemArgument);
        command.Options.Add(itemOption);
        selectors.AddTo(command);
        command.Options.Add(timeoutOption);
        context.Attach(
            command,
            parseResult => new SelectRequest
            {
                Ref = parseResult.GetValue(refArgument),
                Selector = selectors.Read(parseResult),
                Item = RequestBuilder.RequireValue(
                    parseResult.GetValue(itemArgument),
                    parseResult.GetValue(itemOption),
                    "item"
                ),
                TimeoutMs = parseResult.GetValue(timeoutOption),
            }
        );
        return command;
    }

    private static Command BuildExpand(CommandContext context)
    {
        var refArgument = OptionalRefArgument(
            "Ref of an expandable element (menu, tree item, combo box)."
        );
        var selectors = new SelectorOptions();
        var collapseOption = new Option<bool>("--collapse")
        {
            Description = "Collapse instead of expand.",
        };
        var timeoutOption = CreateTimeoutOption();
        var command = new Command("expand", "Expand (or collapse) an element.");
        command.Arguments.Add(refArgument);
        selectors.AddTo(command);
        command.Options.Add(collapseOption);
        command.Options.Add(timeoutOption);
        context.Attach(
            command,
            parseResult => new ExpandRequest
            {
                Ref = parseResult.GetValue(refArgument),
                Selector = selectors.Read(parseResult),
                Collapse = parseResult.GetValue(collapseOption),
                TimeoutMs = parseResult.GetValue(timeoutOption),
            }
        );
        return command;
    }

    private static Command BuildToggle(CommandContext context)
    {
        var refArgument = OptionalRefArgument("Ref of a checkbox or toggle element.");
        var selectors = new SelectorOptions();
        var onOption = new Option<bool>("--on") { Description = "Ensure the element is checked." };
        var offOption = new Option<bool>("--off")
        {
            Description = "Ensure the element is unchecked.",
        };
        var timeoutOption = CreateTimeoutOption();
        var command = new Command("toggle", "Toggle a checkbox or toggle element.");
        command.Arguments.Add(refArgument);
        selectors.AddTo(command);
        command.Options.Add(onOption);
        command.Options.Add(offOption);
        command.Options.Add(timeoutOption);
        context.Attach(
            command,
            parseResult =>
                RequestBuilder.BuildToggle(
                    parseResult.GetValue(refArgument),
                    selectors.Read(parseResult),
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
        var selectors = new SelectorOptions();
        var amountOption = new Option<double>("--amount")
        {
            Description = "Scroll steps (wheel ticks or small increments).",
            DefaultValueFactory = _ => 3,
        };
        var timeoutOption = CreateTimeoutOption();
        var command = new Command("scroll", "Scroll an element or the target window.");
        command.Arguments.Add(directionArgument);
        command.Arguments.Add(refArgument);
        selectors.AddTo(command);
        command.Options.Add(amountOption);
        command.Options.Add(timeoutOption);
        context.Attach(
            command,
            parseResult => new ScrollRequest
            {
                Ref = parseResult.GetValue(refArgument),
                Selector = selectors.Read(parseResult),
                Direction = RequestBuilder.ParseDirection(
                    GetRequiredValue(parseResult, directionArgument)
                ),
                Amount = parseResult.GetValue(amountOption),
                TimeoutMs = parseResult.GetValue(timeoutOption),
            }
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
        var selectors = new SelectorOptions();
        var goneOption = new Option<bool>("--gone")
        {
            Description = "Wait for the element/text to disappear instead.",
        };
        var timeoutOption = CreateTimeoutOption();
        var command = new Command("wait", "Wait until an element or text appears (or disappears).");
        command.Arguments.Add(refArgument);
        selectors.AddTo(command);
        command.Options.Add(textOption);
        command.Options.Add(goneOption);
        command.Options.Add(timeoutOption);
        context.Attach(
            command,
            parseResult => new WaitRequest
            {
                Ref = parseResult.GetValue(refArgument),
                Selector = selectors.Read(parseResult),
                Text = parseResult.GetValue(textOption),
                Gone = parseResult.GetValue(goneOption),
                TimeoutMs = parseResult.GetValue(timeoutOption),
            }
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
                OutputPath = Path.GetFullPath(GetRequiredValue(parseResult, outputArgument)),
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
            parseResult => new WindowActionRequest
            {
                Action = RequestBuilder.ParseWindowAction(
                    GetRequiredValue(parseResult, actionArgument)
                ),
                X = parseResult.GetValue(xOption),
                Y = parseResult.GetValue(yOption),
                Width = parseResult.GetValue(widthOption),
                Height = parseResult.GetValue(heightOption),
            }
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
#if NATIVE_CLIENT
        run.SetAction(
            (Func<ParseResult, int>)(
                _ =>
                    throw new AutomationException(
                        ErrorCodes.BadRequest,
                        "The NativeAOT client cannot run the managed automation daemon in-process."
                    )
            )
        );
#else
        run.SetAction(parseResult => DaemonHost.Run(context.GetSession(parseResult)));
#endif
        daemon.Subcommands.Add(run);

        var allOption = new Option<bool>("--all") { Description = "Stop every daemon session." };
        var stop = new Command("stop", "Stop the daemon for this session, or every session.");
        stop.Options.Add(allOption);
        context.Attach(stop, _ => new ShutdownRequest());
        stop.SetAction(
            async (parseResult, cancellationToken) =>
            {
                if (!parseResult.GetValue(allOption))
                {
                    return context.Execute(parseResult, new ShutdownRequest());
                }

                try
                {
                    return context.Render(
                        parseResult,
                        await DaemonManager.StopAllAsync(cancellationToken)
                    );
                }
                catch (AutomationException ex)
                {
                    return context.Render(parseResult, DaemonResponse.Failure(ex.Code, ex.Message));
                }
            }
        );
        daemon.Subcommands.Add(stop);

        return daemon;
    }

    private sealed class SelectorOptions
    {
        private readonly Option<string?> _automationId = new("--automation-id")
        {
            Description = "Match AutomationId (case-insensitive).",
        };
        private readonly Option<string?> _name = new("--name")
        {
            Description = "Match accessible name exactly (case-insensitive).",
        };
        private readonly Option<string?> _nameContains = new("--name-contains")
        {
            Description = "Match accessible name by substring (case-insensitive).",
        };
        private readonly Option<string?> _role = new("--role")
        {
            Description = "Match an accessibility role such as button or edit.",
        };
        private readonly Option<string?> _scope = new("--scope")
        {
            Description = "Restrict the selector to a current ref's subtree.",
        };
        private readonly Option<bool> _requireUnique = new("--require-unique")
        {
            Description = "Fail unless exactly one element matches.",
        };

        public void AddTo(Command command)
        {
            command.Options.Add(_automationId);
            command.Options.Add(_name);
            command.Options.Add(_nameContains);
            command.Options.Add(_role);
            command.Options.Add(_scope);
            command.Options.Add(_requireUnique);
        }

        public ElementSelector? Read(ParseResult parseResult)
        {
            var selector = new ElementSelector
            {
                AutomationId = parseResult.GetValue(_automationId),
                Name = parseResult.GetValue(_name),
                NameContains = parseResult.GetValue(_nameContains),
                Role = parseResult.GetValue(_role),
                ScopeRef = parseResult.GetValue(_scope),
                RequireUnique = parseResult.GetValue(_requireUnique),
            };
            return selector.IsEmpty ? null : selector;
        }
    }
}
