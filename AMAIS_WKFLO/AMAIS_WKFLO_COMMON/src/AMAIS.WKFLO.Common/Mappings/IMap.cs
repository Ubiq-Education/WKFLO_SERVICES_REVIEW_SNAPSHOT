//using AutoMapper;
using Mapster;

namespace AMAIS.WKFLO.Common.Mappings
{
    public interface IMap
    {
        //public void Mapping(Profile profile);
        public void Mapping(TypeAdapterConfig config);
    }
}
