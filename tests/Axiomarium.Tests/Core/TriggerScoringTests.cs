using Axiomarium.Core.Instructions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Tests.Core;

public class TriggerScoringTests
{
    private static readonly TriggerPromptFile Deploy = new(
        "deploy",
        null,
        [
            new TriggerPrompt("Ship the shop.", PromptKind.Positive, true),
            new TriggerPrompt("Get v2 out the door.", PromptKind.Paraphrased, true),
            new TriggerPrompt("Format this table.", PromptKind.Negative, false),
            new TriggerPrompt("Deploy the docs site preview.", PromptKind.Adversarial, false),
            new TriggerPrompt("Which one ships it?", PromptKind.Ambiguous, false, "ship"),
        ]);

    private static readonly Dictionary<string, TriggerPromptFile> Prompts = new() { ["deploy"] = Deploy };

    private static SessionResult Result(int prompt, int run, params string[] loads) =>
        new(new TriggerSession(Harness.ClaudeCode, "deploy", prompt, run, Deploy.Prompts[prompt].Prompt), loads, "claude-opus-5-5", "2.1.284", null);

    // using-superpowers loads before every task, so the pick is the first load after it.
    private static readonly SessionResult[] Runs =
    [
        Result(0, 1, "using-superpowers", "deploy"), Result(0, 2, "using-superpowers", "deploy"), Result(0, 3, "using-superpowers", "deploy"),
        Result(1, 1, "using-superpowers", "deploy"), Result(1, 2, "using-superpowers", "ship"), Result(1, 3, "using-superpowers"),
        Result(2, 1, "using-superpowers"), Result(2, 2, "using-superpowers"), Result(2, 3, "using-superpowers"),
        Result(3, 1, "using-superpowers", "deploy"), Result(3, 2, "using-superpowers"), Result(3, 3, "using-superpowers"),
        Result(4, 1, "using-superpowers", "deploy"), Result(4, 2, "using-superpowers", "ship"), Result(4, 3, "using-superpowers", "ship"),
    ];

    [Fact]
    public void A_skill_loaded_in_every_run_that_is_not_under_test_is_background_and_never_the_pick()
    {
        var results = TriggerScoring.Score(Runs, Prompts);

        Assert.Equal([new BackgroundSkill(Harness.ClaudeCode, "using-superpowers", 15, 15)], results.Background);
    }

    // Positive and paraphrased: 4 of 6 runs picked deploy. Negative, adversarial and the ambiguous prompt that
    // should go to its rival: deploy fired in 2 of 9.
    [Fact]
    public void Precision_and_recall_count_runs_over_the_skill_s_own_prompts()
    {
        var score = Assert.Single(TriggerScoring.Score(Runs, Prompts).Scores);

        Assert.Equal((Harness.ClaudeCode, "deploy", 4, 2, 2), (score.Harness, score.Skill, score.TruePositives, score.FalsePositives, score.FalseNegatives));
        Assert.Equal((4 / 6.0, 4 / 6.0), (score.Precision, score.Recall));
    }

    [Fact]
    public void Each_wrong_outcome_of_a_prompt_is_one_problem_with_its_count()
    {
        var problems = TriggerScoring.Score(Runs, Prompts).Problems;

        Assert.Equal(
            [
                (ProblemKind.Collision, 1, "deploy", "ship", 1, 3),
                (ProblemKind.Collision, 4, "ship", "deploy", 1, 3),
                (ProblemKind.FalseTrigger, 3, null, "deploy", 1, 3),
                (ProblemKind.Miss, 1, "deploy", null, 1, 3),
            ],
            problems.Select(problem => (problem.Kind, problem.Prompt, problem.Expected, problem.Picked, problem.Count, problem.Runs)));
    }

    [Fact]
    public void A_session_with_a_problem_is_left_out_and_reported()
    {
        SessionResult[] runs =
        [
            Result(0, 1, "deploy"),
            new(new TriggerSession(Harness.Codex, "deploy", 0, 1, "Ship the shop."), [], null, null, "You've hit your usage limit."),
        ];

        var results = TriggerScoring.Score(runs, Prompts);

        Assert.Equal(["You've hit your usage limit."], results.Failed.Select(result => result.Problem));
        Assert.Equal([Harness.ClaudeCode], results.Scores.Select(score => score.Harness));
    }

    // One prompt run three times can't tell a background skill from a correct pick.
    [Fact]
    public void Background_needs_runs_of_at_least_two_prompts_and_a_skill_under_test_is_never_background()
    {
        SessionResult[] onePrompt = [Result(4, 1, "ship"), Result(4, 2, "ship")];
        SessionResult[] allDeploy = [Result(0, 1, "deploy"), Result(2, 1, "deploy")];

        Assert.Empty(TriggerScoring.Score(onePrompt, Prompts).Background);
        Assert.Empty(TriggerScoring.Score(allDeploy, Prompts).Background);
        Assert.Equal(ProblemKind.FalseTrigger, Assert.Single(TriggerScoring.Score(allDeploy, Prompts).Problems).Kind);
    }

    [Fact]
    public void A_skill_with_nothing_to_divide_by_has_no_precision()
    {
        var score = Assert.Single(TriggerScoring.Score([Result(2, 1)], Prompts).Scores);

        Assert.Equal(((double?)null, (double?)null), (score.Precision, score.Recall));
    }
}
