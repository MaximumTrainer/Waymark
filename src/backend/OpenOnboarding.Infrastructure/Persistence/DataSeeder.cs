using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Infrastructure.Persistence;

/// <summary>
/// Seeds the example compliance onboarding flow (from flow-definition.example.json) in
/// Development environments. Skips silently if the flow already exists.
/// </summary>
public static class DataSeeder
{
    // These GUIDs match flow-definition.example.json so the frontend can start immediately.
    private static readonly Guid FlowId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CountryFormNodeId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UsSsnNodeId = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PassportUploadNodeId = new("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid HighValueKycNodeId = new("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly Guid ConnectionUsaId = new("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid ConnectionNonUsaId = new("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ConnectionHighValueId = new("11111111-2222-3333-4444-555555555555");

    public static async Task SeedAsync(OnboardingDbContext dbContext, CancellationToken cancellationToken = default)
    {
        if (await dbContext.Flows.AnyAsync(f => f.Id == FlowId, cancellationToken))
        {
            return;
        }

        var flow = new Flow
        {
            Id = FlowId,
            Name = "Compliance Onboarding",
            Description = "Branch by country and collect required tax identity",
            Version = 1,
            Nodes =
            [
                new Node
                {
                    Id = CountryFormNodeId,
                    FlowId = FlowId,
                    Key = "country-form",
                    Type = NodeType.Form,
                    Title = "Tell us about your business",
                    JsonContent = """{"fields":[{"name":"CompanyName","type":"text","required":true},{"name":"Country","type":"select","required":true},{"name":"AnnualRevenue","type":"number","required":false}]}""",
                    ComplianceRuleJson = """{"requiredFields":["CompanyName","Country"]}""",
                    IsStartNode = true
                },
                new Node
                {
                    Id = UsSsnNodeId,
                    FlowId = FlowId,
                    Key = "us-ssn-form",
                    Type = NodeType.Form,
                    Title = "US Tax Verification",
                    JsonContent = """{"fields":[{"name":"Ssn","type":"text","required":true}]}""",
                    ComplianceRuleJson = """{"requiredFields":["Ssn"]}""",
                    IsStartNode = false
                },
                new Node
                {
                    Id = PassportUploadNodeId,
                    FlowId = FlowId,
                    Key = "passport-upload",
                    Type = NodeType.DocumentUpload,
                    Title = "Passport upload",
                    JsonContent = """{"acceptedFileTypes":["application/pdf","image/jpeg","image/png"]}""",
                    IsStartNode = false
                },
                new Node
                {
                    Id = HighValueKycNodeId,
                    FlowId = FlowId,
                    Key = "high-value-kyc",
                    Type = NodeType.Form,
                    Title = "Enhanced KYC — High Value Customer",
                    JsonContent = """{"fields":[{"name":"SourceOfFunds","type":"text","required":true},{"name":"PoliticallyExposed","type":"checkbox","required":false}]}""",
                    ComplianceRuleJson = """{"requiredFields":["SourceOfFunds"]}""",
                    IsStartNode = false
                }
            ],
            Connections =
            [
                new Connection
                {
                    Id = ConnectionHighValueId,
                    FlowId = FlowId,
                    SourceNodeId = CountryFormNodeId,
                    TargetNodeId = HighValueKycNodeId,
                    ConditionField = "AnnualRevenue",
                    ConditionOperator = ConditionOperator.GreaterThan,
                    ConditionValue = "1000000",
                    Priority = 0
                },
                new Connection
                {
                    Id = ConnectionUsaId,
                    FlowId = FlowId,
                    SourceNodeId = CountryFormNodeId,
                    TargetNodeId = UsSsnNodeId,
                    ConditionField = "Country",
                    ConditionOperator = ConditionOperator.Equals,
                    ConditionValue = "USA",
                    Priority = 1
                },
                new Connection
                {
                    Id = ConnectionNonUsaId,
                    FlowId = FlowId,
                    SourceNodeId = CountryFormNodeId,
                    TargetNodeId = PassportUploadNodeId,
                    ConditionField = "Country",
                    ConditionOperator = ConditionOperator.NotEquals,
                    ConditionValue = "USA",
                    Priority = 2
                }
            ]
        };

        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
