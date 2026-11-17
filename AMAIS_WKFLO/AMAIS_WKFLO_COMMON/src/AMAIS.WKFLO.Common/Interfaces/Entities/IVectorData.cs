using Microsoft.Extensions.AI;

namespace AMAIS.WKFLO.Common.Interfaces.Entities
{
    public interface IVectorData
    {
        public string Id { get; init; }

        public string Title { get; init; }

        public string Content { get; init; }

        public DateTime Published { get; init; }

        public List<string> Authors { get; init; }

        public List<string> Categories { get; init; }

        public string Tenant { get; init; }

        public Embedding<float> Embedding { get; set; }
    }
}
