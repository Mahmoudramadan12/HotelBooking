using FluentValidation;
using HotelBooking.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace HotelBooking.API;

public static class ClaimsExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new UnauthorizedAccessException("You are not signed in.");

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole("Admin");
    public static string GetFullName(this ClaimsPrincipal user) => user.FindFirstValue("full_name") ?? "Guest";
    public static Actor ToActor(this ClaimsPrincipal user) => new(user.GetUserId(), user.IsAdmin());
    public static Actor? ToActorOrNull(this ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true ? user.ToActor() : null;
}

/// Runs the FluentValidation validator for every action argument that has one registered.
/// (Registering validators alone does not validate anything in ASP.NET Core.)
public class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;
            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (result.IsValid) continue;

            var errors = result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(errors) { Title = "Please check the highlighted details." });
            return;
        }
        await next();
    }
}

/// Turns exceptions into JSON <c>{ "error": "..." }</c> with the right status code. Never leaks internals on 500.
public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task Invoke(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            ctx.Response.StatusCode = 499; // client closed the request
        }
        catch (Exception ex)
        {
            var (status, message) = ex switch
            {
                UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, ex.Message),
                ForbiddenException => (StatusCodes.Status403Forbidden, ex.Message),
                NotFoundException or KeyNotFoundException => (StatusCodes.Status404NotFound, ex.Message),
                ConflictException => (StatusCodes.Status409Conflict, ex.Message),
                InvalidOperationException => (StatusCodes.Status400BadRequest, ex.Message),
                _ => (StatusCodes.Status500InternalServerError, "Something went wrong on our side. Please try again.")
            };

            if (status >= 500) logger.LogError(ex, "Unhandled exception on {Path}", ctx.Request.Path);
            else logger.LogInformation("{Type} on {Path}: {Message}", ex.GetType().Name, ctx.Request.Path, ex.Message);

            if (ctx.Response.HasStarted) throw;
            ctx.Response.StatusCode = status;
            await ctx.Response.WriteAsJsonAsync(new { error = message });
        }
    }
}
