using Elastic.Clients.Elasticsearch;

namespace AMAIS.WKFLO.Common.Infrastructure.Providers.Clients
{
    public class EisElasticsearchClient : ElasticsearchClient
    {
        public EisElasticsearchClient(IElasticsearchClientSettings elasticsearchClientSettings) : base(elasticsearchClientSettings)
        {
        }
    }
}
