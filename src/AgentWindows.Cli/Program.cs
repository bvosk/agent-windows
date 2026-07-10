using System.CommandLine;
using AgentWindows.Cli.ConsoleHost;

return await CommandTree.Build().Parse(args, CommandTree.CreateConfiguration()).InvokeAsync();
