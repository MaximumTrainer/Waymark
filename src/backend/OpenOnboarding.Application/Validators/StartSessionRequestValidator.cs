using FluentValidation;
using OpenOnboarding.Application.Contracts;

namespace OpenOnboarding.Application.Validators;

public sealed class StartSessionRequestValidator : AbstractValidator<StartSessionRequest>
{
    public StartSessionRequestValidator()
    {
        RuleFor(x => x.FlowId).NotEmpty();
    }
}
