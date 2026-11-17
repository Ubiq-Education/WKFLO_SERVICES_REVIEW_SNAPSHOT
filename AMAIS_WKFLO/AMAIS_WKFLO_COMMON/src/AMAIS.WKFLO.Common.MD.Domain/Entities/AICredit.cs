using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    // WI 7384: per-AIModel token pricing/multiplier definition, used to convert token usage into
    // credits. Cost is intentionally NOT part of the Credits formula - it's already baked into
    // MultiplierIn/MultiplierOut (calibrated offline against the reference spreadsheet). CostInMTok/
    // CostOutMTok/TotalCost are kept for reference/traceability only.
    public class AICredit : Entity<int>
    {
        public int AIModelId { get; set; }
        public AIModel AIModel { get; set; }

        public long TokensIn { get; set; }
        public decimal CostInMTok { get; set; }
        public long TokensOut { get; set; }
        public decimal CostOutMTok { get; set; }

        // Computed column (SQL Server): CostInMTok + CostOutMTok.
        public decimal TotalCost { get; set; }

        public decimal MultiplierIn { get; set; }
        public decimal MultiplierOut { get; set; }

        // Computed column (SQL Server): (TokensIn * MultiplierIn / 1000) + (TokensOut * MultiplierOut / 1000).
        public decimal Credits { get; set; }
    }
}
