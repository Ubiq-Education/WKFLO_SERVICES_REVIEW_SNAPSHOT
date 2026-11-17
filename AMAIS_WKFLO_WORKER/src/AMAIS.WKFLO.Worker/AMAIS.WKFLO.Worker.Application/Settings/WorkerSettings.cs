namespace AMAIS.WKFLO.Worker.Application.Settings
{
    public class WorkerSettings
    {
        public int LookbackMonths { get; set; } = 12;
        public int BatchSize { get; set; } = 50;
    }
}
