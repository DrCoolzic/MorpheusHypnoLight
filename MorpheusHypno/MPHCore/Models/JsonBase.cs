// Ignore Spelling: Serializable Json deserialized deserializing

using System.Text;
using Newtonsoft.Json;

namespace MPHCore.Models;

/// <summary>
/// Base class for models that can be serialized and deserialized from JSON.
/// </summary>
public abstract class JsonBase
{
    /// <summary>
    /// Loads the JSON content from a file asynchronously into an instance of the specified type.
    /// </summary>
    public static async Task<T> LoadJsonFileAsync<T>(string filePath) where T : JsonBase
    {
        try
        {
            var jsonString = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            return JsonConvert.DeserializeObject<T>(jsonString) ?? throw new InvalidOperationException($"Error deserializing JSON file '{filePath}' to type {typeof(T).Name}");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Error deserializing JSON file '{filePath}' to type {typeof(T).Name}: {ex.Message}");
        }
    }


    /// <summary>
    /// Converts the instance to a JSON string and saves it to a file.
    /// </summary>
    public async Task SaveJsonFileAsync(string filePath)
    {
        try
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };
            var jsonString = JsonConvert.SerializeObject(this, settings);
            await File.WriteAllTextAsync(filePath, jsonString);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error creating directory for file '{filePath}': {ex.Message}");
        }

    }

}
