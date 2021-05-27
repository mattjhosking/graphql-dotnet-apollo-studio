using GraphQL;
using GraphQL.Types;
using GraphQL.Utilities;
using Microsoft.AspNetCore.Http;
using ProtoBuf;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using GraphQL.ApolloStudio;

namespace GraphQL.ApolloStudio.Logging
{
    public class ApolloReportingTraceLogger : IGraphQLTraceLogger
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _apiKey;
        private readonly ILogger<ApolloReportingTraceLogger> _logger;
        private readonly ReportHeader _reportHeader;
        private readonly object _tracesLock = new object();
        private ConcurrentDictionary<string, TracesAndStats> _traces = new ConcurrentDictionary<string, TracesAndStats>();
        private readonly MetricsToTraceConverter _metricsToTraceConverter = new MetricsToTraceConverter();
        private const int BATCH_THRESHOLD_SIZE = 2 * 1024 * 1024; // Send batches at 2mb so we stay well below the 4mb limit recommended

        public ApolloReportingTraceLogger(IHttpClientFactory httpClientFactory, ISchema schema, string apiKey, string graphRef, ILogger<ApolloReportingTraceLogger> logger)
        {
            _httpClientFactory = httpClientFactory;
            _apiKey = apiKey;
            _logger = logger;
            _reportHeader = new ReportHeader
            {
                Hostname = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME") ?? Environment.MachineName,
                AgentVersion = "engineproxy 0.1.0",
                ServiceVersion = Assembly.GetExecutingAssembly().FullName,
                RuntimeVersion = $".NET Core {Environment.Version}",
                Uname = Environment.OSVersion.ToString(),
                GraphRef = graphRef,
                ExecutableSchemaId = ComputeSha256Hash(new SchemaPrinter(schema).Print())
            };
        }

        public void LogTrace(HttpContext httpContext, GraphQLRequest query, ExecutionResult result)
        {
            Trace? trace = _metricsToTraceConverter.CreateTrace(result);

            if (trace != null)
            {
                var userAgent = (httpContext.Request.Headers.TryGetValue("User-Agent", out var userAgentHeader) ? userAgentHeader.ToString() : "Unknown/Unknown").Split('/');
                trace.ClientName = httpContext.Request.Headers.TryGetValue("apollographql-client-name", out var clientName) ? clientName.ToString() : userAgent.First();
                trace.ClientVersion = httpContext.Request.Headers.TryGetValue("apollographql-client-version", out var clientVersion) ? clientVersion.ToString() : userAgent.Last();

                lock (_tracesLock)
                {
                    var tracesAndStats = _traces.GetOrAdd($"# {(string.IsNullOrWhiteSpace(query.OperationName) ? "-" : query.OperationName)}\n{MinimalWhitespace(query.Query)}",
                        key => new TracesAndStats());
                    tracesAndStats.Traces.Add(trace);

                    // Trigger sending now if we exceed the batch threshold size (2mb)
                    if (Serializer.Measure(CreateReport(_traces)).Length > BATCH_THRESHOLD_SIZE)
                        ForceSendTrigger.Set();
                }
            }
        }

        public AsyncAutoResetEvent ForceSendTrigger { get; } = new AsyncAutoResetEvent();

        public async Task Send()
        {
            // Swap values atomically so we don't get an add after we retrieve and before we clear

            IDictionary<string, TracesAndStats> traces;
            lock (_tracesLock)
                traces = Interlocked.Exchange(ref _traces, new ConcurrentDictionary<string, TracesAndStats>());
            if (traces.Count > 0)
            {
                var report = CreateReport(traces);

                byte[] bytes;
                await using (var memoryStream = new MemoryStream())
                {
                    await using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Fastest))
                        Serializer.Serialize(gzipStream, report);
                    bytes = memoryStream.ToArray();
                }

                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, new Uri("https://engine-report.apollodata.com/api/ingress/traces"));
                httpRequestMessage.Headers.Add("X-Api-Key", _apiKey);

                httpRequestMessage.Content = new ByteArrayContent(bytes)
                {
                    Headers =
                    {
                        ContentType = new MediaTypeHeaderValue("application/protobuf"),
                        ContentEncoding = {"gzip"}
                    }
                };

                var client = _httpClientFactory.CreateClient();
                var response = await client.SendAsync(httpRequestMessage);
                if (!response.IsSuccessStatusCode)
                    _logger.LogWarning("Failed to send traces to Apollo Studio with error code {errorCode}", response.StatusCode);
            }
        }

        private static string MinimalWhitespace(string? requestQuery)
        {
            return Regex.Replace((requestQuery ?? "").Trim().Replace("\r", "\n").Replace("\n", " "), @"\s{2,}", " ");
        }

        private static string ComputeSha256Hash(string rawData)
        {
            // Create a SHA256   
            using SHA256 sha256Hash = SHA256.Create();
            // ComputeHash - returns byte array  
            byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));

            // Convert byte array to a string   
            StringBuilder builder = new StringBuilder();
            foreach (var t in bytes)
                builder.Append(t.ToString("x2"));
            return builder.ToString();
        }

        private Report CreateReport(IDictionary<string, TracesAndStats> traces)
        {
            var report = new Report
            {
                Header = _reportHeader
            };

            foreach (var (key, value) in traces)
                report.TracesPerQueries.Add(key, value);

            return report;
        }
    }
}