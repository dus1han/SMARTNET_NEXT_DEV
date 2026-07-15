using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Smartnet.Api.Contracts;

/// <summary>
/// Marks every non-nullable property as <c>required</c> in the OpenAPI schema.
/// </summary>
/// <remarks>
/// Swashbuckle describes every property as optional by default, so <c>MeResponse.permissions</c> —
/// a non-nullable <c>IReadOnlyList&lt;string&gt;</c> that the server always populates — is generated
/// as <c>permissions?: string[]</c>. The frontend then null-checks a field that cannot be null.
///
/// <para>The cost is not the noise. It is that a schema where <i>everything</i> is optional cannot
/// distinguish "this may genuinely be absent" from "the tool did not know", which is precisely the
/// information a generated client exists to carry. <c>CompanyProfile.vatNumber</c> really can be
/// null; <c>CompanyProfile.name</c> really cannot. That difference should survive into TypeScript.</para>
///
/// <para>Nullability comes from the C# nullable annotation context, which is enabled solution-wide
/// (Directory.Build.props). So the schema follows the code rather than a second, hand-kept list.</para>
/// </remarks>
public sealed class RequireNonNullableSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties is null || schema.Properties.Count == 0)
        {
            return;
        }

        foreach (var (name, property) in schema.Properties)
        {
            // Swashbuckle has already resolved nullability from the C# annotations by the time a
            // schema filter runs — it is the one thing we do not have to work out ourselves.
            if (!property.Nullable)
            {
                schema.Required.Add(name);
            }
        }
    }
}
