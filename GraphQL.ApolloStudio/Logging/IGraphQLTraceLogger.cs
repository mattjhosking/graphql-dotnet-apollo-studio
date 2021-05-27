using System.Threading.Tasks;
using GraphQL.ApolloStudio;
using GraphQL;
using Microsoft.AspNetCore.Http;

namespace GraphQL.ApolloStudio.Logging
{
    public interface IGraphQLTraceLogger
    {
        void LogTrace(HttpContext httpContext, GraphQLRequest query, ExecutionResult result);
        AsyncAutoResetEvent ForceSendTrigger { get; }
        Task Send();
    }
}