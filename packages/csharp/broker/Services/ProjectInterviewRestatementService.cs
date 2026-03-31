namespace Broker.Services;

public sealed class ProjectInterviewRestatementService
{
    public IReadOnlyList<RestatementOption> BuildOptions(IEnumerable<string> candidateStatements, string conservativeOptionText)
    {
        var normalizedStatements = candidateStatements
            .Where(static statement => !string.IsNullOrWhiteSpace(statement))
            .Take(3)
            .ToArray();

        var options = new List<RestatementOption>(normalizedStatements.Length + 1);
        for (var index = 0; index < normalizedStatements.Length; index++)
        {
            var statement = normalizedStatements[index].Trim();
            options.Add(new RestatementOption(
                $"opt-{index + 1}",
                statement,
                new[] { statement },
                false));
        }

        options.Add(new RestatementOption(
            "opt-conservative",
            conservativeOptionText.Trim(),
            Array.Empty<string>(),
            true));

        return options;
    }
}
