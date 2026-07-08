using AgentWindows.Core.Protocol;

namespace AgentWindows.E2e.Tests;

public sealed record CliResult
{
    public required int ExitCode { get; init; }

    public required string StandardOutput { get; init; }

    public required string StandardError { get; init; }

    public DaemonResponse? Response { get; init; }

    /// <summary>Asserts the command succeeded and returns its payload.</summary>
    public ResponsePayload? ShouldSucceed() =>
        Response is { Ok: true }
            ? Response.Payload
            : throw new InvalidOperationException(
                $"Expected a successful response but got exit code {ExitCode}. "
                    + $"stdout: {StandardOutput} stderr: {StandardError}"
            );

    public T ShouldSucceedWith<T>()
        where T : ResponsePayload =>
        ShouldSucceed() as T
        ?? throw new InvalidOperationException(
            $"Expected a {typeof(T).Name} payload. stdout: {StandardOutput}"
        );
}
