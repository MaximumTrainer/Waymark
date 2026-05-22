using System.Text.Json;
using System.Text.RegularExpressions;
using FluentValidation;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Domain.Entities;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class ComplianceRuleEvaluator : IComplianceRuleEvaluator
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<ComplianceViolation> Evaluate(
        Node node,
        IReadOnlyDictionary<string, object?> payload,
        IReadOnlyList<Submission> previousSubmissions)
    {
        if (string.IsNullOrWhiteSpace(node.ComplianceRuleJson))
        {
            return [];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(node.ComplianceRuleJson);
        }
        catch (JsonException)
        {
            throw new ValidationException($"Compliance rule configuration is invalid for node '{node.Key}'.");
        }

        var violations = new List<ComplianceViolation>();

        using (document)
        {
            var root = document.RootElement;

            EvaluateRequiredFields(root, payload, violations);
            EvaluateFieldRules(root, payload, violations);
            EvaluateCrossFieldRules(root, payload, previousSubmissions, violations);
        }

        return violations;
    }

    private static void EvaluateRequiredFields(
        JsonElement root,
        IReadOnlyDictionary<string, object?> payload,
        List<ComplianceViolation> violations)
    {
        if (!root.TryGetProperty("requiredFields", out var requiredFields) ||
            requiredFields.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var element in requiredFields.EnumerateArray())
        {
            var fieldName = element.GetString();
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                continue;
            }

            if (!payload.TryGetValue(fieldName, out var value) ||
                value is null ||
                string.IsNullOrWhiteSpace(value.ToString()))
            {
                violations.Add(new ComplianceViolation
                {
                    Field = fieldName,
                    Message = $"Compliance rule failed: '{fieldName}' is required."
                });
            }
        }
    }

    private static void EvaluateFieldRules(
        JsonElement root,
        IReadOnlyDictionary<string, object?> payload,
        List<ComplianceViolation> violations)
    {
        if (!root.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var rule in rules.EnumerateArray())
        {
            if (!rule.TryGetProperty("field", out var fieldProp))
            {
                continue;
            }

            var fieldName = fieldProp.GetString();
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                continue;
            }

            payload.TryGetValue(fieldName, out var rawValue);
            var fieldValue = rawValue?.ToString();

            EvaluateMinLength(rule, fieldName, fieldValue, violations);
            EvaluateMaxLength(rule, fieldName, fieldValue, violations);
            EvaluatePattern(rule, fieldName, fieldValue, violations);
            EvaluateMinimum(rule, fieldName, fieldValue, violations);
            EvaluateMaximum(rule, fieldName, fieldValue, violations);
            EvaluateAllowedValues(rule, fieldName, fieldValue, violations);
        }
    }

    private static void EvaluateMinLength(JsonElement rule, string fieldName, string? fieldValue, List<ComplianceViolation> violations)
    {
        if (!rule.TryGetProperty("minLength", out var minLengthProp) || !minLengthProp.TryGetInt32(out var minLength))
        {
            return;
        }

        if (fieldValue is not null && fieldValue.Length < minLength)
        {
            violations.Add(new ComplianceViolation
            {
                Field = fieldName,
                Message = $"'{fieldName}' must be at least {minLength} characters long."
            });
        }
    }

    private static void EvaluateMaxLength(JsonElement rule, string fieldName, string? fieldValue, List<ComplianceViolation> violations)
    {
        if (!rule.TryGetProperty("maxLength", out var maxLengthProp) || !maxLengthProp.TryGetInt32(out var maxLength))
        {
            return;
        }

        if (fieldValue is not null && fieldValue.Length > maxLength)
        {
            violations.Add(new ComplianceViolation
            {
                Field = fieldName,
                Message = $"'{fieldName}' must be at most {maxLength} characters long."
            });
        }
    }

    private static void EvaluatePattern(JsonElement rule, string fieldName, string? fieldValue, List<ComplianceViolation> violations)
    {
        if (!rule.TryGetProperty("pattern", out var patternProp))
        {
            return;
        }

        var pattern = patternProp.GetString();
        if (pattern is null || fieldValue is null)
        {
            return;
        }

        bool matches;
        try
        {
            matches = Regex.IsMatch(fieldValue, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
        }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or ArgumentException)
        {
            matches = false;
        }

        if (!matches)
        {
            violations.Add(new ComplianceViolation
            {
                Field = fieldName,
                Message = $"'{fieldName}' does not match the required pattern."
            });
        }
    }

    private static void EvaluateMinimum(JsonElement rule, string fieldName, string? fieldValue, List<ComplianceViolation> violations)
    {
        if (!rule.TryGetProperty("minimum", out var minimumProp) || !minimumProp.TryGetDecimal(out var minimum))
        {
            return;
        }

        if (fieldValue is null) return;
        if (!decimal.TryParse(fieldValue, out var numericValue) || numericValue < minimum)
        {
            violations.Add(new ComplianceViolation
            {
                Field = fieldName,
                Message = $"'{fieldName}' must be at least {minimum}."
            });
        }
    }

    private static void EvaluateMaximum(JsonElement rule, string fieldName, string? fieldValue, List<ComplianceViolation> violations)
    {
        if (!rule.TryGetProperty("maximum", out var maximumProp) || !maximumProp.TryGetDecimal(out var maximum))
        {
            return;
        }

        if (fieldValue is null) return;
        if (!decimal.TryParse(fieldValue, out var numericValue) || numericValue > maximum)
        {
            violations.Add(new ComplianceViolation
            {
                Field = fieldName,
                Message = $"'{fieldName}' must be at most {maximum}."
            });
        }
    }

    private static void EvaluateAllowedValues(JsonElement rule, string fieldName, string? fieldValue, List<ComplianceViolation> violations)
    {
        if (!rule.TryGetProperty("allowedValues", out var allowedValuesProp) || allowedValuesProp.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var allowedValues = allowedValuesProp
            .EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => x is not null)
            .ToList();

        if (fieldValue is not null && !allowedValues.Any(v => string.Equals(v, fieldValue, StringComparison.OrdinalIgnoreCase)))
        {
            violations.Add(new ComplianceViolation
            {
                Field = fieldName,
                Message = $"'{fieldName}' must be one of: {string.Join(", ", allowedValues)}."
            });
        }
    }

    private void EvaluateCrossFieldRules(
        JsonElement root,
        IReadOnlyDictionary<string, object?> payload,
        IReadOnlyList<Submission> previousSubmissions,
        List<ComplianceViolation> violations)
    {
        if (!root.TryGetProperty("crossFieldRules", out var crossFieldRules) || crossFieldRules.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var extendedValues = BuildExtendedValues(payload, previousSubmissions);

        foreach (var crossRule in crossFieldRules.EnumerateArray())
        {
            if (!crossRule.TryGetProperty("field1", out var field1Prop) ||
                !crossRule.TryGetProperty("operator", out var operatorProp) ||
                !crossRule.TryGetProperty("field2", out var field2Prop))
            {
                continue;
            }

            var field1 = field1Prop.GetString();
            var operatorStr = operatorProp.GetString();
            var field2 = field2Prop.GetString();

            if (string.IsNullOrWhiteSpace(field1) || string.IsNullOrWhiteSpace(operatorStr) || string.IsNullOrWhiteSpace(field2))
            {
                continue;
            }

            extendedValues.TryGetValue(field1, out var value1);
            extendedValues.TryGetValue(field2, out var value2);

            if (value1 is null || value2 is null)
            {
                // Cannot evaluate — skip if either field is missing
                continue;
            }

            if (!EvaluateCrossFieldComparison(value1, operatorStr, value2))
            {
                violations.Add(new ComplianceViolation
                {
                    Field = field1,
                    Message = $"'{field1}' must be {operatorStr} '{field2}'."
                });
            }
        }
    }

    private Dictionary<string, string?> BuildExtendedValues(
        IReadOnlyDictionary<string, object?> payload,
        IReadOnlyList<Submission> previousSubmissions)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var submission in previousSubmissions.OrderBy(x => x.SubmittedAt))
        {
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(submission.DataJson, _jsonOptions);
                if (data is not null)
                {
                    foreach (var (key, value) in data)
                    {
                        result[key] = value.ValueKind == JsonValueKind.Null ? null : value.ToString();
                    }
                }
            }
            catch (JsonException) { }
        }

        foreach (var (key, value) in payload)
        {
            result[key] = value?.ToString();
        }

        return result;
    }

    private static bool EvaluateCrossFieldComparison(string value1, string operatorStr, string value2)
    {
        if (decimal.TryParse(value1, out var num1) && decimal.TryParse(value2, out var num2))
        {
            return operatorStr switch
            {
                "GreaterThan" => num1 > num2,
                "LessThan" => num1 < num2,
                "GreaterThanOrEqual" => num1 >= num2,
                "LessThanOrEqual" => num1 <= num2,
                "Equals" => num1 == num2,
                "NotEquals" => num1 != num2,
                _ => false
            };
        }

        if (DateTimeOffset.TryParse(value1, out var date1) && DateTimeOffset.TryParse(value2, out var date2))
        {
            return operatorStr switch
            {
                "GreaterThan" => date1 > date2,
                "LessThan" => date1 < date2,
                "GreaterThanOrEqual" => date1 >= date2,
                "LessThanOrEqual" => date1 <= date2,
                "Equals" => date1 == date2,
                "NotEquals" => date1 != date2,
                _ => false
            };
        }

        var comparison = string.Compare(value1, value2, StringComparison.OrdinalIgnoreCase);
        return operatorStr switch
        {
            "GreaterThan" => comparison > 0,
            "LessThan" => comparison < 0,
            "GreaterThanOrEqual" => comparison >= 0,
            "LessThanOrEqual" => comparison <= 0,
            "Equals" => comparison == 0,
            "NotEquals" => comparison != 0,
            _ => false
        };
    }
}
