// Ignore Spelling: Metadata

using Newtonsoft.Json;

namespace MPHCore.Models;


/// <summary>
/// Contains metadata for MPHCollection and MPHSequence objects.
/// </summary>
public class ProgramMetadata : JsonBase
{
    public static readonly float MetadataVersion = 1.0F;

    /// <summary>
    /// the parent directory of the object.
    /// </summary>
    [JsonProperty("parent")]
    public string Parent { get; set; } = string.Empty;

    /// <summary>
    /// The name of the object as a dictionary of language keys and values.
    /// </summary>
    [JsonProperty("name")]
    public Dictionary<string, string> NameItems { get; set; } = new() { { "en", "" }, { "fr", "" } };

    /// <summary>
    /// A summary description of the object as a dictionary of language keys and values.
    /// </summary>
    [JsonProperty("summary")]
    public Dictionary<string, string> SummaryItems { get; set; } = new() { { "en", "" }, { "fr", "" } };

    /// <summary>
    /// the version number of the metadata format
    /// </summary>
    [JsonProperty("version")]
    public float Version { get; set; } = ProgramMetadata.MetadataVersion;

    /// <summary>
    /// The last time the Program or the Sequence was updated.
    /// </summary>
    [JsonProperty("last_updated")]
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// Indicates whether the program or sequence is protected from modifications.
    /// When true, prevents adding/deleting sequences in programs and editing sequences.
    /// </summary>
    [JsonProperty("protected")]
    public bool IsProtected { get; set; } = true;
}


/// <summary>
/// Extended metadata specific to sequences, containing additional properties like duration, category, etc.
/// Inherits from ProgramMetadata to maintain compatibility with the base metadata system.
/// </summary>
public class SequenceMetadata : ProgramMetadata
{
    /// <summary>
    /// Detailed description of the sequence in multiple languages.
    /// Keys are language codes (e.g., "en", "fr"), values are the descriptions.
    /// </summary>
    [JsonProperty("detail")]
    public Dictionary<string, string> DetailItems { get; set; } = new Dictionary<string, string> { { "en", "" }, { "fr", "" } };

    /// <summary>
    /// Duration of the sequence in milliseconds.
    /// </summary>
    [JsonProperty("duration")]
    public int Duration { get; set; } = 0;

    /// <summary>
    /// Category identifier for the sequence.
    /// Used to group sequences by type or purpose.
    /// </summary>
    [JsonProperty("category")]
    public int Category { get; set; } = 0;

    /// <summary>
    /// Difficulty or intensity level of the sequence.
    /// </summary>
    [JsonProperty("level")]
    public int Level { get; set; } = 0;
}



public class Userdata : JsonBase
{
    /// <summary>
    /// User rating or quality score for the sequence.
    /// </summary>
    [JsonProperty("rating")]
    public int Rating { get; set; } = 0; // User rating, not serialized
}
