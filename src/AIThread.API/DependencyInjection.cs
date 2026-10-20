using System.Text.Json.Serialization;
using AIThread.API.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AIThread.API;

public static class DependencyInjection
{
    // Makes the API controllers in this assembly discoverable by the UI host.
    public static IMvcBuilder AddApiControllers(this IMvcBuilder builder)
    {
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        builder.Services.AddScoped<AgentProxyHandler>();

        return builder
            .AddApplicationPart(typeof(DependencyInjection).Assembly)
            .AddMvcOptions(options => options.Filters.Add<ApiExceptionFilter>())
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    }
}
