using System.Text;

namespace Broker.Services;

public sealed record PdfRenderResult(string FileName, byte[] Bytes, string MetadataDigest);

public sealed class ProjectInterviewPdfRenderService
{
    public PdfRenderResult Render(WorkflowDesignViewModel viewModel, string jsonDigest)
    {
        var content = Encoding.UTF8.GetBytes($"workflow-design:{viewModel.TaskId}:v{viewModel.Version}:{jsonDigest}");
        return new PdfRenderResult($"workflow-design.v{viewModel.Version}.pdf", content, jsonDigest);
    }
}
