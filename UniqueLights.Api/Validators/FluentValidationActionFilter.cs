using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using UniqueLights.Api.Extensions;

namespace UniqueLights.Api.Validators;

/// <summary>
/// Global MVC action filter that runs the registered FluentValidation validator (if any) for every
/// action argument (body, query objects, form models). Failures become a 400 envelope with camelCase keys.
/// </summary>
public sealed class FluentValidationActionFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator) continue;

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);
            if (!result.IsValid) failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
        {
            throw new Exceptions.ValidationException(failures.ToCamelCaseDictionary());
        }

        await next();
    }
}
