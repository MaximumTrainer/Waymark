using OpenOnboarding.Application.Tests.PersonaHarness;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Application.Tests;

/// <summary>
/// Validates the persona test harness against a branching compliance-onboarding flow.
///
/// Flow layout:
///   country-form
///     ├─ [Country == "USA"]  → us-ssn  → (complete)
///     └─ [Country != "USA"]  → passport-upload → (complete)
/// </summary>
public sealed class PersonaHarnessTests
{
    // ── Passing scenario ──────────────────────────────────────────────────────

    [Fact]
    public async Task PersonaRunner_ReportsPass_ForPersonaMatchingExpectedPath()
    {
        var runner = new PersonaRunner(CreateBranchingFlow);
        var persona = new PersonaDefinition
        {
            Name = "US Applicant",
            Description = "USA resident taking the SSN path",
            Steps =
            [
                new PersonaStep { Payload = new() { ["Country"] = "USA", ["FirstName"] = "Ada" } },
                new PersonaStep { Payload = new() { ["Ssn"] = "123-45-6789" } }
            ],
            ExpectedNodePath = ["country-form", "us-ssn"],
            ExpectedCompletion = true
        };

        var report = await runner.RunAllAsync([persona]);

        Assert.Single(report.Results);
        var result = report.Results[0];
        Assert.True(result.Passed, result.FailureReason);
        Assert.Equal(1, report.PassCount);
        Assert.Equal(0, report.FailCount);
    }

    // ── Failing scenario ──────────────────────────────────────────────────────

    [Fact]
    public async Task PersonaRunner_ReportsFail_ForPersonaWithWrongExpectedPath()
    {
        var runner = new PersonaRunner(CreateBranchingFlow);

        // The persona submits Country = "CA" so the flow routes to passport-upload,
        // but the expected path incorrectly declares us-ssn — this should fail.
        var persona = new PersonaDefinition
        {
            Name = "International Applicant — Wrong Expectation",
            Description = "Non-USA applicant incorrectly expected to take the SSN path",
            Steps =
            [
                new PersonaStep { Payload = new() { ["Country"] = "CA", ["FirstName"] = "Bob" } },
                new PersonaStep { Payload = new() }
            ],
            ExpectedNodePath = ["country-form", "us-ssn"],   // wrong: actual route is passport-upload
            ExpectedCompletion = true
        };

        var report = await runner.RunAllAsync([persona]);

        Assert.Single(report.Results);
        var result = report.Results[0];
        Assert.False(result.Passed);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("passport-upload", result.FailureReason);
        Assert.Equal(0, report.PassCount);
        Assert.Equal(1, report.FailCount);
    }

    // ── Full matrix + report export ───────────────────────────────────────────

    [Fact]
    public async Task PersonaRunner_ExportsReport_WithPassAndFailPersonas()
    {
        var personas = new List<PersonaDefinition>
        {
            new()
            {
                Name = "US Applicant",
                Description = "USA resident — expects SSN path",
                Steps =
                [
                    new PersonaStep { Payload = new() { ["Country"] = "USA", ["FirstName"] = "Ada" } },
                    new PersonaStep { Payload = new() { ["Ssn"] = "123-45-6789" } }
                ],
                ExpectedNodePath = ["country-form", "us-ssn"],
                ExpectedCompletion = true
            },
            new()
            {
                Name = "International Applicant — Wrong Expectation",
                Description = "Non-USA applicant with deliberately wrong expected path",
                Steps =
                [
                    new PersonaStep { Payload = new() { ["Country"] = "CA", ["FirstName"] = "Bob" } },
                    new PersonaStep { Payload = new() }
                ],
                ExpectedNodePath = ["country-form", "us-ssn"],   // intentionally wrong
                ExpectedCompletion = true
            }
        };

        var runner = new PersonaRunner(CreateBranchingFlow);
        var report = await runner.RunAllAsync(personas);

        // ── Write report for CI artifact ──────────────────────────────────────
        var reportDir = Environment.GetEnvironmentVariable("PERSONA_REPORT_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "persona-report");
        Directory.CreateDirectory(reportDir);
        await File.WriteAllTextAsync(
            Path.Combine(reportDir, "persona-report.json"),
            report.ToJson());

        // ── Assertions ────────────────────────────────────────────────────────
        Assert.Equal(2, report.TotalCount);
        Assert.Equal(1, report.PassCount);
        Assert.Equal(1, report.FailCount);

        var passing = report.Results.Single(r => r.Passed);
        Assert.Equal("US Applicant", passing.PersonaName);

        var failing = report.Results.Single(r => !r.Passed);
        Assert.NotNull(failing.FailureReason);
        Assert.Contains("passport-upload", failing.FailureReason);
    }

    // ── Flow factory ──────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a fresh branching flow for each persona run so that each run
    /// gets an independent in-memory database instance.
    /// </summary>
    private static Flow CreateBranchingFlow()
    {
        var flowId = Guid.NewGuid();

        var startNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "country-form",
            Title = "Country details",
            Type = NodeType.Form,
            IsStartNode = true,
            ComplianceRuleJson = """{"requiredFields":["Country","FirstName"]}"""
        };

        var usaNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "us-ssn",
            Title = "SSN Form",
            Type = NodeType.Form,
            ComplianceRuleJson = """{"requiredFields":["Ssn"]}"""
        };

        var passportNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "passport-upload",
            Title = "Passport Upload",
            Type = NodeType.DocumentUpload
        };

        return new Flow
        {
            Id = flowId,
            Name = "Persona harness — compliance onboarding",
            Nodes = [startNode, usaNode, passportNode],
            Connections =
            [
                new Connection
                {
                    FlowId = flowId,
                    SourceNodeId = startNode.Id,
                    TargetNodeId = usaNode.Id,
                    ConditionField = "Country",
                    ConditionOperator = ConditionOperator.Equals,
                    ConditionValue = "USA",
                    Priority = 0
                },
                new Connection
                {
                    FlowId = flowId,
                    SourceNodeId = startNode.Id,
                    TargetNodeId = passportNode.Id,
                    ConditionField = "Country",
                    ConditionOperator = ConditionOperator.NotEquals,
                    ConditionValue = "USA",
                    Priority = 1
                }
            ]
        };
    }
}
