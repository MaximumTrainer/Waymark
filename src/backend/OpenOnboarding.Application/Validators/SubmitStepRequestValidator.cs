using FluentValidation;
using OpenOnboarding.Application.Contracts;

namespace OpenOnboarding.Application.Validators;

public sealed class SubmitStepRequestValidator : AbstractValidator<SubmitStepRequest>
{
    public SubmitStepRequestValidator()
    {
        RuleFor(x => x.Payload).NotNull();
    }
}
