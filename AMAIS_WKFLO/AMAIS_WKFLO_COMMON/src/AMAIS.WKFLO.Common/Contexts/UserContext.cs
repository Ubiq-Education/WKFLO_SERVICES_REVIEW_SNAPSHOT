using AMAIS.WKFLO.Common.Mappings;
using Mapster;

namespace AMAIS.WKFLO.Common.Contexts
{
    public class UserContext : IMap
    {
        /// <summary>
        /// Unique user ID extracted from the JWT token
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// Unique user ID extracted from WKFLO DB
        /// </summary>
        public string Guid { get; set; }

        /// <summary>
        /// User name extracted from the JWT token
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// User given name extracted from the JWT token
        /// </summary>
        public string GivenName { get; set; }

        /// <summary>
        /// User surname extracted from the JWT token
        /// </summary>
        public string Surname { get; set; }

        /// <summary>
        /// User email extracted from the JWT token
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// Audience extracted from the JWT token
        /// </summary>
        public string Audience { get; set; }

        /// <summary>
        /// User roles extracted from the JWT token
        /// </summary>
        public IEnumerable<string> Roles { get; set; } = new List<string>();

        /// <summary>
        /// All original claims from the JWT token
        /// </summary>
        public IEnumerable<ClaimContext> Claims { get; set; } = new List<ClaimContext>();

        /// <summary>
        /// Gets or sets the raw string value of the authentication token.
        /// </summary>
        public string RawToken { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<UserContext, UserContext>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<UserContext, UserContext>();
        }
    }

    public class ClaimContext
    {
        public ClaimContext(string type, string value)
        {
            this.Type = type;
            this.Value = value;
        }

        public string Type { get; set; }
        public string Value { get; set; }
    }
}
