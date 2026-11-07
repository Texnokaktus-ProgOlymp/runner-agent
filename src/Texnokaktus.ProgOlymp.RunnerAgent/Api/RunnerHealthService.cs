using Grpc.Core;
using Grpc.Health.V1;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Api;

internal sealed class RunnerHealthService(IHostApplicationLifetime lifetime) : Health.HealthBase
{
    private static bool Known(string service) =>
        service.Length == 0 || service == "texnokaktus.progolymp.runner.v1.Runner";

    private HealthCheckResponse Response => new()
    {
        Status = lifetime.ApplicationStopping.IsCancellationRequested
                     ? HealthCheckResponse.Types.ServingStatus.NotServing
                     : HealthCheckResponse.Types.ServingStatus.Serving
    };

    public override Task<HealthCheckResponse> Check(HealthCheckRequest request, ServerCallContext context) =>
        Known(request.Service)
            ? Task.FromResult(Response)
            : throw new RpcException(new Status(StatusCode.NotFound, "Unknown service."));

    public override async Task Watch(
        HealthCheckRequest request,
        IServerStreamWriter<HealthCheckResponse> responseStream,
        ServerCallContext context
    )
    {
        await responseStream.WriteAsync(
            Known(request.Service)
                ? Response
                : new HealthCheckResponse { Status = HealthCheckResponse.Types.ServingStatus.ServiceUnknown }
        );

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken,
            lifetime.ApplicationStopping
        );

        try
        {
            await Task.Delay(Timeout.Infinite, cancellation.Token);
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            if (Known(request.Service)) await responseStream.WriteAsync(Response);
        }
    }
}
