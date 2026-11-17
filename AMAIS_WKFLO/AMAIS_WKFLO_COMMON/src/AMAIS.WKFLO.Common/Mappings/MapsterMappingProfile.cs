using Mapster;
using System.Reflection;

namespace AMAIS.WKFLO.Common.Mappings
{
    /// <summary>
    /// Mapster equivalent of MappingProfile. Implements IRegister so TypeAdapterConfig.Scan()
    /// picks it up automatically.
    ///
    /// IMapFrom{T} and IMapTo{T} are registered explicitly; Mapster's property-name convention
    /// handles the member mapping inside each pair.
    ///
    /// IMap types have their Mapping(TypeAdapterConfig) overload invoked. Types that don't
    /// override it get the default no-op and fall back to Mapster convention mapping.
    /// </summary>
    public class MapsterMappingProfile : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            ApplyMappingsFromAssemblies(config, AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .ToArray());
        }

        public static void ApplyMappingsFromAssemblies(TypeAdapterConfig config, Assembly[] assemblies)
        {
            // Prevent stack overflows on circular EF navigation properties (e.g. Agent → AIModel → Agents → ...).
            // When Mapster encounters an object instance it has already mapped in the current call,
            // it reuses the already-mapped result instead of recursing into it again.
            config.Default.PreserveReference(true);

            foreach (var assembly in assemblies)
            {
                // IMapFrom<T>: T → implementing type
                foreach (var destType in assembly.GetExportedTypes()
                    .Where(t => t.GetInterfaces().Any(i =>
                        i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMapFrom<>))))
                {
                    var sourceType = destType.GetInterfaces()
                        .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMapFrom<>))
                        .GetGenericArguments()[0];

                    config.NewConfig(sourceType, destType);
                }

                // IMapTo<T>: implementing type → T
                foreach (var sourceType in assembly.GetExportedTypes()
                    .Where(t => t.GetInterfaces().Any(i =>
                        i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMapTo<>))))
                {
                    var destType = sourceType.GetInterfaces()
                        .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMapTo<>))
                        .GetGenericArguments()[0];

                    config.NewConfig(sourceType, destType);
                }

                // IMap: call the Mapster overload; types that don't override it get the no-op default
                foreach (var type in assembly.GetExportedTypes()
                    .Where(t => !t.IsAbstract && typeof(IMap).IsAssignableFrom(t)))
                {
                    var instance = (IMap)Activator.CreateInstance(type);
                    instance.Mapping(config);
                }
            }
        }
    }
}
