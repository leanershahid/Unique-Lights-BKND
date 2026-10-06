using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.Exceptions;
using UniqueLights.Api.Extensions;

namespace UniqueLights.Api.Middleware;

/// <summary>
/// First middleware in the pipeline. Converts exceptions into the ApiResponse envelope and also gives
/// body-less error responses (401 challenge, 403, unmatched routes, 405...) the same envelope.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IOptions<JsonOptions> jsonOptions,
    IHostEnvironment environment)
{
    private readonly JsonSerializerOptions _json = jsonOptions.Value.JsonSerializerOptions;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);

            if (context.Response.StatusCode >= 400 && !context.Response.HasStarted &&
                context.Response.ContentLength is null && string.IsNullOrEmpty(context.Response.ContentType))
            {
                await WriteAsync(context, context.Response.StatusCode, DefaultMessage(context.Response.StatusCode), null, clearHeaders: false);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client went away; nothing to write.
            if (!context.Response.HasStarted) context.Response.StatusCode = 499;
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
        {
            logger.LogError(exception, "Unhandled exception after the response started");
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }

        var (status, message, errors) = Map(exception);

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else
        {
            logger.LogInformation("Request {Method} {Path} failed with {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, status, message);
        }

        await WriteAsync(context, status, message, errors, clearHeaders: true);
    }

    private (int Status, string Message, IReadOnlyDictionary<string, string[]>? Errors) Map(Exception exception)
    {
        switch (exception)
        {
            case Exceptions.ValidationException ve:
                return (ve.StatusCode, ve.Message, ve.Errors);
            case FluentValidation.ValidationException fve:
                return (StatusCodes.Status400BadRequest, "Validation failed", fve.Errors.ToCamelCaseDictionary());
            case AppException app:
                return (app.StatusCode, app.Message, null);
            case DbUpdateConcurrencyException:
                return (StatusCodes.Status409Conflict, "The resource was modified by another request. Please reload and try again.", null);
            case DbUpdateException { InnerException: PostgresException pg }:
                return pg.SqlState switch
                {
                    PostgresErrorCodes.UniqueViolation => (StatusCodes.Status409Conflict, "A record with the same unique value already exists.", null),
                    PostgresErrorCodes.ForeignKeyViolation => (StatusCodes.Status409Conflict, "The operation conflicts with related data.", null),
                    PostgresErrorCodes.CheckViolation => (StatusCodes.Status422UnprocessableEntity, "The operation violates a data constraint.", null),
                    _ => (StatusCodes.Status500InternalServerError, GenericError(exception), null)
                };
            case Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException:
            case NpgsqlException { IsTransient: true }:
            case InvalidOperationException { InnerException: NpgsqlException { IsTransient: true } }:
                return (StatusCodes.Status503ServiceUnavailable, "The service is temporarily unavailable. Please try again shortly.", null);
            case BadHttpRequestException bad:
                return (bad.StatusCode, bad.StatusCode == StatusCodes.Status413PayloadTooLarge ? "The request body is too large." : "Bad request.", null);
            default:
                return (StatusCodes.Status500InternalServerError, GenericError(exception), null);
        }
    }

    private string GenericError(Exception exception) =>
        environment.IsDevelopment() ? $"An unexpected error occurred: {exception.Message}" : "An unexpected error occurred.";

    private async Task WriteAsync(HttpContext context, int status, string message,
        IReadOnlyDictionary<string, string[]>? errors, bool clearHeaders)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        if (clearHeaders)
        {
            // Keep Set-Cookie (e.g. clearing the refresh cookie on a failed refresh) when resetting the response.
            var setCookie = context.Response.Headers.SetCookie;
            context.Response.Clear();
            if (setCookie.Count > 0) context.Response.Headers.SetCookie = setCookie;
        }
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        var body = ApiResponse.Fail(message, traceId, errors);
        await JsonSerializer.SerializeAsync(context.Response.Body, body, _json, context.RequestAborted);
    }

    private static string DefaultMessage(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Bad request.",
        StatusCodes.Status401Unauthorized => "Authentication is required.",
        StatusCodes.Status403Forbidden => "You do not have permission to perform this action.",
        StatusCodes.Status404NotFound => "The requested resource was not found.",
        StatusCodes.Status405MethodNotAllowed => "Method not allowed.",
        StatusCodes.Status413PayloadTooLarge => "The request body is too large.",
        StatusCodes.Status415UnsupportedMediaType => "Unsupported media type.",
        StatusCodes.Status429TooManyRequests => "Too many requests. Please try again later.",
        _ => "The request could not be completed."
    };
}
