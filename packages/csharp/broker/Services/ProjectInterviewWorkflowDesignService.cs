namespace Broker.Services;

public sealed record WorkflowDesignViewModel(
    string TaskId,
    int Version,
    string TemplateFamily,
    string ProjectScale,
    IReadOnlyList<string> EnabledModules,
    string StyleProfile);

public sealed class ProjectInterviewWorkflowDesignService
{
    public WorkflowDesignViewModel BuildViewModel(string taskId, int version, ProjectInstanceDefinition projectDefinition) =>
        new(
            taskId,
            version,
            projectDefinition.TemplateFamily,
            projectDefinition.ProjectScale,
            projectDefinition.EnabledModules,
            projectDefinition.StyleProfile);
}
