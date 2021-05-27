using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using GraphQL;
using GraphQL.Instrumentation;
using Microsoft.Extensions.Configuration;

namespace GraphQL.ApolloStudio.Logging
{
    public class MetricsToTraceConverter
    {
        public Trace? CreateTrace(ExecutionResult result)
        {
            ApolloTrace? trace = result.Extensions != null && result.Extensions.ContainsKey("tracing") ? (ApolloTrace)result.Extensions["tracing"] : null;

            var resolvers = trace?.Execution.Resolvers?
                .OrderBy(x => string.Join(":", x.Path), new ConfigurationKeyComparer())
                .ToArray();

            var rootTrace = resolvers?.FirstOrDefault(x => x.Path.Count == 1);
            if (rootTrace == null && result.Errors == null)
                return null;

            int resolverIndex = 1;
            var rootErrors = result.Errors?.Where(x => x.Path != null && x.Path.Count() == 1).ToArray();

            var rootNode = rootTrace != null && resolvers != null
                ? CreateNodes(rootTrace.Path, CreateNodeForResolver(rootTrace, rootErrors), resolvers, ref resolverIndex, GetSubErrors(rootTrace.Path, result.Errors?.ToArray()))
                : new Trace.Node();

            if (rootTrace == null && result.Errors != null)
            {
                foreach (var executionError in result.Errors)
                    rootNode.Errors.Add(CreateTraceError(executionError));
            }

            return new Trace
            {
                StartTime = trace?.StartTime ?? DateTime.Now,
                EndTime = trace?.EndTime ?? DateTime.Now,
                DurationNs = (ulong)(trace?.Duration ?? 0),
                http = new Trace.Http { method = Trace.Http.Method.Post, StatusCode = result.Errors?.Any() == true ? (uint)HttpStatusCode.BadRequest : (uint)HttpStatusCode.OK },
                Root = rootNode
            };
        }

        private static Trace.Node CreateNodeForResolver(ApolloTrace.ResolverTrace resolver, ExecutionError[]? executionErrors)
        {
            var node = new Trace.Node
            {
                ResponseName = resolver.FieldName,
                Type = resolver.ReturnType,
                StartTime = (ulong)resolver.StartOffset,
                EndTime = (ulong)(resolver.StartOffset + resolver.Duration),
                ParentType = resolver.ParentType
            };

            if (executionErrors != null)
            {
                foreach (var executionError in executionErrors)
                    node.Errors.Add(CreateTraceError(executionError));
            }

            return node;
        }

        private static Trace.Error CreateTraceError(ExecutionError executionError)
        {
            var error = new Trace.Error
            {
                Json = JsonSerializer.Serialize(executionError),
                Message = executionError.Message
            };
            if (executionError.Locations != null)
                error.Locations.AddRange(executionError.Locations.Select(x => new Trace.Location { Column = (uint)x.Column, Line = (uint)x.Line }));
            return error;
        }

        private static ExecutionError[]? GetSubErrors(List<object> path, ExecutionError[]? errors)
        {
            return errors
                ?.Where(x => x.Path != null && x.Path.Count() > path.Count && x.Path.Take(path.Count).SequenceEqual(path))
                .ToArray();
        }

        private static Trace.Node CreateNodes(List<object> path, Trace.Node node, ApolloTrace.ResolverTrace[] resolvers,
            ref int resolverIndex, ExecutionError[]? executionErrors)
        {
            bool isArray = node.Type.StartsWith("[") && node.Type.TrimEnd('!').EndsWith("]");
            if (isArray)
            {
                if (resolverIndex < resolvers.Length)
                {
                    var resolver = resolvers[resolverIndex];
                    while (resolver.Path != null && resolver.Path.Count == path.Count + 2 && resolver.Path.Take(path.Count).SequenceEqual(path))
                    {
                        var index = (int)(resolver.Path[^2]);
                        var subPath = path.Concat(new object[] {index}).ToList();

                        var previousIndex = resolverIndex;
                        node.Childs.Add(CreateNodes(subPath,
                            new Trace.Node
                            {
                                Index = Convert.ToUInt32(index),
                                ParentType = node.Type,
                                Type = node.Type.TrimStart('[').TrimEnd('!').TrimEnd(']')
                            }, resolvers, ref resolverIndex, GetSubErrors(subPath, executionErrors)));

                        // Avoid infinite loop if the worst happens and we don't match any items for this index (HOW?!?!?)
                        if (resolverIndex == previousIndex)
                            resolverIndex++;

                        if (resolverIndex >= resolvers.Length)
                            break;

                        resolver = resolvers[resolverIndex];
                    }
                }
            }
            else
            {
                if (resolverIndex < resolvers.Length)
                {
                    var resolver = resolvers[resolverIndex];
                    while (resolver.Path != null && resolver.Path.Count == path.Count + 1 && resolver.Path.Take(path.Count).SequenceEqual(path))
                    {
                        var errors = executionErrors?.Where(x => x.Path.SequenceEqual(resolver.Path)).ToArray();
                        resolverIndex++;

                        node.Childs.Add(CreateNodes(resolver.Path, CreateNodeForResolver(resolver, errors), resolvers,
                            ref resolverIndex, GetSubErrors(resolver.Path, executionErrors)));

                        if (resolverIndex >= resolvers.Length)
                            break;

                        resolver = resolvers[resolverIndex];
                    }
                }
            }

            return node;
        }
    }
}