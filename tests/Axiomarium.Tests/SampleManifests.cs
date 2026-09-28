namespace Axiomarium.Tests;

/// <summary>Manifest text for tests. Variants are made with <see cref="string.Replace(string, string)"/>.</summary>
internal static class SampleManifests
{
    /// <summary>A complete, valid agent manifest whose <c>maturity</c> is on line 5.</summary>
    public const string Valid = """
        name: determinism-auditor
        kind: agent
        version: 0.1.0
        description: Reviews a codebase for decisions an LLM shouldn't own.
        maturity: experimental
        supports:
          claude-code: experimental
          generic: full
        permissions:
          filesystem: read
          shell: none
          network: none
        side_effects: none
        inputs:
          - source-code
        outputs:
          - boundary-findings
        evals:
          trigger: false
          behavioral: false
          regression: false

        """;

    /// <summary>A valid block for each kind that has one, to append to a manifest of that kind.</summary>
    public static IReadOnlyDictionary<string, string> Blocks { get; } = new Dictionary<string, string>
    {
        ["skill"] = "skill:\n  use_when: Writing or changing an asset.\n",
        ["hook"] = "hook:\n  event: after-edit\n  command: axm hook x\n  response: warn\n",
        ["policy"] = "policy:\n  rules:\n    - id: a-rule\n      rule: A rule.\n      enforced_by: []\n",
    };
}
