using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Nelsar.Axia.Api.Middleware;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Nelsar.Axia.Api.Swagger;

public sealed class ProviderHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Parameters ??= new List<OpenApiParameter>();

        // Add optional header param for X-LDAP-Provider-Id to help API consumers select provider
        if (!operation.Parameters.Any(p => p.Name == ProviderHeaderMiddleware.HeaderName))
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = ProviderHeaderMiddleware.HeaderName,
                In = ParameterLocation.Header,
                Required = false,
                // Two audiences in one line, because the header means different things to each.
                // On sign-in it chooses; afterwards the session has already chosen, and this can
                // only agree — a reader who does not know that meets it as an unexplained 403.
                Description =
                    "Selects the LDAP directory. On sign-in it chooses which directory to "
                    + "authenticate against. On an authenticated request it is optional — the "
                    + "session already names its directory — and naming a different one is "
                    + "refused with 403; sign in again to switch.",
                Schema = new OpenApiSchema { Type = "string" }
            });
        }
    }
}
