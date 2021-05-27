using GraphQL.ApolloStudio.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GraphQL.ApolloStudio
{
    public class GraphQLTraceService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<GraphQLTraceService> _logger;

        public GraphQLTraceService(IServiceProvider serviceProvider, ILogger<GraphQLTraceService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogDebug("GraphQLTraceService is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var graphQlTraceLogger =
                        scope.ServiceProvider
                            .GetRequiredService<IGraphQLTraceLogger>();

                    // Send every 20 seconds or when forced due to size threshold reached
                    var nextExecution = DateTime.Now.AddSeconds(20);
                    await Task.WhenAny(graphQlTraceLogger.ForceSendTrigger.WaitAsync(), Task.Delay(Math.Max((int)(nextExecution - DateTime.Now).TotalMilliseconds, 0), stoppingToken));

                    _logger.LogDebug("Sending queued traces...");
                    await graphQlTraceLogger.Send();
                }
            }

            _logger.LogDebug("GraphQLTraceService is stopping.");
        }
    }
}
