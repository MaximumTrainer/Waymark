using System.Text.Json;
using FluentValidation;
using OpenOnboarding.Application.Contracts;

namespace OpenOnboarding.Application.Validators;

public sealed class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator()
    {
        RuleFor(x => x.ExternalCustomerId).NotEmpty();

        RuleFor(x => x.Email)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.MetadataJson)
            .Must(BeValidJson)
            .WithMessage("metadataJson must be valid JSON")
            .When(x => !string.IsNullOrWhiteSpace(x.MetadataJson));
    }

    private static bool BeValidJson(string json)
    {
        try
        {
            JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
