// Ignore Spelling: Metadata

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace MPHCore.Models;


/// <summary>
/// Metadata for MPHSequence objects.
/// </summary>
public class SequenceMetadata : JsonBase
{
    public static readonly float MetadataVersion = 1.0F;

    /// <summary>
    /// the parent directory of the sequence.
    /// </summary>
    [JsonProperty("parent")]
    public string Parent { get; set; } = string.Empty;

    /// <summary>
    /// The name of the sequence as a dictionary of language keys and values.
    /// </summary>
    [JsonProperty("name")]
    public Dictionary<string, string> NameItems { get; set; } = new() { { "en", "" }, { "fr", "" } };

    /// <summary>
    /// A summary description of the sequence as a dictionary of language keys and values.
    /// </summary>
    [JsonProperty("summary")]
    public Dictionary<string, string> SummaryItems { get; set; } = new() { { "en", "" }, { "fr", "" } };

    /// <summary>
    /// the version number of the metadata format
    /// </summary>
    [JsonProperty("version")]
    public float Version { get; set; } = SequenceMetadata.MetadataVersion;

    /// <summary>
    /// The last time the sequence was updated.
    /// </summary>
    [JsonProperty("last_updated")]
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// Indicates whether the sequence is protected from modifications.
    /// When true, prevents editing the sequence.
    /// </summary>
    [JsonProperty("protected")]
    public bool IsProtected { get; set; } = true;

    /// <summary>
    /// Detailed description of the sequence in multiple languages.
    /// Keys are language codes (e.g., "en", "fr"), values are the descriptions.
    /// </summary>
    [JsonProperty("detail")]
    public Dictionary<string, string> DetailItems { get; set; } = new Dictionary<string, string> { { "en", "" }, { "fr", "" } };

    /// <summary>
    /// Duration of the sequence in seconds.
    /// </summary>
    [JsonProperty("duration")]
    public double DurationSeconds { get; set; } = 0;

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



public class Userdata : JsonBase, INotifyPropertyChanged
{
    private int _rating;

    /// <summary>
    /// User rating or quality score for the sequence.
    /// </summary>
    [JsonProperty("rating")]
    public int Rating
    {
        get => _rating;
        set
        {
            if (_rating == value)
                return;

            _rating = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event for the given property.
    /// </summary>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
