using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using SharpCoreDB.Aspire.Hosting;

namespace Scdms.Aspire.Hosting;

/// <summary>
/// .NET Aspire extension methods that register a SCDMS web studio container and link it to a
/// SharpCoreDB server container. SCDMS talks to the server exclusively over gRPC; the link is
/// configured through the <c>SCDMS__DefaultServer*</c> environment variables that the SCDMS
/// image reads at startup (see docs/usage.md).
/// </summary>
public static class ScdmsAspireExtensions
{
    /// <summary>Published OCI image for SCDMS.</summary>
    public const string ScdmsImage = "ghcr.io/mpcoredeveloper/scdms";

    /// <summary>Default image tag used when no explicit tag is supplied.</summary>
    public const string DefaultImageTag = "latest";

    /// <summary>Default HTTP port inside the SCDMS container (see the repository Dockerfile).</summary>
    public const int DefaultHttpTargetPort = 8080;

    /// <summary>
    /// Adds a SCDMS web studio container to the Aspire application. The resource exposes an HTTP
    /// endpoint (named <see cref="ScdmsResource.HttpEndpointName"/>, container port 8080). When
    /// <paramref name="sharpCoreDb"/> is supplied the SCDMS container is linked to the SharpCoreDB
    /// server over gRPC via <see cref="WithGrpcReference"/>.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="sharpCoreDb">Optional SharpCoreDB server resource to auto-connect to.</param>
    /// <param name="imageTag">Optional container image tag (defaults to <c>latest</c>).</param>
    /// <param name="port">Optional fixed host port for the HTTP endpoint (default: allocated by Aspire).</param>
    /// <returns>The SCDMS resource builder.</returns>
    public static IResourceBuilder<ScdmsResource> AddSCDMS(
        this IDistributedApplicationBuilder builder,
        string name,
        IResourceBuilder<SharpCoreDbServerResource>? sharpCoreDb = null,
        string? imageTag = null,
        int? port = null)
    {
        var scdms = builder.AddResource(new ScdmsResource(name))
            .WithImage(ScdmsImage)
            .WithImageTag(imageTag ?? DefaultImageTag)
            .WithHttpEndpoint(targetPort: DefaultHttpTargetPort, port: port, name: ScdmsResource.HttpEndpointName)
            .WithEnvironment("SCDMS__EnableHttps", "false") // reverse proxy terminates TLS in production
            .WithEnvironment("SCDMS__BindAddress", "0.0.0.0") // bind all container interfaces
            .WithEnvironment("SCDMS__DataDirectory", "/app/data") // volume mount point for persistence
            .WithEnvironment("SCDMS__UpdateCheckEnabled", "false"); // no GitHub reachability needed in containers

        return sharpCoreDb is null ? scdms : scdms.WithGrpcReference(sharpCoreDb);
    }

    /// <summary>
    /// Links a SCDMS container to a SharpCoreDB server container. The gRPC endpoint of the server
    /// (container port 5001) is resolved and forwarded as <c>SCDMS__DefaultServerHost</c> and
    /// <c>SCDMS__DefaultServerPort</c>; TLS stays enabled and SCDMS auto-connects when the UI
    /// opens, mirroring the production compose sample in samples/docker/.
    /// </summary>
    /// <param name="scdms">The SCDMS resource builder.</param>
    /// <param name="sharpCoreDb">The SharpCoreDB server resource builder.</param>
    /// <returns>The SCDMS resource builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    public static IResourceBuilder<ScdmsResource> WithGrpcReference(
        this IResourceBuilder<ScdmsResource> scdms,
        IResourceBuilder<SharpCoreDbServerResource> sharpCoreDb)
    {
        ArgumentNullException.ThrowIfNull(scdms);
        ArgumentNullException.ThrowIfNull(sharpCoreDb);

        var grpcEndpoint = sharpCoreDb.GetEndpoint(SharpCoreDbServerResource.GrpcEndpointName);

        return scdms
            .WithEnvironment("SCDMS__DefaultServerHost", grpcEndpoint.Property(EndpointProperty.Host))
            .WithEnvironment("SCDMS__DefaultServerPort", grpcEndpoint.Property(EndpointProperty.Port))
            .WithEnvironment("SCDMS__DefaultServerUseSsl", "true")
            .WithEnvironment("SCDMS__DefaultServerAutoConnect", "true");
    }
}
