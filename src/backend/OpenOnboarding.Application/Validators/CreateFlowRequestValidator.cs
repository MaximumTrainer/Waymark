using FluentValidation;
using OpenOnboarding.Application.Contracts.Flows;
using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Application.Validators;

public sealed class CreateFlowRequestValidator : AbstractValidator<CreateFlowRequest>
{
    public CreateFlowRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Flow name is required.")
            .MaximumLength(200).WithMessage("Flow name must not exceed 200 characters.");

        RuleFor(x => x.Nodes)
            .NotEmpty().WithMessage("A flow must contain at least one node.");

        RuleForEach(x => x.Nodes).ChildRules(node =>
        {
            node.RuleFor(n => n.Key).NotEmpty().WithMessage("Node key must not be empty.");
            node.RuleFor(n => n.Type).IsInEnum().WithMessage("Node type is not valid.");
            node.RuleFor(n => n.Title).NotEmpty().WithMessage("Node title must not be empty.");
        });

        RuleFor(x => x.Nodes)
            .Must(nodes => nodes.Count(n => n.IsStartNode) == 1)
            .When(x => x.Nodes.Count > 0)
            .WithMessage("Exactly one start node is required.");

        RuleForEach(x => x.Connections).ChildRules(conn =>
        {
            conn.RuleFor(c => c.SourceNodeId).NotEmpty().WithMessage("Connection source node ID must not be empty.");
            conn.RuleFor(c => c.TargetNodeId).NotEmpty().WithMessage("Connection target node ID must not be empty.");
        });

        RuleFor(x => x)
            .Must(r =>
            {
                var nodeIds = r.Nodes.Select(n => n.Id).ToHashSet();
                return r.Connections.All(c => nodeIds.Contains(c.SourceNodeId) && nodeIds.Contains(c.TargetNodeId));
            })
            .When(x => x.Connections.Count > 0)
            .WithMessage("All connection sourceNodeId and targetNodeId values must reference nodes in this flow.");
    }
}
