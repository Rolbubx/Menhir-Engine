using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Serialization;

/// <summary>
/// Reads and writes ".scene" files: readable JSON holding a flat list of entities in
/// sibling order. Components are saved by reflection from their public read/write properties.
/// </summary>
/// <example>
/// <code>
/// {
///   "version": 1,
///   "nextLocalId": 3,
///   "entities": [
///     {
///       "id": 1,
///       "parent": 0,
///       "name": "Ground",
///       "transform": {
///         "position": [0, 0.5, 0],
///         "rotation": [0, 0, 0, 1],
///         "scale": [1, 1, 1]
///       },
///       "components": [
///         { "type": "MeshRenderer", "mesh": "Cube", "color": [0.5, 0.5, 0.5, 1] }
///       ]
///     }
///   ]
/// }
/// </code>
/// </example>
public static class SceneSerializer
{
    public const int CurrentVersion = 1;
    public const string Extension = ".scene";

    /// <summary>Options for component property values (vectors as arrays, enums as names).</summary>
    public static readonly JsonSerializerOptions ValueOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(),
            new Vector2Converter(),
            new Vector3Converter(),
            new Vector4Converter(),
            new QuaternionConverter(),
        },
    };

    public static string Serialize(Scene scene)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", CurrentVersion);
            writer.WriteNumber("nextLocalId", scene.NextLocalId);
            writer.WriteStartArray("entities");
            foreach (Entity entity in scene.Entities)
                WriteEntity(writer, entity);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>
    /// Parses a scene file. Problems that can be worked around (unknown component type, bad
    /// property value, missing parent) are reported in <paramref name="warnings"/>; anything
    /// else throws <see cref="SceneFormatException"/>.
    /// </summary>
    public static Scene Deserialize(string json, List<string> warnings)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? throw new SceneFormatException("The file is not a JSON object.");
        }
        catch (JsonException e)
        {
            throw new SceneFormatException($"Invalid JSON: {e.Message}", e);
        }

        int version = Get<int>(root, "version", 0);
        if (version < 1)
            throw new SceneFormatException("Missing or invalid \"version\".");
        if (version > CurrentVersion)
            throw new SceneFormatException($"The scene was saved by a newer editor (version {version}).");

        if (root["entities"] is not JsonArray entityArray)
            throw new SceneFormatException("Missing \"entities\" array.");

        var entities = new List<Entity>();
        foreach (JsonNode? node in entityArray)
        {
            if (node is not JsonObject entityObject)
                throw new SceneFormatException("An entry in \"entities\" is not an object.");
            entities.Add(ReadEntity(entityObject, warnings));
        }

        try
        {
            Scene scene = Scene.FromEntities(entities, Get<long>(root, "nextLocalId", 1), out List<Entity> fixedEntities);
            foreach (Entity entity in fixedEntities)
                warnings.Add($"'{entity.Name}' (id {entity.LocalId}) had a missing or invalid parent; it is now a root.");
            return scene;
        }
        catch (ArgumentException e)
        {
            throw new SceneFormatException(e.Message, e);
        }
    }

    private static void WriteEntity(Utf8JsonWriter writer, Entity entity)
    {
        writer.WriteStartObject();
        writer.WriteNumber("id", entity.LocalId);
        writer.WriteNumber("parent", entity.ParentId);
        writer.WriteString("name", entity.Name);

        writer.WriteStartObject("transform");
        WriteValue(writer, "position", entity.Transform.Position);
        WriteValue(writer, "rotation", entity.Transform.Rotation);
        WriteValue(writer, "scale", entity.Transform.Scale);
        writer.WriteEndObject();

        writer.WriteStartArray("components");
        foreach (Component component in entity.Components)
            WriteComponent(writer, component);
        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    private static void WriteComponent(Utf8JsonWriter writer, Component component)
    {
        writer.WriteStartObject();
        if (component is MissingComponent missing)
        {
            writer.WriteString("type", missing.TypeName);
            foreach ((string key, JsonNode? value) in missing.Data)
            {
                writer.WritePropertyName(key);
                if (value == null)
                    writer.WriteNullValue();
                else
                    value.WriteTo(writer);
            }
        }
        else
        {
            writer.WriteString("type", ComponentTypes.GetName(component.GetType()));
            foreach (PropertyInfo property in ComponentTypes.GetSerializedProperties(component.GetType()))
            {
                writer.WritePropertyName(JsonNamingPolicy.CamelCase.ConvertName(property.Name));
                JsonSerializer.Serialize(writer, property.GetValue(component), property.PropertyType, ValueOptions);
            }
        }
        writer.WriteEndObject();
    }

    private static void WriteValue<T>(Utf8JsonWriter writer, string name, T value)
    {
        writer.WritePropertyName(name);
        JsonSerializer.Serialize(writer, value, ValueOptions);
    }

    private static Entity ReadEntity(JsonObject json, List<string> warnings)
    {
        var entity = new Entity
        {
            LocalId = Get<long>(json, "id", 0),
            ParentId = Get<long>(json, "parent", 0),
            Name = Get(json, "name", "Entity") ?? "Entity",
        };

        if (json["transform"] is JsonObject transform)
        {
            entity.Transform.Position = Get(transform, "position", Vector3.Zero);
            Quaternion rotation = Get(transform, "rotation", Quaternion.Identity);
            entity.Transform.Rotation = rotation.LengthSquared() > 1e-8f ? Quaternion.Normalize(rotation) : Quaternion.Identity;
            entity.Transform.Scale = Get(transform, "scale", Vector3.One);
        }

        if (json["components"] is JsonArray components)
        {
            foreach (JsonNode? node in components)
            {
                if (node is JsonObject componentObject)
                    entity.Components.Add(ReadComponent(componentObject, entity, warnings));
            }
        }
        return entity;
    }

    private static Component ReadComponent(JsonObject json, Entity entity, List<string> warnings)
    {
        string typeName = Get(json, "type", "") ?? "";
        var data = (JsonObject)json.DeepClone();
        data.Remove("type");

        if (ComponentTypes.Create(typeName) is not { } component)
        {
            warnings.Add($"'{entity.Name}': unknown component type '{typeName}'; its data is kept as is.");
            return new MissingComponent(typeName, data);
        }

        foreach (PropertyInfo property in ComponentTypes.GetSerializedProperties(component.GetType()))
        {
            string key = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!data.TryGetPropertyValue(key, out JsonNode? value))
                continue; // not saved: keep the default
            data.Remove(key);
            try
            {
                property.SetValue(component, value.Deserialize(property.PropertyType, ValueOptions));
            }
            catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
            {
                warnings.Add($"'{entity.Name}': could not read {typeName}.{property.Name} ({e.Message}); using the default.");
            }
        }
        foreach ((string key, _) in data)
            warnings.Add($"'{entity.Name}': {typeName} has no property '{key}'; it was ignored.");

        return component;
    }

    private static T Get<T>(JsonObject json, string key, T fallback)
    {
        if (json[key] is not { } node)
            return fallback;
        try
        {
            return node.Deserialize<T>(ValueOptions) ?? fallback;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new SceneFormatException($"Invalid value for \"{key}\": {node.ToJsonString()}", e);
        }
    }
}
