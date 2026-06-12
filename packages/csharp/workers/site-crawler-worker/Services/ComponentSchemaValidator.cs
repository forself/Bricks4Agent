using System.Text.Json;
using System.Collections;
using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed class ComponentSchemaValidator
{
    public ComponentValidationResult Validate(GeneratorSiteDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var result = new ComponentValidationResult();
        var definitions = document.ComponentLibrary.Components
            .GroupBy(component => component.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        if (document.Routes.Count == 0)
        {
            result.Errors.Add("site document must contain at least one route.");
        }

        for (var routeIndex = 0; routeIndex < document.Routes.Count; routeIndex++)
        {
            var route = document.Routes[routeIndex];
            if (string.IsNullOrWhiteSpace(route.Path))
            {
                result.Errors.Add($"routes[{routeIndex}] path is required.");
            }

            ValidateNode(route.Root, definitions, $"routes[{routeIndex}].root", result);
        }

        return result;
    }

    private static void ValidateNode(
        ComponentNode node,
        IReadOnlyDictionary<string, ComponentDefinition> definitions,
        string path,
        ComponentValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(node.Type))
        {
            result.Errors.Add($"{path} component type is required.");
            return;
        }

        if (!definitions.TryGetValue(node.Type, out var definition))
        {
            result.Errors.Add($"{path} unknown component type '{node.Type}'.");
            return;
        }

        foreach (var requiredProp in definition.PropsSchema.Required)
        {
            if (!node.Props.ContainsKey(requiredProp) || node.Props[requiredProp] is null)
            {
                result.Errors.Add($"{path} ({node.Type}) missing required prop '{requiredProp}'.");
            }
        }

        foreach (var prop in node.Props)
        {
            if (!definition.PropsSchema.Properties.TryGetValue(prop.Key, out var propSchema))
            {
                result.Errors.Add($"{path} ({node.Type}) prop '{prop.Key}' is not declared in props_schema.");
                continue;
            }

            ValidateValue(prop.Value, propSchema, $"{path}.props.{prop.Key}", result);
        }

        for (var childIndex = 0; childIndex < node.Children.Count; childIndex++)
        {
            ValidateNode(node.Children[childIndex], definitions, $"{path}.children[{childIndex}]", result);
        }
    }

    private static void ValidateValue(
        object? value,
        ComponentPropSchema schema,
        string path,
        ComponentValidationResult result)
    {
        switch (schema.Type)
        {
            case "string":
                if (value is not string)
                {
                    result.Errors.Add($"{path} {DescribePathLeaf(path)} must be string.");
                }
                break;
            case "boolean":
                if (value is not bool)
                {
                    result.Errors.Add($"{path} {DescribePathLeaf(path)} must be boolean.");
                }
                break;
            case "array":
                if (value is not IEnumerable<object?> enumerable || value is string)
                {
                    result.Errors.Add($"{path} {DescribePathLeaf(path)} must be array.");
                    break;
                }

                if (schema.Items is not null)
                {
                    var index = 0;
                    foreach (var item in enumerable)
                    {
                        ValidateValue(item, schema.Items, $"{path}[{index}]", result);
                        index++;
                    }
                }
                break;
            case "object":
                var dictionary = NormalizeObject(value);
                if (dictionary is null)
                {
                    result.Errors.Add($"{path} {DescribePathLeaf(path)} must be object.");
                    break;
                }

                foreach (var required in schema.Required)
                {
                    if (!dictionary.ContainsKey(required) || dictionary[required] is null)
                    {
                        result.Errors.Add($"{path} missing required prop '{required}'.");
                    }
                }

                foreach (var property in dictionary)
                {
                    if (schema.Properties.TryGetValue(property.Key, out var childSchema))
                    {
                        ValidateValue(property.Value, childSchema, $"{path}.{property.Key}", result);
                    }
                    else
                    {
                        result.Errors.Add($"{path} prop '{property.Key}' is not declared in props_schema.");
                    }
                }
                break;
            default:
                result.Errors.Add($"{path} uses unsupported schema type '{schema.Type}'.");
                break;
        }
    }

    private static IReadOnlyDictionary<string, object?>? NormalizeObject(object? value)
    {
        if (value is IReadOnlyDictionary<string, object?> readOnly)
        {
            return readOnly;
        }

        if (value is IDictionary<string, object?> dictionary)
        {
            return new Dictionary<string, object?>(dictionary, StringComparer.Ordinal);
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            return element.EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => ConvertJsonElement(property.Value),
                StringComparer.Ordinal);
        }

        if (value is IDictionary nonGenericDictionary)
        {
            var converted = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in nonGenericDictionary)
            {
                if (entry.Key is string key)
                {
                    converted[key] = entry.Value;
                }
            }

            return converted;
        }

        return null;
    }

    private static string DescribePathLeaf(string path)
    {
        var propMarker = ".props.";
        var propIndex = path.LastIndexOf(propMarker, StringComparison.Ordinal);
        if (propIndex < 0)
        {
            return string.Empty;
        }

        var leaf = path[(propIndex + propMarker.Length)..];
        var bracketIndex = leaf.IndexOf('[', StringComparison.Ordinal);
        if (bracketIndex >= 0)
        {
            leaf = leaf[..bracketIndex];
        }

        var dotIndex = leaf.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex >= 0)
        {
            leaf = leaf[..dotIndex];
        }

        return string.IsNullOrWhiteSpace(leaf) ? string.Empty : $"prop '{leaf}'";
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => ConvertJsonElement(property.Value),
                    StringComparer.Ordinal),
            JsonValueKind.Null => null,
            _ => null,
        };
    }
}
