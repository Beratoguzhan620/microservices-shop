using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Shop.ServiceDefaults.OpenApi;

/// <summary>Title and landing text of a service's API document; requests go through the gateway, at the same origin.</summary>
internal sealed class DocumentInfoTransformer(ServiceInfo service) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = service.Title,
            Version = "1.0",
            Description = service.Description + """


                **Errors** follow RFC 9457 problem details with a stable `code`, the same in every service, for example
                `validation_failed` or `out_of_stock`. Every response carries an `X-Correlation-Id`.
                """,
            License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://github.com/Beratoguzhan620/microservices-shop/blob/main/LICENSE") },
        };
        document.Servers = [new OpenApiServer { Url = "/" }];
        return Task.CompletedTask;
    }
}

/// <summary>Declares the bearer scheme and attaches it to every operation that requires a signed-in user.</summary>
public sealed class SecuritySchemesTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    public const string Scheme = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[Scheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "The access token from `POST /api/identity/login`.",
        };
        return Task.CompletedTask;
    }

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any())
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(Scheme, context.Document)] = [],
            });
        }

        return Task.CompletedTask;
    }
}

/// <summary>Brings the API reference in line with the portfolio's design tokens: warm neutrals, one teal accent, IBM Plex.</summary>
public static class ScalarStyle
{
    public const string Css = """
        @import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500&family=IBM+Plex+Sans:wght@400;500;600&display=swap');

        :root {
          --scalar-font: 'IBM Plex Sans', system-ui, sans-serif;
          --scalar-font-code: 'IBM Plex Mono', ui-monospace, monospace;
          --scalar-radius: 6px;
          --scalar-radius-lg: 10px;
          --scalar-radius-xl: 10px;
        }

        .light-mode {
          --scalar-background-1: #FFFFFF;
          --scalar-background-2: #F7F6F3;
          --scalar-background-3: #EFEDE8;
          --scalar-border-color: #E2DFD8;
          --scalar-color-1: #1A1A1A;
          --scalar-color-2: #6B6862;
          --scalar-color-3: #8A867F;
          --scalar-color-accent: #1F6F6A;
          --scalar-background-accent: #1F6F6A14;
          --scalar-button-1: #1F2A37;
          --scalar-button-1-hover: #2B3A4C;
          --scalar-button-1-color: #FFFFFF;
          --scalar-sidebar-background-1: #F7F6F3;
          --scalar-sidebar-item-active-background: #EFEDE8;
          --scalar-sidebar-color-active: #1A1A1A;
          --scalar-sidebar-search-background: #FFFFFF;
          --scalar-sidebar-border-color: #E2DFD8;
          --scalar-shadow-1: none;
          --scalar-shadow-2: 0 1px 2px rgba(26, 26, 26, 0.06);
        }
        """;
}
