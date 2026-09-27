namespace Axiomarium.Core.Assets;

/// <summary>What an asset is. Each kind lives in its own top-level vault folder.</summary>
public enum AssetKind
{
    /// <summary>A specialized agent, in <c>agents/</c>.</summary>
    Agent,

    /// <summary>A skill the model loads when it is relevant, in <c>skills/</c>.</summary>
    Skill,

    /// <summary>A hook the harness runs on an event, in <c>hooks/</c>.</summary>
    Hook,

    /// <summary>A policy that says which decisions belong to code, in <c>policies/</c>.</summary>
    Policy,

    /// <summary>A multi-step workflow, in <c>workflows/</c>.</summary>
    Workflow,

    /// <summary>An experiment and its write-up, in <c>experiments/</c>.</summary>
    Experiment,
}
