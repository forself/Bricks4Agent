using System.Text.Json;
using BrokerCore.Services;

namespace Unit.Tests.Broker;

/// <summary>
/// generate 超出 scope 送管理員審批時，審批畫面顯示的內容（BrokerService.BuildRendered 的 generate case）：
/// 標題、每頁的 id／頁型／欄位數與定義本身（截斷到上限）。
/// </summary>
public sealed class GenerationApprovalRenderTests
{
    private static JsonElement Args(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [Fact]
    public void GenerateRequest_RendersTheTitlePagesAndDefinition()
    {
        var args = Args("""
            {
              "title": "聯絡人原型",
              "page_ids": ["contacts-list"],
              "template": {
                "kind": "definition-template",
                "definitions": {
                  "pages": [
                    { "id": "contacts-list", "definition": { "type": "list", "fields": [ { "name": "fullName" }, { "name": "email" } ] } },
                    { "id": "contact-form", "definition": { "type": "form", "fields": [ { "name": "fullName" } ] } }
                  ]
                }
              }
            }
            """);

        var rendered = BrokerService.RenderGenerationRequest(args, "聯絡人原型");

        rendered.Kind.Should().Be("definition");
        rendered.Payload.Should().Contain("title: 聯絡人原型")
            .And.Contain("pages: 2")
            .And.Contain("- contacts-list (list, 2 fields)")
            .And.Contain("- contact-form (form, 1 fields)")
            .And.Contain("page_ids: [\"contacts-list\"]")
            .And.Contain("\"kind\": \"definition-template\"");
    }

    [Fact]
    public void LargeDefinitions_AreTruncated()
    {
        var fields = string.Join(",", Enumerable.Range(0, 400).Select(i => $"{{\"name\":\"field{i}\",\"type\":\"text\",\"label\":\"欄位 {i}\"}}"));
        var args = Args("{\"template\":{\"definitions\":{\"pages\":[{\"id\":\"big\",\"definition\":{\"type\":\"form\",\"fields\":[" + fields + "]}}]}}}");

        var rendered = BrokerService.RenderGenerationRequest(args, null);

        rendered.Payload.Should().Contain("- big (form, 400 fields)").And.EndWith("…");
        rendered.Payload!.Length.Should().BeLessThan(BrokerService.RenderedDefinitionPreviewLimit + 400);
    }

    [Fact]
    public void MissingTemplate_StillRenders()
    {
        var rendered = BrokerService.RenderGenerationRequest(Args("{}"), null);

        rendered.Kind.Should().Be("definition");
        rendered.Payload.Should().Contain("title: (none)").And.Contain("(no template)");
    }
}
