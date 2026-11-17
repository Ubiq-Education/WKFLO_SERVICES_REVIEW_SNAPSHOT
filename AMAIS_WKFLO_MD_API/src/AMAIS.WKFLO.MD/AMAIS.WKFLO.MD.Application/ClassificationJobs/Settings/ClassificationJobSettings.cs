namespace AMAIS.WKFLO.MD.Application.ClassificationJobs.Settings
{
    public class ClassificationJobSettings
    {
        public double DelayHours { get; set; } = 8;
        public double CooldownHours { get; set; } = 24;
        public int LookbackMonths { get; set; } = 12;
    }
}
