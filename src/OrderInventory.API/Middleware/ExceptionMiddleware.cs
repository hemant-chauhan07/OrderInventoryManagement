using Microsoft.AspNetCore.Mvc;
using OrderInventory.Application.Exceptions;

namespace OrderInventory.API.Middleware
{
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;

        public ExceptionMiddleware(
            RequestDelegate next,
            ILogger<ExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (ApiException exception)
            {
                _logger.LogWarning(
                    "API error {StatusCode}: {Message}",
                    exception.StatusCode,
                    exception.Message);

                await WriteProblemDetails(
                    context,
                    exception.StatusCode,
                    exception.Message);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Unhandled exception occurred.");

                await WriteProblemDetails(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "An unexpected error occurred.");
            }
        }

        private static async Task WriteProblemDetails(
            HttpContext context,
            int statusCode,
            string message)
        {
            if (context.Response.HasStarted)
            {
                return;
            }

            context.Response.Clear();

            context.Response.StatusCode = statusCode;

            context.Response.ContentType =
                "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = statusCode,
                Title = GetTitle(statusCode),
                Detail = message,
                Instance = context.Request.Path
            };

            problem.Extensions["traceId"] =
                context.TraceIdentifier;

            await context.Response.WriteAsJsonAsync(
                problem,
                cancellationToken: context.RequestAborted);
        }

        private static string GetTitle(int statusCode)
        {
            return statusCode switch
            {
                400 => "Bad Request",
                401 => "Unauthorized",
                403 => "Forbidden",
                404 => "Not Found",
                409 => "Conflict",
                429 => "Too Many Requests",
                _ => "Internal Server Error"
            };
        }
    }
}
