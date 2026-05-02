using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public static class DefaultComponentLibrary
{
    public static ComponentLibraryManifest Create()
    {
        return new ComponentLibraryLoader().LoadDefault();
    }

    public static ComponentDefinition Define(
        string type,
        string description,
        IEnumerable<string> supportedRoles,
        ComponentPropsSchema propsSchema,
        bool generated = false)
    {
        return new ComponentDefinition
        {
            Type = type,
            Description = description,
            SupportedRoles = supportedRoles.ToList(),
            PropsSchema = propsSchema,
            Generated = generated,
        };
    }

}
