using FluentValidation;
using OrderManagement.Application.Dtos;

namespace OrderManagement.Application.Validators;

public class UpdateStatusRequestValidator : AbstractValidator<UpdateStatusRequest>
{
    public UpdateStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Invalid order status.");
    }
}