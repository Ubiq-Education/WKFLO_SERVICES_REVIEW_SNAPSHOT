using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.Models.Config
{
    public class ContactFormToolConfig
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("mandatory")]
        public bool Mandatory { get; set; }

        [JsonProperty("firstNameRequired")]
        public bool FirstNameRequired { get; set; }

        [JsonProperty("lastNameRequired")]
        public bool LastNameRequired { get; set; }

        [JsonProperty("emailRequired")]
        public bool EmailRequired { get; set; }

        [JsonProperty("whenToUse")]
        public string WhenToUse { get; set; }

        [JsonProperty("submitButtonText")]
        public string SubmitButtonText { get; set; } = "Submit";

        [JsonProperty("successMessageEnabled")]
        public bool SuccessMessageEnabled { get; set; }

        [JsonProperty("successMessageText")]
        public string SuccessMessageText { get; set; }
    }
}
