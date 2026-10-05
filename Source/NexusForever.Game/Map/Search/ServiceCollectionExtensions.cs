using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Map.Search;
using NexusForever.Shared;

namespace NexusForever.Game.Map.Search
{
    public static class ServiceCollectionExtensions
    {
        public static void AddGameMapSearch(this IServiceCollection sc)
        {
            sc.AddTransient<ISearchCheckFactory, SearchCheckFactory>();
            sc.AddTransientFactory<ISearchCheckTelegraph, SearchCheckTelegraph>();
            sc.AddTransient<ISearchCheckSpellTargetImplicit, SearchCheckSpellTargetImplicit>();
        }
    }
}
