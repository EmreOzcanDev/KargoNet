using KargoNet.Messaging.Consuming;
using KargoNet.Messaging.Infra;
using KargoNet.Tracking.Api.Consuming;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;

namespace KargoNet.Tracking.Tests.Consuming
{
    /// <summary>
    /// Plan 04-03: the hosted consume loop survives broker outages (channel
    /// acquisition keeps failing) without propagating the fault, and shuts
    /// down cleanly when the host cancellation token fires.
    /// </summary>
    public class ShipmentEventsConsumerHostedTests
    {
        [Fact]
        public async Task Consume_loop_survives_channel_outage_and_stops_on_cancellation()
        {
            var consumer = new ShipmentEventsConsumer(
                new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                new RabbitConsumerBroker(new ThrowingChannelFactory(), NullLogger<RabbitConsumerBroker>.Instance),
                new ThrowingChannelFactory(),
                NullLogger<ShipmentEventsConsumer>.Instance);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            // .NET 10: StartAsync returns once the service has started; the
            // lifetime task is ExecuteTask.
            await consumer.StartAsync(cts.Token);
            await consumer.ExecuteTask;

            Assert.True(cts.IsCancellationRequested);
        }

        private sealed class ThrowingChannelFactory : LazyChannelFactory
        {
            public ThrowingChannelFactory()
                : base(new ConfigurationBuilder().Build())
            {
            }

            public override Task<IChannel> GetChannelAsync()
                => Task.FromException<IChannel>(new InvalidOperationException("no broker"));
        }
    }
}
