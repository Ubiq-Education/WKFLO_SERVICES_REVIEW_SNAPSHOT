namespace AMAIS.WKFLO.Common.Models
{
    public enum ProviderEnum : int
    {
        azure = 1,
        openai = 2,
        //cohere = 3,
        anthropic = 4,
        google = 5
    }

    public enum AuthModeEnum : int
    {
        apiKey = 0,
        managedIdentity = 1
    }

    public enum ItemType : int
    {
        Agent = 1,
        Tool = 2
    }

    public enum ChannelMessageType : int
    {
        None = 0,
        Normal = 1,
        Thinking = 2,
        Metadata = 3,
        // General-purpose channel for structured form/card messages. The specific kind of
        // form is discriminated by the CommonMetadataConsts.FormType metadata key (e.g.
        // "TextSuggestion") so new form types can be added without new channel types.
        Forms = 4
    }
}
