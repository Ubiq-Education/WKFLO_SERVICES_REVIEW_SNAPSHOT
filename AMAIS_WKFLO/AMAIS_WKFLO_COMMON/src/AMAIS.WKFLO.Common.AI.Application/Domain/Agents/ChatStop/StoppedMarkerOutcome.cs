namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop
{
    /// <summary>US 7502: what happened to the "Stopped" marker of a stopped turn, as logged.</summary>
    public enum StoppedMarkerOutcome
    {
        /// <summary>The marker was saved after the agent's history.</summary>
        Saved,

        /// <summary>Not saved: the agent has no saved history in the session yet (a first turn leaves nothing behind).</summary>
        NoHistoryYet,

        /// <summary>Not saved: the history already holds a marker of the same Stop.</summary>
        AlreadyPresent,

        /// <summary>Not saved: the turn had already saved its history before the Stop was caught.</summary>
        TurnAlreadySaved,

        /// <summary>Not saved: no agent or session to save it for.</summary>
        NoAgent,

        /// <summary>Not saved: the save failed (logged as an error).</summary>
        Failed
    }
}
