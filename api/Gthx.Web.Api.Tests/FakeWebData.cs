using Gthx.Core;
using Gthx.Data;

namespace Gthx.Web.Api.Tests;

internal class FakeWebData : IGthxWebData
{
    public List<Factoid> SearchResults { get; } = [];
    public List<FactoidRefCount> TopResults { get; } = [];
    public int FactoidCount { get; set; }
    public int SearchCalls { get; private set; }
    public (string, int)? LastSearch { get; private set; }
    public int? LastTopCount { get; private set; }

    public Task<List<Factoid>> SearchFactoidsAsync(string search, int maxResults, CancellationToken cancellationToken = default)
    {
        SearchCalls++;
        LastSearch = (search, maxResults);
        return Task.FromResult(SearchResults.ToList());
    }

    public Task<List<FactoidRefCount>> GetTopFactoidsAsync(int count, CancellationToken cancellationToken = default)
    {
        LastTopCount = count;
        return Task.FromResult(TopResults.ToList());
    }

    public Task<int> GetFactoidCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(FactoidCount);
}
