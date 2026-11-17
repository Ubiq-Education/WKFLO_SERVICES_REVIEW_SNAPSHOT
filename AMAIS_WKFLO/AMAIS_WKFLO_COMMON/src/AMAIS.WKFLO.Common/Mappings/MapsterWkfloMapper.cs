using Mapster;

namespace AMAIS.WKFLO.Common.Mappings
{
    public sealed class MapsterWkfloMapper : IWkfloMapper
    {
        private readonly MapsterMapper.IMapper _mapper;

        public MapsterWkfloMapper(MapsterMapper.IMapper mapper) => _mapper = mapper;

        public TDestination Map<TDestination>(object source) =>
            _mapper.Map<TDestination>(source);

        public TDestination Map<TSource, TDestination>(TSource source) =>
            _mapper.Map<TSource, TDestination>(source);

        public TDestination Map<TSource, TDestination>(TSource source, TDestination destination) =>
            _mapper.Map(source, destination);

        public IQueryable<TDestination> ProjectTo<TDestination>(IQueryable source) =>
            source.ProjectToType<TDestination>();
    }
}
