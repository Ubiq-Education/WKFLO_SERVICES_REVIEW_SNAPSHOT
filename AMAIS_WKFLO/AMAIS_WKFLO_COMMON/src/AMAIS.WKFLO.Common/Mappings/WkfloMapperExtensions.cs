//using AutoMapper;
using Mapster;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace AMAIS.WKFLO.Common.Mappings
{
    public static class WkfloMapperExtensions
    {
        // ----------------------------------------------------------------
        // DI registration helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Registers AutoMapper (with MappingProfile convention scan) and IWkfloMapper in one call.
        /// </summary>
        //public static IServiceCollection AddAutoMapperWkfloMapper(this IServiceCollection services)
        //{
        //    services.AddAutoMapper(config =>
        //    {
        //        config.AddProfile<AutoMapperMappingProfile>();
        //    });
        //    services.AddScoped<IWkfloMapper, AutoMapperWkfloMapper>();
        //    return services;
        //}

        /// <summary>
        /// Registers IWkfloMapper backed by Mapster.
        /// Scans the provided assemblies (or all loaded assemblies) for IRegister implementations
        /// including MapsterMappingProfile.
        /// </summary>
        public static IServiceCollection AddMapsterWkfloMapper(
            this IServiceCollection services,
            Action<TypeAdapterConfig> configure = null,
            params Assembly[] assemblies)
        {
            var config = new TypeAdapterConfig();

            var toScan = assemblies.Length > 0
                ? assemblies
                : AppDomain.CurrentDomain.GetAssemblies();

            config.Scan(toScan);
            configure?.Invoke(config);

            services.AddSingleton(config);
            services.AddScoped<MapsterMapper.IMapper, MapsterMapper.ServiceMapper>();
            services.AddScoped<IWkfloMapper, MapsterWkfloMapper>();
            return services;
        }

        // ----------------------------------------------------------------
        // Factory helpers — for unit tests and other non-DI contexts
        // ----------------------------------------------------------------

        /// <summary>
        /// Creates a ready-to-use IWkfloMapper for use outside of DI (e.g. unit test fixtures).
        /// Pass WkfloMapperBackend.AutoMapper or WkfloMapperBackend.Mapster to choose the backend.
        /// </summary>
        public static IWkfloMapper CreateMapper(WkfloMapperBackend backend = WkfloMapperBackend.Mapster)
        {
            return backend switch
            {
                //WkfloMapperBackend.AutoMapper => CreateAutoMapper(),
                WkfloMapperBackend.Mapster => CreateMapsterMapper(),
                _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, null)
            };
        }

        //private static IWkfloMapper CreateAutoMapper()
        //{
        //    var configuration = new MapperConfiguration(cfg =>
        //    {
        //        cfg.AddProfile<AutoMapperMappingProfile>();
        //    });

        //    return new AutoMapperWkfloMapper(configuration.CreateMapper());
        //}

        private static IWkfloMapper CreateMapsterMapper()
        {
            var config = new TypeAdapterConfig();
            config.Scan(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).ToArray());

            return new MapsterWkfloMapper(new MapsterMapper.Mapper(config));
        }
    }

    public enum WkfloMapperBackend
    {
        AutoMapper,
        Mapster
    }
}
