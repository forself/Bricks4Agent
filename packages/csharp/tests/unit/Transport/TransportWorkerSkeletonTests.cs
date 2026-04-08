using TransportTdxWorker.Handlers;

namespace Unit.Tests.Transport;

public class TransportWorkerSkeletonTests
{
    [Fact]
    public void TransportQueryHandler_exposes_transport_query_capability()
    {
        var handler = new TransportQueryHandler();

        handler.CapabilityId.Should().Be("transport.query");
    }
}
