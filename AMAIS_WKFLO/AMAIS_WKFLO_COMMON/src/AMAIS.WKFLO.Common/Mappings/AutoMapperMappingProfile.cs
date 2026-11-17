//using AutoMapper;
//using System.Reflection;

//namespace AMAIS.WKFLO.Common.Mappings
//{
//    public class AutoMapperMappingProfile : Profile
//    {
//        public AutoMapperMappingProfile()
//        {
//            Array.ForEach(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).ToArray(), assembly =>
//            {
//                ApplyMappingsFromAssembly(assembly);
//            });
//        }

//        private void ApplyMappingsFromAssembly(Assembly assembly)
//        {
//            var types = new List<Type>();

//            types = assembly.GetExportedTypes()
//               .Where(t => t.GetInterfaces().Any(i =>
//                   i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMapFrom<>)))
//               .ToList();

//            foreach (var type in types)
//            {
//                var instance = Activator.CreateInstance(type);

//                var methodInfo = type.GetMethod("Mapping", new[] { typeof(Profile) })
//                    ?? type.GetInterface("IMapFrom`1").GetMethod("Mapping", new[] { typeof(Profile) });

//                methodInfo?.Invoke(instance, new object[] { this });

//            }

//            types = new List<Type>();

//            types = assembly.GetExportedTypes()
//                .Where(t => t.GetInterfaces().Any(i =>
//                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMapTo<>)))
//                .ToList();

//            foreach (var type in types)
//            {
//                var instance = Activator.CreateInstance(type);

//                var methodInfo = type.GetMethod("Mapping", new[] { typeof(Profile) })
//                    ?? type.GetInterface("IMapTo`1").GetMethod("Mapping", new[] { typeof(Profile) });

//                methodInfo?.Invoke(instance, new object[] { this });
//            }

//            types = new List<Type>();

//            types = assembly.GetExportedTypes()
//                .Where(t => t.GetInterfaces().Any(i => i == typeof(IMap)))
//                .ToList();

//            foreach (var type in types)
//            {
//                if (!type.IsAbstract)
//                {
//                    if (type.IsGenericType)
//                    {
//                        var genericTypeParameters = new Type[] { typeof(object) };
//                        var t = type.MakeGenericType(genericTypeParameters);
//                        var instance = Activator.CreateInstance(t);
//                        var methodInfo = t.GetMethod("Mapping", new[] { typeof(Profile) });
//                        methodInfo?.Invoke(instance, new object[] { this });
//                    }
//                    else
//                    {
//                        var instance = Activator.CreateInstance(type);
//                        var methodInfo = type.GetMethod("Mapping", new[] { typeof(Profile) });
//                        methodInfo?.Invoke(instance, new object[] { this });
//                    }
//                }
//            }
//        }
//    }
//}
