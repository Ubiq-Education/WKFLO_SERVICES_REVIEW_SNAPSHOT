using AMAIS.WKFLO.Common.Interfaces.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Data;

namespace AMAIS.WKFLO.Common.Infrastructure.Providers.Models
{
    public class VectorData : IVectorData
    {
        [VectorStoreKey(StorageName = "id")]
        [TextSearchResultName]
        public string Id { get; init; }

        [VectorStoreData(StorageName = "title")]
        public string Title { get; init; }

        [VectorStoreData(StorageName = "content")]
        [TextSearchResultValue]
        public string Content { get; init; }

        [VectorStoreData(StorageName = "published")]
        public DateTime Published { get; init; }

        [VectorStoreData(StorageName = "authors")]
        public List<string> Authors { get; init; }

        [VectorStoreData(StorageName = "categories")]
        public List<string> Categories { get; init; }

        [VectorStoreData(StorageName = "tenant")]
        public string Tenant { get; init; }

        [VectorStoreVector(dimensions: 1536, StorageName = "embedding")]
        public Embedding<float> Embedding { get; set; }
    }
}
