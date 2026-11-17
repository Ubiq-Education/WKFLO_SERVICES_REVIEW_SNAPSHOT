#nullable enable

using System.Text.RegularExpressions;
using EFCoreSecondLevelCacheInterceptor;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AMAIS.WKFLO.Common.MD.Domain.Caching
{
    /// <summary>
    /// Allowlist for the type names embedded in cached EF payloads. The L2 cache is shared infrastructure,
    /// so a poisoned entry must not be able to make Newtonsoft load or instantiate arbitrary types.
    /// The names are checked before anything is resolved.
    /// </summary>
    public sealed partial class EfCacheSerializationBinder : ISerializationBinder
    {
        private static readonly DefaultSerializationBinder Default = new();

        private static readonly Type[] AllowedTypes =
        [
            typeof(string), typeof(Guid), typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan), typeof(DateOnly), typeof(TimeOnly),
            typeof(decimal), typeof(DBNull), typeof(object),
            typeof(bool), typeof(byte), typeof(sbyte), typeof(char), typeof(short), typeof(ushort), typeof(int), typeof(uint),
            typeof(long), typeof(ulong), typeof(float), typeof(double),
            typeof(List<>), typeof(Dictionary<,>),
            typeof(EFCachedData), typeof(EFTableRows), typeof(EFTableRow), typeof(EFTableColumnInfo)
        ];

        // Assemblies the allowed types live in, depending on the runtime and the serializing process.
        private static readonly HashSet<string> AllowedAssemblies = new(StringComparer.Ordinal)
        {
            "System.Private.CoreLib", "mscorlib", "System.Runtime", "System.Collections", "EFCoreSecondLevelCacheInterceptor"
        };

        private static readonly HashSet<string> AllowedTypeNames = new(AllowedTypes.Select(type => type.FullName!), StringComparer.Ordinal);

        public Type BindToType(string? assemblyName, string typeName)
        {
            if (!NamesAreAllowed(assemblyName, typeName))
            {
                throw new JsonSerializationException($"Type '{typeName}' is not allowed in the EF cache payload.");
            }

            var type = Default.BindToType(assemblyName, typeName);

            if (!IsAllowed(type))
            {
                throw new JsonSerializationException($"Type '{typeName}' is not allowed in the EF cache payload.");
            }

            return type;
        }

        public void BindToName(Type serializedType, out string? assemblyName, out string? typeName) =>
            Default.BindToName(serializedType, out assemblyName, out typeName);

        // Every name in the (possibly generic or array) type name, including the assemblies of its arguments, must be on the list.
        private static bool NamesAreAllowed(string? assemblyName, string typeName) =>
            (assemblyName is null || AllowedAssemblies.Contains(assemblyName))
            && NameTokens().Matches(typeName).All(token => AllowedTypeNames.Contains(token.Value) || AllowedAssemblies.Contains(token.Value));

        private static bool IsAllowed(Type type)
        {
            if (type.IsArray)
            {
                return IsAllowed(type.GetElementType()!);
            }

            if (type.IsGenericType)
            {
                return AllowedTypes.Contains(type.GetGenericTypeDefinition()) && type.GetGenericArguments().All(IsAllowed);
            }

            return AllowedTypes.Contains(type);
        }

        [GeneratedRegex(@"[^\[\],\s]+")]
        private static partial Regex NameTokens();
    }
}
