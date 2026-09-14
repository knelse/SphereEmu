using System.Reflection;
using System.Text.Json;

namespace SphServer.Helpers.Networking;

public sealed class MbcCatalog
{
    private readonly Dictionary<(string Side, int Tag, int Region, int? Command), MbcEventInfo> events;

    public IReadOnlyDictionary<int, MbcModuleSpec> Modules { get; }
    public IReadOnlySet<(string Side, int Tag, int Region)> CommandRegions { get; }

    private MbcCatalog(
        IReadOnlyDictionary<int, MbcModuleSpec> modules,
        Dictionary<(string Side, int Tag, int Region, int? Command), MbcEventInfo> events,
        HashSet<(string Side, int Tag, int Region)> commandRegions)
    {
        Modules = modules;
        this.events = events;
        CommandRegions = commandRegions;
    }

    public static MbcCatalog LoadEmbedded()
    {
        return new MbcCatalog(ReadModules(ReadEmbedded("sfera_protocol_schema.json")),
            ReadEvents(ReadEmbedded("sfera_event_names.json"), out var commandRegions), commandRegions);
    }

    public string ModuleName(int? tag)
    {
        if (tag is null)
        {
            return "";
        }

        return Modules.TryGetValue(tag.Value, out var module) ? module.Name : "";
    }

    public MbcEventInfo? SemanticEvent(MbcDirection direction, int moduleTag, int region, int? command)
    {
        var side = direction == MbcDirection.Client ? "C2S" : "S2C";
        if (events.TryGetValue((side, moduleTag, region, command), out var item))
        {
            return item;
        }

        if (command is not null && events.TryGetValue((side, moduleTag, region, null), out item))
        {
            return item;
        }

        return null;
    }

    private static string ReadEmbedded(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            throw new InvalidOperationException($"Embedded resource {fileName} was not found");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Embedded resource {fileName} could not be opened");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Dictionary<int, MbcModuleSpec> ReadModules(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var modules = new Dictionary<int, MbcModuleSpec>();
        if (!doc.RootElement.TryGetProperty("modules", out var modulesEl))
        {
            return modules;
        }

        foreach (var moduleProp in modulesEl.EnumerateObject())
        {
            if (!int.TryParse(moduleProp.Name, out var tag))
            {
                continue;
            }

            var name = moduleProp.Value.TryGetProperty("module", out var nameEl)
                ? nameEl.GetString() ?? ""
                : "";
            var regions = new Dictionary<int, MbcRegionSpec>();
            if (moduleProp.Value.TryGetProperty("regions", out var regionsEl))
            {
                foreach (var regionProp in regionsEl.EnumerateObject())
                {
                    if (!int.TryParse(regionProp.Name, out var region))
                    {
                        continue;
                    }

                    var desc = Array.Empty<int>();
                    if (regionProp.Value.TryGetProperty("desc", out var descEl) && descEl.ValueKind == JsonValueKind.Array)
                    {
                        desc = descEl.EnumerateArray().Select(x => x.GetInt32()).ToArray();
                    }

                    regions[region] = new MbcRegionSpec
                    {
                        Flags = regionProp.Value.TryGetProperty("flags", out var flagsEl) ? flagsEl.GetInt32() : 0,
                        Handler = regionProp.Value.TryGetProperty("handler", out var handlerEl)
                            ? handlerEl.GetString() ?? ""
                            : "",
                        Schema = regionProp.Value.TryGetProperty("schema", out var schemaEl)
                            ? schemaEl.GetString() ?? ""
                            : "",
                        Desc = desc
                    };
                }
            }

            modules[tag] = new MbcModuleSpec { Name = name, Regions = regions };
        }

        return modules;
    }

    private static Dictionary<(string Side, int Tag, int Region, int? Command), MbcEventInfo> ReadEvents(
        string json, out HashSet<(string Side, int Tag, int Region)> commandRegions)
    {
        var events = new Dictionary<(string Side, int Tag, int Region, int? Command), MbcEventInfo>();
        commandRegions = [];
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("events", out var eventsEl))
        {
            return events;
        }

        foreach (var eventProp in eventsEl.EnumerateObject())
        {
            if (!TryParseEventId(eventProp.Name, out var side, out var tag, out var region, out var command))
            {
                continue;
            }

            if (region is null)
            {
                continue;
            }

            var aliases = new List<string>();
            if (eventProp.Value.TryGetProperty("aliases", out var aliasesEl) && aliasesEl.ValueKind == JsonValueKind.Array)
            {
                aliases.AddRange(aliasesEl.EnumerateArray()
                    .Select(x => x.GetString())
                    .OfType<string>());
            }

            var info = new MbcEventInfo
            {
                EventId = eventProp.Name,
                Name = eventProp.Value.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "",
                Confidence = eventProp.Value.TryGetProperty("confidence", out var confEl)
                    ? confEl.GetString() ?? ""
                    : "",
                Evidence = eventProp.Value.TryGetProperty("evidence", out var evEl) ? evEl.GetString() ?? "" : "",
                Handler = eventProp.Value.TryGetProperty("handler", out var handlerEl)
                    ? handlerEl.GetString() ?? ""
                    : "",
                Aliases = aliases
            };
            events[(side, tag, region.Value, command)] = info;
            if (command is not null)
            {
                commandRegions.Add((side, tag, region.Value));
            }
        }

        return events;
    }

    private static bool TryParseEventId(string eventId, out string side, out int tag, out int? region,
        out int? command)
    {
        side = "";
        tag = 0;
        region = null;
        command = null;
        var parts = eventId.Split(':');
        if (parts.Length < 4)
        {
            return false;
        }

        side = parts[0];
        if (!int.TryParse(parts[1], out tag))
        {
            return false;
        }

        if (parts[2] != "*")
        {
            if (!int.TryParse(parts[2], out var regionVal))
            {
                return false;
            }

            region = regionVal;
        }

        if (parts[3] != "*" && int.TryParse(parts[3], out var commandVal))
        {
            command = commandVal;
        }

        return side is "C2S" or "S2C";
    }
}
