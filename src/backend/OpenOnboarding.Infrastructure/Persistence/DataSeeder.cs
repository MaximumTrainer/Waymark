using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Infrastructure.Persistence;

/// <summary>
/// Seeds example onboarding journeys in Development environments.
/// </summary>
public static class DataSeeder
{
    public static readonly Guid SmallBusinessFlowId = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid MediumBusinessFlowId = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid LargeBusinessFlowId = new("33333333-3333-3333-3333-333333333333");

    public static async Task SeedAsync(OnboardingDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var targetFlowIds = new[] { SmallBusinessFlowId, MediumBusinessFlowId, LargeBusinessFlowId };

        var existingFlowIds = await dbContext.Flows
            .Where(f => targetFlowIds.Contains(f.Id))
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

        if (!existingFlowIds.Contains(SmallBusinessFlowId))
        {
            dbContext.Flows.Add(CreateSmallBusinessFlow());
        }

        if (!existingFlowIds.Contains(MediumBusinessFlowId))
        {
            dbContext.Flows.Add(CreateMediumBusinessFlow());
        }

        if (!existingFlowIds.Contains(LargeBusinessFlowId))
        {
            dbContext.Flows.Add(CreateLargeBusinessFlow());
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static Flow CreateSmallBusinessFlow()
    {
        var startNodeId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var highValueKycNodeId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var completionNodeId = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");

        return new Flow
        {
            Id = SmallBusinessFlowId,
            Name = "Small business onboarding",
            Description = "Onboarding flow with conditional KYC step for high-value businesses (AnnualRevenue >= £1M).",
            Version = 1,
            Nodes =
            [
                new Node
                {
                    Id = startNodeId,
                    FlowId = SmallBusinessFlowId,
                    Key = "small-business-details",
                    Type = NodeType.Form,
                    Title = "Small business details",
                    JsonContent = """{"fields":[{"name":"CompanyName","type":"text","required":true},{"name":"Country","type":"text","required":true},{"name":"AnnualRevenue","type":"number","required":true}]}""",
                    ComplianceRuleJson = """{"requiredFields":["CompanyName","Country","AnnualRevenue"]}""",
                    IsStartNode = true
                },
                new Node
                {
                    Id = highValueKycNodeId,
                    FlowId = SmallBusinessFlowId,
                    Key = "high-value-kyc",
                    Type = NodeType.Form,
                    Title = "High-value KYC questionnaire",
                    JsonContent = """{"fields":[{"name":"SourceOfFunds","type":"text","required":true}]}""",
                    ComplianceRuleJson = """{"requiredFields":["SourceOfFunds"]}""",
                    IsStartNode = false
                },
                new Node
                {
                    Id = completionNodeId,
                    FlowId = SmallBusinessFlowId,
                    Key = "small-complete",
                    Type = NodeType.Information,
                    Title = "Small business onboarding checks complete.",
                    IsStartNode = false
                }
            ],
            Connections =
            [
                // High-value path: AnnualRevenue >= 1,000,000 → KYC step (evaluated first, Priority 0)
                new Connection
                {
                    Id = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                    FlowId = SmallBusinessFlowId,
                    SourceNodeId = startNodeId,
                    TargetNodeId = highValueKycNodeId,
                    ConditionField = "AnnualRevenue",
                    ConditionOperator = ConditionOperator.GreaterThanOrEqual,
                    ConditionValue = "1000000",
                    Priority = 0
                },
                // Default path: AnnualRevenue < 1,000,000 → completion (fallback, Priority 1)
                new Connection
                {
                    Id = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                    FlowId = SmallBusinessFlowId,
                    SourceNodeId = startNodeId,
                    TargetNodeId = completionNodeId,
                    Priority = 1
                }
                // high-value-kyc has no outgoing connections: submitting SourceOfFunds completes the session
            ]
        };
    }

    private static Flow CreateMediumBusinessFlow()
    {
        var startNodeId = new Guid("44444444-4444-4444-4444-444444444444");
        var documentUploadNodeId = new Guid("55555555-5555-5555-5555-555555555555");
        var experianNodeId = new Guid("66666666-6666-6666-6666-666666666666");
        var companiesHouseNodeId = new Guid("77777777-7777-7777-7777-777777777777");
        var completionNodeId = new Guid("88888888-8888-8888-8888-888888888888");

        return new Flow
        {
            Id = MediumBusinessFlowId,
            Name = "Medium business onboarding",
            Description = "Medium complexity onboarding with multi-owner and third-party verification",
            Version = 1,
            Nodes =
            [
                new Node
                {
                    Id = startNodeId,
                    FlowId = MediumBusinessFlowId,
                    Key = "medium-business-details",
                    Type = NodeType.Form,
                    Title = "Medium business details",
                    JsonContent = """{"fields":[{"name":"BusinessName","type":"text","required":true},{"name":"PrimaryAddress","type":"textarea","required":true},{"name":"AnnualRevenue","type":"number","required":true},{"name":"BusinessOwner","type":"text","required":true},{"name":"SecondaryBusinessOwner","type":"text","required":true},{"name":"NumberOfOutlets","type":"number","required":true},{"name":"BusinessStaffCount","type":"number","required":true},{"name":"BeneficialOwnersConfirmed","type":"checkbox","required":true},{"name":"TaxComplianceConfirmed","type":"checkbox","required":true}]}""",
                    ComplianceRuleJson = """{"requiredFields":["BusinessName","PrimaryAddress","AnnualRevenue","BusinessOwner","SecondaryBusinessOwner","NumberOfOutlets","BusinessStaffCount","BeneficialOwnersConfirmed","TaxComplianceConfirmed"]}""",
                    IsStartNode = true
                },
                new Node
                {
                    Id = documentUploadNodeId,
                    FlowId = MediumBusinessFlowId,
                    Key = "medium-document-verification",
                    Type = NodeType.DocumentUpload,
                    Title = "Upload ownership and trading documents",
                    JsonContent = """{"acceptedFileTypes":["application/pdf","image/jpeg","image/png"],"maxFiles":2}""",
                    IsStartNode = false
                },
                new Node
                {
                    Id = experianNodeId,
                    FlowId = MediumBusinessFlowId,
                    Key = "medium-experian-check",
                    Type = NodeType.Logic,
                    Title = "Mocked Experian verification",
                    JsonContent = """{"action":"MockVerification","provider":"Experian","resultField":"experianVerificationStatus","approved":true}""",
                    IsStartNode = false
                },
                new Node
                {
                    Id = companiesHouseNodeId,
                    FlowId = MediumBusinessFlowId,
                    Key = "medium-companies-house-check",
                    Type = NodeType.Logic,
                    Title = "Mocked Companies House verification",
                    JsonContent = """{"action":"MockVerification","provider":"CompaniesHouse","resultField":"companiesHouseVerificationStatus","approved":true}""",
                    IsStartNode = false
                },
                new Node
                {
                    Id = completionNodeId,
                    FlowId = MediumBusinessFlowId,
                    Key = "medium-complete",
                    Type = NodeType.Information,
                    Title = "Medium business onboarding checks complete.",
                    IsStartNode = false
                }
            ],
            Connections =
            [
                new Connection
                {
                    Id = new Guid("99999999-9999-9999-9999-999999999999"),
                    FlowId = MediumBusinessFlowId,
                    SourceNodeId = startNodeId,
                    TargetNodeId = documentUploadNodeId,
                    Priority = 0
                },
                new Connection
                {
                    Id = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                    FlowId = MediumBusinessFlowId,
                    SourceNodeId = documentUploadNodeId,
                    TargetNodeId = experianNodeId,
                    Priority = 0
                },
                new Connection
                {
                    Id = new Guid("ffffffff-1111-2222-3333-444444444444"),
                    FlowId = MediumBusinessFlowId,
                    SourceNodeId = experianNodeId,
                    TargetNodeId = companiesHouseNodeId,
                    Priority = 0
                },
                new Connection
                {
                    Id = new Guid("55555555-6666-7777-8888-999999999999"),
                    FlowId = MediumBusinessFlowId,
                    SourceNodeId = companiesHouseNodeId,
                    TargetNodeId = completionNodeId,
                    Priority = 0
                }
            ]
        };
    }

    private static Flow CreateLargeBusinessFlow()
    {
        var profileNodeId = new Guid("10101010-1010-1010-1010-101010101010");
        var complianceNodeId = new Guid("20202020-2020-2020-2020-202020202020");
        var experianNodeId = new Guid("30303030-3030-3030-3030-303030303030");
        var companiesHouseNodeId = new Guid("40404040-4040-4040-4040-404040404040");
        var completionNodeId = new Guid("50505050-5050-5050-5050-505050505050");

        return new Flow
        {
            Id = LargeBusinessFlowId,
            Name = "Large nationwide business onboarding",
            Description = "High complexity onboarding with legal structure and advanced compliance checks",
            Version = 1,
            Nodes =
            [
                new Node
                {
                    Id = profileNodeId,
                    FlowId = LargeBusinessFlowId,
                    Key = "large-business-profile",
                    Type = NodeType.Form,
                    Title = "Large business profile",
                    JsonContent = """{"fields":[{"name":"BusinessName","type":"text","required":true},{"name":"LegalStructure","type":"select","required":true,"options":["LimitedCompany","Partnership","PublicLimitedCompany","Other"]},{"name":"PrimaryAddress","type":"textarea","required":true},{"name":"AnnualRevenue","type":"number","required":true},{"name":"BusinessOwner","type":"text","required":true},{"name":"SecondaryBusinessOwner","type":"text","required":true},{"name":"NumberOfOutlets","type":"number","required":true},{"name":"BusinessStaffCount","type":"number","required":true}]}""",
                    ComplianceRuleJson = """{"requiredFields":["BusinessName","LegalStructure","PrimaryAddress","AnnualRevenue","BusinessOwner","SecondaryBusinessOwner","NumberOfOutlets","BusinessStaffCount"]}""",
                    IsStartNode = true
                },
                new Node
                {
                    Id = complianceNodeId,
                    FlowId = LargeBusinessFlowId,
                    Key = "large-compliance-questionnaire",
                    Type = NodeType.Form,
                    Title = "Compliance questionnaire",
                    JsonContent = """{"fields":[{"name":"RegulatoryLicensesConfirmed","type":"checkbox","required":true},{"name":"SanctionsScreeningCompleted","type":"checkbox","required":true},{"name":"BeneficialOwnershipReviewed","type":"checkbox","required":true}]}""",
                    ComplianceRuleJson = """{"requiredFields":["RegulatoryLicensesConfirmed","SanctionsScreeningCompleted","BeneficialOwnershipReviewed"]}""",
                    IsStartNode = false
                },
                new Node
                {
                    Id = experianNodeId,
                    FlowId = LargeBusinessFlowId,
                    Key = "large-experian-check",
                    Type = NodeType.Logic,
                    Title = "Mocked Experian verification",
                    JsonContent = """{"action":"MockVerification","provider":"Experian","resultField":"experianVerificationStatus","approved":true}""",
                    IsStartNode = false
                },
                new Node
                {
                    Id = companiesHouseNodeId,
                    FlowId = LargeBusinessFlowId,
                    Key = "large-companies-house-check",
                    Type = NodeType.Logic,
                    Title = "Mocked Companies House verification",
                    JsonContent = """{"action":"MockVerification","provider":"CompaniesHouse","resultField":"companiesHouseVerificationStatus","approved":true}""",
                    IsStartNode = false
                },
                new Node
                {
                    Id = completionNodeId,
                    FlowId = LargeBusinessFlowId,
                    Key = "large-complete",
                    Type = NodeType.Information,
                    Title = "Large nationwide business onboarding checks complete.",
                    IsStartNode = false
                }
            ],
            Connections =
            [
                new Connection
                {
                    Id = new Guid("60606060-6060-6060-6060-606060606060"),
                    FlowId = LargeBusinessFlowId,
                    SourceNodeId = profileNodeId,
                    TargetNodeId = complianceNodeId,
                    Priority = 0
                },
                new Connection
                {
                    Id = new Guid("70707070-7070-7070-7070-707070707070"),
                    FlowId = LargeBusinessFlowId,
                    SourceNodeId = complianceNodeId,
                    TargetNodeId = experianNodeId,
                    Priority = 0
                },
                new Connection
                {
                    Id = new Guid("80808080-8080-8080-8080-808080808080"),
                    FlowId = LargeBusinessFlowId,
                    SourceNodeId = experianNodeId,
                    TargetNodeId = companiesHouseNodeId,
                    Priority = 0
                },
                new Connection
                {
                    Id = new Guid("90909090-9090-9090-9090-909090909090"),
                    FlowId = LargeBusinessFlowId,
                    SourceNodeId = companiesHouseNodeId,
                    TargetNodeId = completionNodeId,
                    Priority = 0
                }
            ]
        };
    }
}
