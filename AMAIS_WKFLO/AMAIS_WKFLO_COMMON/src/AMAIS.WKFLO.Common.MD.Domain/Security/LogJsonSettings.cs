using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace AMAIS.WKFLO.Common.MD.Domain.Security
{
    /// <summary>
    /// Marks a string property whose value must never be written to the logs (e.g. a base64 file attachment):
    /// <see cref="LogJsonSettings"/> writes "[omitted: N chars]" in its place. Only affects log serialization,
    /// unlike <see cref="JsonIgnoreAttribute"/>, which would also drop the value from Newtonsoft model binding.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class OmitFromLogsAttribute : Attribute
    {
    }

    /// <summary>
    /// Newtonsoft settings for serializing requests into log messages. Same output as the default settings,
    /// except for <see cref="OmitFromLogsAttribute"/> properties.
    /// </summary>
    /// <remarks>
    /// Bug 8189: an 11 MB PDF attachment logged in full cost about 224 MiB per command (the JSON text, the log
    /// formatting and the exporters), enough to run the 0.5 GiB AI API container out of memory.
    /// </remarks>
    public static class LogJsonSettings
    {
        public static JsonSerializerSettings Default { get; } = new() { ContractResolver = new OmitFromLogsContractResolver() };

        private sealed class OmitFromLogsContractResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);
                if (member.GetCustomAttribute<OmitFromLogsAttribute>() != null && property.ValueProvider != null)
                    property.ValueProvider = new OmittedValueProvider(property.ValueProvider);

                return property;
            }
        }

        private sealed class OmittedValueProvider(IValueProvider inner) : IValueProvider
        {
            public object GetValue(object target) =>
                inner.GetValue(target) is string value ? $"[omitted: {value.Length} chars]" : null;

            public void SetValue(object target, object value) => inner.SetValue(target, value);
        }
    }
}
