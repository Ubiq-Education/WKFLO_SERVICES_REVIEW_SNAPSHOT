using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class AIModel : Entity<int>
    {
        public string Name { get; set; }
        public string Model { get; set; }
        public string DeploymentName { get; set; }
        public string Provider { get; set; }
        public string Endpoint { get; set; }
        public string ApiVersion { get; set; }
        public string Type { get; set; }
        public string AuthMode { get; set; }
        public bool IsActive { get; set; }

        public int? SecretId { get; set; }
        public Secret Secret { get; set; }

        public virtual ICollection<Agent> Agents { get; set; }

        // WI 7384: token pricing/multiplier definition for this model.
        public AICredit AICredit { get; set; }
    }
}
