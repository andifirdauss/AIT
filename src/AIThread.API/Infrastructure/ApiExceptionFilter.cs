using AIThread.Common.DTOs;
using AIThread.Service.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AIThread.API.Infrastructure;

// Turns expected failures of /api endpoints into ApiResponse payloads the UI can show as-is.
public sealed class ApiExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (!context.HttpContext.Request.Path.StartsWithSegments("/api")) return;

        var (status, message) = context.Exception switch
        {
            BusinessPageConflictException ex => (StatusCodes.Status409Conflict, ex.Message),
            AppValidationException ex => (StatusCodes.Status400BadRequest, ex.Message),
            NineRouterException ex => (StatusCodes.Status502BadGateway, ex.Message),
            OrchestrationException ex => (StatusCodes.Status409Conflict, ex.Message),
            _ => (0, string.Empty)
        };
        if (status == 0) return;

        context.Result = new ObjectResult(ApiResponse<object>.Fail(message)) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
