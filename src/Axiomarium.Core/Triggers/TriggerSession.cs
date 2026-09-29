using Axiomarium.Core.Instructions;

namespace Axiomarium.Core.Triggers;

/// <summary>One session a trigger test runs: one prompt of one skill, on one harness, for the nth time.</summary>
/// <param name="Harness">The harness that runs it.</param>
/// <param name="Skill">The vault skill whose prompt it is.</param>
/// <param name="Prompt">The prompt's index in the skill's prompt file, from 0.</param>
/// <param name="Run">Which run of that prompt, from 1.</param>
/// <param name="Text">The prompt, sent as the session's first message.</param>
public sealed record TriggerSession(Harness Harness, string Skill, int Prompt, int Run, string Text);

/// <summary>What one session loaded before its first other action.</summary>
/// <param name="Session">The session.</param>
/// <param name="Loads">The skills it loaded, in order: names from Claude Code's <c>Skill</c> calls, or the folders of the <c>SKILL.md</c> files Codex read.</param>
/// <param name="Model">The model the harness reported, or <see langword="null"/> when it doesn't say.</param>
/// <param name="Version">The harness version it reported, or <see langword="null"/>.</param>
/// <param name="Problem">Why the session gave no answer, such as a harness error or a timeout, or <see langword="null"/>. A session with a problem isn't scored.</param>
public sealed record SessionResult(TriggerSession Session, IReadOnlyList<string> Loads, string? Model, string? Version, string? Problem);
