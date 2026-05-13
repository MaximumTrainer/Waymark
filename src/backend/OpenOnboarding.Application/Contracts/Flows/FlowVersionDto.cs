namespace OpenOnboarding.Application.Contracts.Flows;

public record FlowVersionSummaryDto(int VersionNumber, DateTimeOffset CreatedAt, string? CreatedBy);
