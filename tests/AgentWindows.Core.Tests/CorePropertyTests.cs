using System.Globalization;
using AgentWindows.Core.Input;
using AgentWindows.Core.Model;
using AgentWindows.Core.Protocol;
using CsCheck;
using Shouldly;
using Xunit;

namespace AgentWindows.Core.Tests;

public sealed class CorePropertyTests
{
    private const int _iterations = 200;

    [Fact]
    public void ElementRefs_NormalizeDisplayAndRecoverTheirIndex()
    {
        Gen.Int.NonNegative.Sample(
            index =>
            {
                var bareRef = $"e{index.ToString(CultureInfo.InvariantCulture)}";
                var displayedRef = ElementRef.Display(bareRef);

                ElementRef.TryNormalize(bareRef, out var normalizedBare).ShouldBeTrue();
                ElementRef.TryNormalize(displayedRef, out var normalizedDisplay).ShouldBeTrue();
                ElementRef.TryGetIndex(normalizedDisplay, out var recoveredIndex).ShouldBeTrue();

                normalizedBare.ShouldBe(bareRef);
                normalizedDisplay.ShouldBe(bareRef);
                recoveredIndex.ShouldBe(index);
            },
            iter: _iterations,
            threads: 1
        );
    }

    [Fact]
    public void KeyChords_PreserveKeysAndDeduplicateModifierAliases()
    {
        var modifierAlias = Gen.OneOf(
            Gen.Const(("Ctrl", KeyModifier.Ctrl)),
            Gen.Const(("Control", KeyModifier.Ctrl)),
            Gen.Const(("Shift", KeyModifier.Shift)),
            Gen.Const(("Alt", KeyModifier.Alt)),
            Gen.Const(("Win", KeyModifier.Win)),
            Gen.Const(("Windows", KeyModifier.Win)),
            Gen.Const(("Meta", KeyModifier.Win))
        );
        var keyToken = Gen.String[Gen.Char.AlphaNumeric, 1, 24];

        Gen.Select(modifierAlias, Gen.Int[1, 8], keyToken)
            .Sample(
                sample =>
                {
                    var ((alias, expectedModifier), repeat, key) = sample;
                    var input = $"{string.Join('+', Enumerable.Repeat(alias, repeat))}+{key}";

                    KeyChord.TryParse(input, out var chord).ShouldBeTrue();
                    chord.ShouldNotBeNull();
                    chord.Modifiers.ShouldBe([expectedModifier]);
                    chord.Key.ShouldBe(key);
                },
                iter: _iterations,
                threads: 1
            );
    }

    [Fact]
    public void PressRequests_RoundTripArbitraryJsonStringContent()
    {
        var validCharacter = Gen.OneOf(
            Gen.Char[char.MinValue, '\uD7FF'],
            Gen.Char['\uE000', char.MaxValue]
        );

        Gen.String[validCharacter, 0, 64]
            .Sample(
                keys =>
                {
                    var request = new PressRequest { Keys = keys };
                    var json = ProtocolSerializer.SerializeRequest(request);
                    var roundTripped = ProtocolSerializer
                        .DeserializeRequest(json)
                        .ShouldBeOfType<PressRequest>();

                    roundTripped.Keys.ShouldBe(keys);
                    ProtocolSerializer.SerializeRequest(roundTripped).ShouldBe(json);
                },
                iter: _iterations,
                threads: 1
            );
    }

    [Fact]
    public void ClickRequests_RoundTripNumericAndEnumBoundaries()
    {
        var button = Gen.OneOf(
            Gen.Const(MouseButtonKind.Left),
            Gen.Const(MouseButtonKind.Right),
            Gen.Const(MouseButtonKind.Middle)
        );

        Gen.Select(Gen.Int, Gen.Int, Gen.Int, button)
            .Sample(
                sample =>
                {
                    var (x, y, timeoutMs, mouseButton) = sample;
                    var request = new ClickRequest
                    {
                        X = x,
                        Y = y,
                        TimeoutMs = timeoutMs,
                        Button = mouseButton,
                    };
                    var json = ProtocolSerializer.SerializeRequest(request);
                    var roundTripped = ProtocolSerializer
                        .DeserializeRequest(json)
                        .ShouldBeOfType<ClickRequest>();

                    roundTripped.X.ShouldBe(x);
                    roundTripped.Y.ShouldBe(y);
                    roundTripped.TimeoutMs.ShouldBe(timeoutMs);
                    roundTripped.Button.ShouldBe(mouseButton);
                    ProtocolSerializer.SerializeRequest(roundTripped).ShouldBe(json);
                },
                iter: _iterations,
                threads: 1
            );
    }
}
