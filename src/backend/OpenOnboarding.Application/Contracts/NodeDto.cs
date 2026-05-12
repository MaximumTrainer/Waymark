using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Application.Contracts;

/// <summary>
/// A data transfer object representing a workflow node/step.
/// </summary>
public sealed class NodeDto
{
    /// <summary>
    /// The unique identifier of the node.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// A short machine-readable key identifying the node within a flow.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The type of node, which determines how the UI should render it.
    /// </summary>
    public NodeType Type { get; set; }

    /// <summary>
    /// The human-readable title displayed to the user for this step.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// A JSON string containing the node's configuration and content (e.g., form fields, display text).
    /// </summary>
    public string JsonContent { get; set; } = "{}";

    /// <summary>
    /// Maps a <see cref="Node"/> domain entity to a <see cref="NodeDto"/>.
    /// </summary>
    /// <param name="node">The domain entity to map from.</param>
    /// <returns>A new <see cref="NodeDto"/> populated from the entity.</returns>
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
