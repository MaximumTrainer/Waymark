using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Application.Contracts;

public sealed class NodeDto
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public NodeType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string JsonContent { get; set; } = "{}";

    public static NodeDto FromEntity(Node node)
    {
        return new NodeDto
        {
            Id = node.Id,
            Key = node.Key,
            Type = node.Type,
            Title = node.Title,
            JsonContent = node.JsonContent
        };
    }
}
