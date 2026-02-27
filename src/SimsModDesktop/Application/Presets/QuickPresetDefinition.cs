using System.Text.Json.Nodes;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Presets;

public sealed record QuickPresetDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public SimsAction Action { get; init; }
    public bool AutoRun { get; init; } = true;
    public JsonObject ActionPatch { get; init; } = new();
    public JsonObject? SharedPatch { get; init; }
}
