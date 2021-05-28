# graphql-dotnet-apollo-studio
This project is the supporting code for a series on integrating [GraphQL.NET](https://github.com/graphql-dotnet/graphql-dotnet) to [Apollo Studio](https://www.apollographql.com/docs/studio/getting-started/):
* https://dev.to/mattjhosking/integrating-apollo-studio-with-graphql-for-net-part-1-4h5f
* Part 2 in progress

## Features
* Standard ASP.NET 5 solution (from `dotnet new webapi`)
* GraphQL.NET 4.5 integration (based on their [custom middleware example](https://github.com/graphql-dotnet/examples/tree/master/src/AspNetCoreCustom))
* Apollo Studio metrics conversion and client
* Background service to batch and send traces periodically

## References
* GraphQL.NET custom middleware example - https://github.com/graphql-dotnet/examples/tree/master/src/AspNetCoreCustom
* Apollo Studio custom integration docs - https://www.apollographql.com/docs/studio/setup-analytics/#adding-support-to-a-third-party-server-advanced
* AsyncAutoResetEvent implementation  - https://devblogs.microsoft.com/pfxteam/building-async-coordination-primitives-part-2-asyncautoresetevent/
* DictionaryStringObjectJsonConverter implementation - https://josef.codes/custom-dictionary-string-object-jsonconverter-for-system-text-json/
