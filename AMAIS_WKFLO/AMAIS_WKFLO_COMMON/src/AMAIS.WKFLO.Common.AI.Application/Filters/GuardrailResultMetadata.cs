namespace AMAIS.WKFLO.Common.AI.Application.Filters
{
    /// <summary>
    /// Well-known <see cref="Microsoft.SemanticKernel.FunctionResult.Metadata"/> key a filter sets to mark
    /// its result as a synthetic guardrail/instructional message rather than real tool output - e.g.
    /// <see cref="SemanticKernelToolCallLimitFilter"/>'s "[GUARDRAIL] ..." message when it short-circuits a call without
    /// ever invoking the underlying tool. Any other filter that also transforms <c>context.Result</c> after
    /// <c>next(context)</c> (e.g. <see cref="SemanticKernelLargeToolOutputFilter"/>, which truncates by
    /// length) must check for this key and leave a marked result alone - it's a filter's own instruction to
    /// the model, not tool output, and applying tool-output rules to it (like a character cap sized for a
    /// tool's actual payloads) can cut it down into a fragment that no longer makes sense.
    /// </summary>
    internal static class GuardrailResultMetadata
    {
        public const string IsGuardrailKey = "IsGuardrailResult";
    }
}
