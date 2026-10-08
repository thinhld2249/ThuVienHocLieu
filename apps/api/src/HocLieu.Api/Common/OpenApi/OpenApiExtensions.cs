using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;

namespace HocLieu.Common.OpenApi;

/// <summary>
/// Tuyên bố tham số query cho tài liệu OpenAPI (gói Microsoft.AspNetCore.OpenApi ở bản build
/// này không có AddOpenApi → generator thủ công không tự biết tham số query).
/// Dùng: <c>.WithMetadata(new QueryParameter("grade", "integer"))</c>
/// </summary>
public sealed record QueryParameter(string Name, string Type = "string", bool Required = false);

/// <summary>
/// Gói Microsoft.AspNetCore.OpenApi 8.0.x ở bản build này không có AddOpenApi()/MapOpenApi()
/// (đã xác minh hash package khớp nuget.org) → tự sinh tài liệu OpenAPI từ metadata endpoint:
/// OpenApiOperation (qua .WithOpenApi()), EndpointNameMetadata, IEndpointSummaryMetadata/
/// IEndpointDescriptionMetadata và tham số route. Chi tiết: docs/decisions.md 2026-10-07.
/// </summary>
public static class OpenApiExtensions
{
    public static IEndpointRouteBuilder MapOpenApiDocument(this IEndpointRouteBuilder app)
    {
        app.MapGet("/openapi/v1.json", (EndpointDataSource endpoints) =>
        {
            var document = Build(endpoints.Endpoints);
            var writer = new StringWriter();
            document.SerializeAsV3(new OpenApiJsonWriter(writer));
            return Results.Text(writer.ToString(), "application/json", Encoding.UTF8);
        });
        return app;
    }

    public static OpenApiDocument Build(IEnumerable<Endpoint> endpoints)
    {
        var document = new OpenApiDocument
        {
            Info = new OpenApiInfo
            {
                Title = "Học Liệu API",
                Version = "v1",
                Description = "API cổng học liệu tiểu học.",
            },
            Paths = new OpenApiPaths(),
        };

        foreach (var endpoint in endpoints)
        {
            if (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods is not { Count: > 0 } methods)
                continue;

            var path = GetRoutePath(endpoint);
            if (path is null)
                continue;

            if (!document.Paths.TryGetValue(path, out var pathItem))
            {
                pathItem = new OpenApiPathItem();
                document.Paths.Add(path, pathItem);
            }

            var operation = endpoint.Metadata.GetMetadata<OpenApiOperation>() ?? new OpenApiOperation();
            operation.OperationId ??= endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName;
            operation.Summary ??= endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary;
            operation.Description ??= endpoint.Metadata.GetMetadata<IEndpointDescriptionMetadata>()?.Description;
            if (operation.Responses.Count == 0)
                operation.Responses["200"] = new OpenApiResponse { Description = "OK" };

            foreach (Match match in RouteParameterRegex.Matches(path))
            {
                var name = match.Groups["name"].Value;
                operation.Parameters ??= [];
                if (operation.Parameters.All(p => p.Name != name))
                {
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = name,
                        In = ParameterLocation.Path,
                        Required = true,
                        Schema = new OpenApiSchema { Type = "string" },
                    });
                }
            }

            foreach (var qp in endpoint.Metadata.OfType<QueryParameter>())
            {
                operation.Parameters ??= [];
                if (operation.Parameters.All(p => p.Name != qp.Name))
                {
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = qp.Name,
                        In = ParameterLocation.Query,
                        Required = qp.Required,
                        Schema = new OpenApiSchema { Type = qp.Type },
                    });
                }
            }

            pathItem.Operations ??= new Dictionary<OperationType, OpenApiOperation>();
            foreach (var method in methods)
                pathItem.Operations[Enum.Parse<OperationType>(method, ignoreCase: true)] = operation;
        }

        return document;
    }

    private static readonly Regex RouteParameterRegex = new(@"\{(?<name>[^}]+)\}", RegexOptions.Compiled);
    private static readonly Regex ConstraintRegex = new(@"\{(?<name>[^}:]+)(?::[^}]*)?\}", RegexOptions.Compiled);

    /// <summary>
    /// Bản build này không có metadata pattern công khai → đọc thuộc tính <c>Route</c> của
    /// RouteDiagnosticsMetadata (internal, tên kiểu cố định qua nhiều bản) bằng reflection;
    /// fallback: phân tích DisplayName dạng "HTTP: GET /api/x/{id}".
    /// </summary>
    private static string? GetRoutePath(Endpoint endpoint)
    {
        foreach (var metadata in endpoint.Metadata)
        {
            var type = metadata.GetType();
            if (type.Name != "RouteDiagnosticsMetadata")
                continue;
            if (type.GetProperty("Route")?.GetValue(metadata) is string { Length: > 0 } route)
                return ConstraintRegex.Replace(route, "{${name}}");
        }

        var displayName = endpoint?.DisplayName;
        if (displayName is null)
            return null;

        var parts = displayName.Split(' ', 3);
        if (parts.Length != 3 || parts[0] is not "HTTP:" || parts[2] is not string pathPart)
            return null;
        if (!pathPart.StartsWith('/'))
            return null;
        return ConstraintRegex.Replace(pathPart, "{${name}}");
    }
}
