using System.Text.Json;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Domain.Entities;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class SetProfileFieldExecutor : ILogicNodeExecutor
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public string ActionName => "SetProfileField";

    public Task ExecuteAsync(
        Node node,
        Session session,
        IReadOnlyDictionary<string, object?> latestPayload,
        CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(node.JsonContent);
        var root = doc.RootElement;

        if (!root.TryGetProperty("field", out var fieldProp) || !root.TryGetProperty("value", out var valueProp))
        {
            throw new InvalidOperationException("SetProfileField node requires 'field' and 'value' in JsonContent.");
        }

        var field = fieldProp.GetString()
            ?? throw new InvalidOperationException("'field' property cannot be null.");

        var value = valueProp.ValueKind == JsonValueKind.String
            ? valueProp.GetString()
            : valueProp.GetRawText();

        if (session.CustomerProfile is null)
        {
            throw new InvalidOperationException("Session has no associated CustomerProfile to update.");
        }

        Dictionary<string, JsonElement> metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                session.CustomerProfile.MetadataJson, _jsonOptions) ?? [];
        }
        catch (JsonException)
        {
            metadata = [];
        }

        using var valueDoc = JsonDocument.Parse(JsonSerializer.Serialize(value, _jsonOptions));
        metadata[field] = valueDoc.RootElement.Clone();

        session.CustomerProfile.MetadataJson = JsonSerializer.Serialize(metadata, _jsonOptions);

        return Task.CompletedTask;
    }
}
