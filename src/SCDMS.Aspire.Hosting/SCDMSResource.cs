using Aspire.Hosting.ApplicationModel;

namespace Scdms.Aspire.Hosting;

/// <summary>
/// A container resource representing the SCDMS web studio image
/// (<c>ghcr.io/mpcoredeveloper/scdms</c>). Inside the container SCDMS serves plain HTTP on
/// port 8080; in production a reverse proxy terminates TLS and forwards the browser traffic
/// to this endpoint. All SCDMS ⇄ SharpCoreDB data traffic flows over gRPC (server mode).
/// </summary>
public sealed class ScdmsResource(string name) : ContainerResource(name), IResourceWithConnectionString
{
    /// <summary>Name of the HTTP endpoint exposed by the SCDMS container.</summary>
    public const string HttpEndpointName = "http";

    /// <summary>Gets a reference to the HTTP endpoint of the container.</summary>
    public EndpointReference HttpEndpoint => new(this, HttpEndpointName);

    /// <inheritdoc />
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create(
            $"{HttpEndpoint.Property(EndpointProperty.Scheme)}://{HttpEndpoint.Property(EndpointProperty.Host)}:{HttpEndpoint.Property(EndpointProperty.Port)}");
}
