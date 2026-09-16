using FluentValidation;
using OrderManagement.Application.Dtos;

namespace OrderManagement.Application.Validators;

public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty().WithMessage("CustomerId is required.");

        RuleFor(x => x.ShippingAddress)
            .NotEmpty().WithMessage("ShippingAddress is required.")
            .MaximumLength(500);

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Order must contain at least one item.")
            .Must(items => items.Count <= 100)
            .WithMessage("Order cannot contain more than 100 items.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId)
                .NotEmpty().WithMessage("ProductId is required.");

            // Yang paling penting: mencegah quantity negatif
            item.RuleFor(i => i.Quantity)
                .GreaterThan(0).WithMessage("Quantity must be greater than zero.")
                .LessThanOrEqualTo(1000).WithMessage("Quantity cannot exceed 1000 per item.");
        });
    }
}