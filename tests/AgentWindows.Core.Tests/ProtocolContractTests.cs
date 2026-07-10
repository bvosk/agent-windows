using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using AgentWindows.Core.Model;
using AgentWindows.Core.Protocol;
using AgentWindows.Core.Session;
using Shouldly;
using Xunit;

namespace AgentWindows.Core.Tests;

public sealed class ProtocolContractTests
{
    [Fact]
    public void RequestExamples_MatchTheCurrentWireContract()
    {
        foreach (var expected in File.ReadLines(ContractPath("requests.ndjson")))
        {
            var request = ProtocolSerializer.DeserializeRequest(expected);

            request.ShouldNotBeNull();
            ProtocolSerializer.SerializeRequest(request).ShouldBe(expected);
        }
    }

    [Fact]
    public void ResponseExamples_MatchTheCurrentWireContract()
    {
        foreach (var expected in File.ReadLines(ContractPath("responses.ndjson")))
        {
            var response = ProtocolSerializer.DeserializeResponse(expected);

            response.ShouldNotBeNull();
            ProtocolSerializer.SerializeResponse(response).ShouldBe(expected);
        }
    }

    [Fact]
    public void RequestSchema_MatchesTheCheckedInContract() =>
        AssertSchema(typeof(DaemonRequest), "request.schema.json");

    [Fact]
    public void ResponseSchema_MatchesTheCheckedInContract() =>
        AssertSchema(typeof(DaemonResponse), "response.schema.json");

    [Fact]
    public void ErrorCodes_MatchTheCheckedInContract()
    {
        var actual = typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expected = File.ReadAllLines(ContractPath("error-codes.txt"));

        actual.ShouldBe(expected);
    }

    private static void AssertSchema(Type contractType, string contractFile)
    {
        var options = new JsonSerializerOptions(ProtocolJsonContext.Default.Options);
        options.Converters.Add(
            new JsonStringEnumConverter<MouseButtonKind>(JsonNamingPolicy.CamelCase)
        );
        options.Converters.Add(
            new JsonStringEnumConverter<ScrollDirection>(JsonNamingPolicy.CamelCase)
        );
        options.Converters.Add(
            new JsonStringEnumConverter<WindowActionKind>(JsonNamingPolicy.CamelCase)
        );
        var exporterOptions = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
        };
        var actual = options.GetJsonSchemaAsNode(contractType, exporterOptions);
        var expected = JsonNode.Parse(File.ReadAllText(ContractPath(contractFile)));

        JsonNode
            .DeepEquals(actual, expected)
            .ShouldBeTrue(
                "intentional protocol changes must update the checked-in schema and examples"
            );
    }

    private static string ContractPath(string fileName, [CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, "Contracts", fileName);
}
