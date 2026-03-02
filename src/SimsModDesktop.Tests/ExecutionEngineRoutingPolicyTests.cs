using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Models;

namespace SimsModDesktop.Tests;

public sealed class ExecutionEngineRoutingPolicyTests
{
    [Fact]
    public async Task DecideAsync_TransformationAction_UsesDefaults()
    {
        var provider = new CrossPlatformConfigurationProvider(NullLogger<CrossPlatformConfigurationProvider>.Instance);
        var sut = new ExecutionEngineRoutingPolicy(provider);

        var decision = await sut.DecideAsync(SimsAction.Flatten);

        Assert.True(decision.UseUnifiedEngine);
        Assert.True(decision.EnableFallbackToPowerShell);
        Assert.False(decision.FallbackOnValidationFailure);
    }

    [Fact]
    public async Task DecideAsync_UsesConfiguredValues()
    {
        var provider = new CrossPlatformConfigurationProvider(NullLogger<CrossPlatformConfigurationProvider>.Instance);
        await provider.SetConfigurationAsync("Execution.UseUnifiedEngine", false);
        await provider.SetConfigurationAsync("Execution.EnableFallbackToPowerShell", false);
        await provider.SetConfigurationAsync("Execution.FallbackOnValidationFailure", true);
        var sut = new ExecutionEngineRoutingPolicy(provider);

        var decision = await sut.DecideAsync(SimsAction.Merge);

        Assert.False(decision.UseUnifiedEngine);
        Assert.False(decision.EnableFallbackToPowerShell);
        Assert.True(decision.FallbackOnValidationFailure);
    }

    [Fact]
    public async Task DecideAsync_NonTransformationAction_DisablesUnified()
    {
        var provider = new CrossPlatformConfigurationProvider(NullLogger<CrossPlatformConfigurationProvider>.Instance);
        var sut = new ExecutionEngineRoutingPolicy(provider);

        var decision = await sut.DecideAsync(SimsAction.FindDuplicates);

        Assert.False(decision.UseUnifiedEngine);
    }
}
