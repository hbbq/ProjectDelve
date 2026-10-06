using System.Text;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Web.Tests;

public sealed class ScenarioDefinitionTransportTests(ITestOutputHelper output)
{
    [Fact]
    public void FullPartyTransportPreservesSemanticRoundZeroStateAndEncodesDeterministically()
    {
        var authoredDefinition = PlaytestScenarios.Definition("full-party-trolls");
        var encoded = ScenarioDefinitionTransport.Encode(authoredDefinition);
        var decodedDefinition = ScenarioDefinitionTransport.Decode(encoded);
        ScenarioDefinitionJsonTests.AssertEquivalentRoundZeroStates(
            GameEngine.CreateGame(authoredDefinition), GameEngine.CreateGame(decodedDefinition));

        Assert.StartsWith("DELVE1:", encoded);
        Assert.DoesNotContain(encoded, char.IsWhiteSpace);
        Assert.Matches("^DELVE1:[A-Za-z0-9_-]+$", encoded);
        Assert.DoesNotContain("=", encoded);
        // Repeat across a timestamp boundary as well as immediate repeats.
        Thread.Sleep(1100);
        Assert.Equal(encoded, ScenarioDefinitionTransport.Encode(authoredDefinition));
        Assert.Equal(encoded, ScenarioDefinitionTransport.Encode(decodedDefinition));

        var payload = encoded["DELVE1:".Length..].Replace('-', '+').Replace('_', '/');
        var gzip = Convert.FromBase64String(payload.PadRight((payload.Length + 3) / 4 * 4, '='));
        output.WriteLine($"FullParty: JSON UTF-8 bytes = {Encoding.UTF8.GetByteCount(ScenarioDefinitionJson.ToJson(authoredDefinition))}; " +
            $"gzip bytes = {gzip.Length}; DELVE1 characters = {encoded.Length}");
    }
}
