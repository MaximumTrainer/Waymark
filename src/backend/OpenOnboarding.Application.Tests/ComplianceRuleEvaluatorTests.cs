using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

public sealed class ComplianceRuleEvaluatorTests
{
    private readonly ComplianceRuleEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_ReturnsNoViolations_WhenComplianceRuleJsonIsEmpty()
    {
        var node = MakeNode(complianceRuleJson: null);
        var violations = _evaluator.Evaluate(node, EmptyPayload(), []);
        Assert.Empty(violations);
    }

    [Fact]
    public void Evaluate_ThrowsValidationException_WhenComplianceRuleJsonIsInvalidJson()
    {
        var node = MakeNode(complianceRuleJson: "{invalid json");
        Assert.Throws<ValidationException>(() => _evaluator.Evaluate(node, EmptyPayload(), []));
    }

    // ─── requiredFields ───────────────────────────────────────────────

    [Fact]
    public void Evaluate_ReturnsViolation_WhenRequiredFieldIsMissing()
    {
        var node = MakeNode(complianceRuleJson: """{"requiredFields":["Ssn"]}""");
        var violations = _evaluator.Evaluate(node, EmptyPayload(), []);
        Assert.Single(violations);
        Assert.Equal("Ssn", violations[0].Field);
    }

    [Fact]
    public void Evaluate_ReturnsNoViolations_WhenRequiredFieldIsPresent()
    {
        var node = MakeNode(complianceRuleJson: """{"requiredFields":["Ssn"]}""");
        var violations = _evaluator.Evaluate(node, Payload("Ssn", "123-45-6789"), []);
        Assert.Empty(violations);
    }

    [Fact]
    public void Evaluate_ReturnsMultipleViolations_WhenMultipleRequiredFieldsMissing()
    {
        var node = MakeNode(complianceRuleJson: """{"requiredFields":["Ssn","Country"]}""");
        var violations = _evaluator.Evaluate(node, EmptyPayload(), []);
        Assert.Equal(2, violations.Count);
    }

    // ─── minLength / maxLength ────────────────────────────────────────

    [Fact]
    public void Evaluate_ReturnsViolation_WhenFieldIsTooShort()
    {
        var node = MakeNode(complianceRuleJson: """{"rules":[{"field":"Name","minLength":5}]}""");
        var violations = _evaluator.Evaluate(node, Payload("Name", "Ada"), []);
        Assert.Single(violations);
        Assert.Equal("Name", violations[0].Field);
    }

    [Fact]
    public void Evaluate_ReturnsNoViolations_WhenFieldMeetsMinLength()
    {
        var node = MakeNode(complianceRuleJson: """{"rules":[{"field":"Name","minLength":3}]}""");
        var violations = _evaluator.Evaluate(node, Payload("Name", "Ada"), []);
        Assert.Empty(violations);
    }

    [Fact]
    public void Evaluate_ReturnsViolation_WhenFieldExceedsMaxLength()
    {
        var node = MakeNode(complianceRuleJson: """{"rules":[{"field":"Name","maxLength":3}]}""");
        var violations = _evaluator.Evaluate(node, Payload("Name", "Alexander"), []);
        Assert.Single(violations);
        Assert.Equal("Name", violations[0].Field);
    }

    [Fact]
    public void Evaluate_ReturnsNoViolations_WhenFieldMeetsMaxLength()
    {
        var node = MakeNode(complianceRuleJson: """{"rules":[{"field":"Name","maxLength":10}]}""");
        var violations = _evaluator.Evaluate(node, Payload("Name", "Ada"), []);
        Assert.Empty(violations);
    }

    // ─── pattern ─────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_ReturnsViolation_WhenFieldDoesNotMatchPattern()
    {
        var node = MakeNode(complianceRuleJson: """{"rules":[{"field":"Ssn","pattern":"^[0-9]{3}-[0-9]{2}-[0-9]{4}$"}]}""");
        var violations = _evaluator.Evaluate(node, Payload("Ssn", "not-a-ssn"), []);
        Assert.Single(violations);
    }

    [Fact]
    public void Evaluate_ReturnsNoViolations_WhenFieldMatchesPattern()
    {
        var node = MakeNode(complianceRuleJson: """{"rules":[{"field":"Ssn","pattern":"^[0-9]{3}-[0-9]{2}-[0-9]{4}$"}]}""");
        var violations = _evaluator.Evaluate(node, Payload("Ssn", "123-45-6789"), []);
        Assert.Empty(violations);
    }

    // ─── minimum / maximum ────────────────────────────────────────────

    [Fact]
    public void Evaluate_ReturnsViolation_WhenNumericFieldBelowMinimum()
    {
        var node = MakeNode(complianceRuleJson: """{"rules":[{"field":"Revenue","minimum":1000}]}""");
        var violations = _evaluator.Evaluate(node, Payload("Revenue", "500"), []);
        Assert.Single(violations);
    }

    [Fact]
    public void Evaluate_ReturnsViolation_WhenNumericFieldAboveMaximum()
    {
        var node = MakeNode(complianceRuleJson: """{"rules":[{"field":"Revenue","maximum":1000000}]}""");
        var violations = _evaluator.Evaluate(node, Payload("Revenue", "2000000"), []);
        Assert.Single(violations);
    }

    [Fact]
    public void Evaluate_ReturnsNoViolations_WhenNumericFieldWithinRange()
    {
        var node = MakeNode(complianceRuleJson: """{"rules":[{"field":"Revenue","minimum":0,"maximum":1000000}]}""");
        var violations = _evaluator.Evaluate(node, Payload("Revenue", "500000"), []);
        Assert.Empty(violations);
    }

    // ─── allowedValues ───────────────────────────────────────────────

    [Fact]
    public void Evaluate_ReturnsViolation_WhenFieldNotInAllowedValues()
    {
        var node = MakeNode(complianceRuleJson: """{"rules":[{"field":"Country","allowedValues":["USA","UK","CA"]}]}""");
        var violations = _evaluator.Evaluate(node, Payload("Country", "DE"), []);
        Assert.Single(violations);
    }

    [Fact]
    public void Evaluate_ReturnsNoViolations_WhenFieldInAllowedValues()
    {
        var node = MakeNode(complianceRuleJson: """{"rules":[{"field":"Country","allowedValues":["USA","UK","CA"]}]}""");
        var violations = _evaluator.Evaluate(node, Payload("Country", "UK"), []);
        Assert.Empty(violations);
    }

    [Fact]
    public void Evaluate_AllowedValues_IsCaseInsensitive()
    {
        var node = MakeNode(complianceRuleJson: """{"rules":[{"field":"Country","allowedValues":["USA","UK","CA"]}]}""");
        var violations = _evaluator.Evaluate(node, Payload("Country", "usa"), []);
        Assert.Empty(violations);
    }

    // ─── crossFieldRules ─────────────────────────────────────────────

    [Fact]
    public void Evaluate_ReturnsViolation_WhenCrossFieldRuleViolated()
    {
        var node = MakeNode(complianceRuleJson: """
            {"crossFieldRules":[{"field1":"EndDate","operator":"GreaterThan","field2":"StartDate"}]}
            """);
        var payload = new Dictionary<string, object?> { ["StartDate"] = "2025-01-10", ["EndDate"] = "2025-01-05" };
        var violations = _evaluator.Evaluate(node, payload, []);
        Assert.Single(violations);
    }

    [Fact]
    public void Evaluate_ReturnsNoViolations_WhenCrossFieldRuleSatisfied()
    {
        var node = MakeNode(complianceRuleJson: """
            {"crossFieldRules":[{"field1":"EndDate","operator":"GreaterThan","field2":"StartDate"}]}
            """);
        var payload = new Dictionary<string, object?> { ["StartDate"] = "2025-01-01", ["EndDate"] = "2025-01-10" };
        var violations = _evaluator.Evaluate(node, payload, []);
        Assert.Empty(violations);
    }

    [Fact]
    public void Evaluate_CrossFieldRule_LookupsValueFromPreviousSubmissions()
    {
        var node = MakeNode(complianceRuleJson: """
            {"crossFieldRules":[{"field1":"EndDate","operator":"GreaterThan","field2":"StartDate"}]}
            """);
        var previousSubmissions = new List<Submission>
        {
            new()
            {
                SessionId = Guid.NewGuid(),
                NodeId = Guid.NewGuid(),
                DataJson = """{"StartDate":"2025-01-01"}""",
                SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            }
        };

        // EndDate is in current payload, StartDate comes from previous submission
        var payload = new Dictionary<string, object?> { ["EndDate"] = "2025-01-10" };
        var violations = _evaluator.Evaluate(node, payload, previousSubmissions);
        Assert.Empty(violations);
    }

    [Fact]
    public void Evaluate_CrossFieldRule_SkipsWhenFieldIsMissing()
    {
        var node = MakeNode(complianceRuleJson: """
            {"crossFieldRules":[{"field1":"EndDate","operator":"GreaterThan","field2":"StartDate"}]}
            """);
        // Neither field is present
        var violations = _evaluator.Evaluate(node, EmptyPayload(), []);
        Assert.Empty(violations);
    }

    // ─── all rules passing together ───────────────────────────────────

    [Fact]
    public void Evaluate_ReturnsNoViolations_WhenAllRulesPass()
    {
        var node = MakeNode(complianceRuleJson: """
            {
                "requiredFields": ["Ssn", "Country"],
                "rules": [
                    { "field": "Ssn", "pattern": "^[0-9]{3}-[0-9]{2}-[0-9]{4}$" },
                    { "field": "Revenue", "minimum": 0, "maximum": 1000000 },
                    { "field": "Name", "minLength": 2, "maxLength": 100 },
                    { "field": "Country", "allowedValues": ["USA", "UK", "CA"] }
                ],
                "crossFieldRules": [
                    { "field1": "EndDate", "operator": "GreaterThan", "field2": "StartDate" }
                ]
            }
            """);

        var payload = new Dictionary<string, object?>
        {
            ["Ssn"] = "123-45-6789",
            ["Country"] = "USA",
            ["Revenue"] = "500000",
            ["Name"] = "Ada Lovelace",
            ["StartDate"] = "2025-01-01",
            ["EndDate"] = "2025-12-31"
        };

        var violations = _evaluator.Evaluate(node, payload, []);
        Assert.Empty(violations);
    }

    [Fact]
    public void Evaluate_ReturnsAllViolations_WhenMultipleRulesFail()
    {
        var node = MakeNode(complianceRuleJson: """
            {
                "requiredFields": ["Ssn"],
                "rules": [
                    { "field": "Revenue", "minimum": 1000 },
                    { "field": "Country", "allowedValues": ["USA", "UK"] }
                ]
            }
            """);

        var payload = new Dictionary<string, object?>
        {
            ["Revenue"] = "0",
            ["Country"] = "DE"
        };

        var violations = _evaluator.Evaluate(node, payload, []);
        Assert.Equal(3, violations.Count);
    }

    private static Node MakeNode(string? complianceRuleJson = null) => new()
    {
        Id = Guid.NewGuid(),
        FlowId = Guid.NewGuid(),
        Key = "test-node",
        Type = NodeType.Form,
        Title = "Test",
        ComplianceRuleJson = complianceRuleJson
    };

    private static IReadOnlyDictionary<string, object?> EmptyPayload() =>
        new Dictionary<string, object?>();

    private static IReadOnlyDictionary<string, object?> Payload(string key, object? value) =>
        new Dictionary<string, object?> { [key] = value };
}
