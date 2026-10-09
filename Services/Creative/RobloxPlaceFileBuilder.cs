using System.Xml.Linq;

namespace SentinelX.Services.Creative;

/// <summary>Builds a text-mode Roblox place (.rbxlx) containing the generated server Script.
/// The output is an interchange project, not a claim that Roblox Studio accepted or ran it.</summary>
public static class RobloxPlaceFileBuilder
{
    private const int MaximumSourceLength = 1_000_000;

    public static string Build(string placeName, string serverSource)
    {
        if (string.IsNullOrWhiteSpace(placeName) || placeName.Length > 128)
            throw new ArgumentException("Nazwa place jest pusta albo za długa.", nameof(placeName));
        if (string.IsNullOrEmpty(serverSource) || serverSource.Length > MaximumSourceLength || serverSource.Contains('\0'))
            throw new ArgumentException("Źródło serwerowe jest puste albo przekracza limit.", nameof(serverSource));

        XElement Item(string className, string referent, string name, params XElement[] children) =>
            new("Item",
                new XAttribute("class", className),
                new XAttribute("referent", referent),
                new XElement("Properties", new XElement("string", new XAttribute("name", "Name"), name)),
                children);

        var script = new XElement("Item",
            new XAttribute("class", "Script"), new XAttribute("referent", "RBX3"),
            new XElement("Properties",
                new XElement("string", new XAttribute("name", "Name"), "SentinelGameBootstrap"),
                new XElement("bool", new XAttribute("name", "Disabled"), "false"),
                new XElement("ProtectedString", new XAttribute("name", "Source"), serverSource)));
        var serverScriptService = Item("ServerScriptService", "RBX2", "ServerScriptService", script);
        var workspace = new XElement("Item",
            new XAttribute("class", "Workspace"), new XAttribute("referent", "RBX1"),
            new XElement("Properties",
                new XElement("string", new XAttribute("name", "Name"), "Workspace"),
                new XElement("bool", new XAttribute("name", "StreamingEnabled"), "true"),
                new XElement("float", new XAttribute("name", "Gravity"), "196.2")));
        var dataModel = Item("DataModel", "RBX0", placeName, workspace, serverScriptService,
            Item("ReplicatedStorage", "RBX4", "ReplicatedStorage"),
            Item("StarterPlayer", "RBX5", "StarterPlayer"),
            Item("Lighting", "RBX6", "Lighting"));
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("roblox", new XAttribute("version", "4"),
                new XElement("External", "null"),
                new XElement("External", "nil"),
                dataModel));
        return document.ToString(SaveOptions.DisableFormatting) + "\n";
    }

    public static bool TryReadServerSource(string placeXml, out string source, out string error)
    {
        source = "";
        error = "";
        if (string.IsNullOrWhiteSpace(placeXml) || placeXml.Length > MaximumSourceLength * 2)
        {
            error = "Dokument place jest pusty albo przekracza limit.";
            return false;
        }
        try
        {
            using var reader = System.Xml.XmlReader.Create(new StringReader(placeXml), new System.Xml.XmlReaderSettings
            {
                DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumSourceLength * 2L,
                IgnoreComments = true
            });
            var document = XDocument.Load(reader, LoadOptions.None);
            if (document.Root?.Name != "roblox" || document.Root.Attribute("version")?.Value != "4")
            {
                error = "To nie jest plik XML place Roblox w wersji 4.";
                return false;
            }
            XElement? script = document.Descendants("Item").FirstOrDefault(item =>
                item.Attribute("class")?.Value == "Script" &&
                item.Element("Properties")?.Elements("ProtectedString").Any(x => x.Attribute("name")?.Value == "Source") == true);
            XElement? protectedSource = script?.Element("Properties")?.Elements("ProtectedString")
                .FirstOrDefault(x => x.Attribute("name")?.Value == "Source");
            if (protectedSource == null)
            {
                error = "W place nie znaleziono serwerowego Script.Source.";
                return false;
            }
            source = protectedSource.Value;
            return source.Length is > 0 and <= MaximumSourceLength;
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
        {
            error = "Nieprawidłowy XML place: " + ex.Message;
            return false;
        }
    }
}
