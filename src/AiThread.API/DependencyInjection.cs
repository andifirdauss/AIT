using System.Text.Json.Serialization;
using AiThread.API.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AiThread.API;

public static class DependencyInjection
{
    // Makes the API controllers in this assembly discoverable by the UI host.
    public static IMvcBuilder AddApiControllers(this IMvcBuilder builder)
    {
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        return builder
            .AddApplicationPart(typeof(DependencyInjection).Assembly)
            .AddMvcOptions(options => options.Filters.Add<ApiExceptionFilter>())
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    }
}
