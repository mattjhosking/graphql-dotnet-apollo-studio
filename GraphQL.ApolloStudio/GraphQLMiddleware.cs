using GraphQL.Instrumentation;
using GraphQL.Types;
using GraphQL.Validation;
using GraphQL.ApolloStudio.Logging;
using Microsoft.AspNetCore.Http;
using StarWars;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace GraphQL.ApolloStudio
{
    public class GraphQLMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly GraphQLSettings _settings;
        private readonly IDocumentExecuter _executer;
        private readonly IDocumentWriter _writer;

        public GraphQLMiddleware(
            RequestDelegate next,
            GraphQLSettings settings,
            IDocumentExecuter executer,
            IDocumentWriter writer)
        {
            _next = next;
            _settings = settings;
            _executer = executer;
            _writer = writer;
        }

        public async Task Invoke(HttpContext context, ISchema schema, IGraphQLTraceLogger traceLogger)
        {
            if (!IsGraphQLRequest(context))
            {
                await _next(context);
                return;
            }

            await ExecuteAsync(context, schema, traceLogger);
        }

        private bool IsGraphQLRequest(HttpContext context)
        {
            return context.Request.Path.StartsWithSegments(_settings.Path)
                && string.Equals(context.Request.Method, "POST", StringComparison.OrdinalIgnoreCase);
        }

        private async Task ExecuteAsync(HttpContext context, ISchema schema, IGraphQLTraceLogger traceLogger)
        {
            var request = await Deserialize<GraphQLRequest>(context.Request.Body);
            if (request == null)
            {
                context.Response.StatusCode = 400;
            }
            else
            {
                var start = DateTime.UtcNow;

                var result = await _executer.ExecuteAsync(_ =>
                {
                    _.Schema = schema;
                    _.Query = request.Query;
                    _.OperationName = request.OperationName;
                    _.Inputs = request.Variables.ToInputs();
                    _.UserContext = _settings.BuildUserContext?.Invoke(context);
                    _.ValidationRules = DocumentValidator.CoreRules.Concat(new[] { new InputValidationRule() });
                    _.EnableMetrics = _settings.EnableMetrics;
                });

                result.EnrichWithApolloTracing(start);

                traceLogger.LogTrace(context, request, result);
                if (!_settings.EnableMetrics)
                    result.Extensions.Remove("tracing");

                await WriteResponseAsync(context, result);
            }
        }

        private async Task WriteResponseAsync(HttpContext context, ExecutionResult result)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = result.Errors?.Any() == true ? (int)HttpStatusCode.BadRequest : (int)HttpStatusCode.OK;

            await _writer.WriteAsync(context.Response.Body, result);
        }

        public static async Task<T?> Deserialize<T>(Stream s)
        {
            using (var reader = new StreamReader(s))
                return await JsonSerializer.DeserializeAsync<T>(s, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
    }
}
