using FluentValidation;

namespace HotelBooking.Application;

public class RegisterValidator : AbstractValidator<RegisterRequest>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password)
            .NotEmpty().MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .Matches("[A-Z]").WithMessage("Password needs an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password needs a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password needs a number.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password needs a symbol such as ! or #.");
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).Must(r => r is "Customer" or "Owner").WithMessage("Role must be Customer or Owner.");
    }
}

public class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class BookingValidator : AbstractValidator<CreateBookingRequest>
{
    public BookingValidator()
    {
        RuleFor(x => x.RoomId).NotEmpty();
        RuleFor(x => x.CheckOut.Date).GreaterThan(x => x.CheckIn.Date).WithMessage("Check-out must be after check-in.");
        RuleFor(x => x.Guests).InclusiveBetween(1, 20);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public class ReviewValidator : AbstractValidator<CreateReviewRequest>
{
    public ReviewValidator()
    {
        RuleFor(x => x.HotelId).NotEmpty();
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Comment).NotEmpty().MaximumLength(1000);
    }
}

public class CreateHotelValidator : AbstractValidator<CreateHotelRequest>
{
    public CreateHotelValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Address).NotEmpty().MaximumLength(300);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ImageUrl).Must(UrlRules.IsHttpUrl).When(x => !string.IsNullOrWhiteSpace(x.ImageUrl))
            .WithMessage("Photo link must start with http:// or https://");
    }
}

public class UpdateHotelValidator : AbstractValidator<UpdateHotelRequest>
{
    public UpdateHotelValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Address).NotEmpty().MaximumLength(300);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
    }
}

public class AddImageValidator : AbstractValidator<AddImageRequest>
{
    public AddImageValidator() =>
        RuleFor(x => x.Url).NotEmpty().MaximumLength(1000).Must(UrlRules.IsHttpUrl)
            .WithMessage("Photo link must start with http:// or https://");
}

public class CreateRoomValidator : AbstractValidator<CreateRoomRequest>
{
    public CreateRoomValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Capacity).InclusiveBetween(1, 20);
        RuleFor(x => x.PricePerNight).GreaterThan(0).LessThanOrEqualTo(100_000);
    }
}

public class UpdateRoomValidator : AbstractValidator<UpdateRoomRequest>
{
    public UpdateRoomValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Capacity).InclusiveBetween(1, 20);
        RuleFor(x => x.PricePerNight).GreaterThan(0).LessThanOrEqualTo(100_000);
    }
}

internal static class UrlRules
{
    public static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
