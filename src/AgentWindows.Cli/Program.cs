using System.CommandLine;
using AgentWindows.Cli;

return await CommandTree.Build().Parse(args, CommandTree.CreateConfiguration()).InvokeAsync();
