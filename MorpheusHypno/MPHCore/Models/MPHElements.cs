// Ignore Spelling: MPH Userdata

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MPHCore.Models;

/// <summary>
/// Base class for all Morpheus file system elements (collections, sequences, etc.).
/// Contains common properties shared by file-system elements such as an audio flag,
/// directory information, modification state, and user data.
/// </summary>
public class MPHElement : INotifyPropertyChanged
{
    /// <summary>
    /// Indicates if audio file exists
    /// </summary>
    public bool HasAudio { get; set; } = false;

    /// <summary>
    /// Indicates whether this element has been modified since last save.
    /// </summary>
    private bool _isModified;
    public bool IsModified
    {
        get => _isModified;
        set
        {
            if (_isModified != value)
            {
                _isModified = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Name of the directory containing this element.
    /// </summary>
    public string DirName { get; set; } = string.Empty;

    private string _directory = string.Empty;
    /// <summary>
    /// Directory path associated with this element.
    /// </summary>
    public string DirPath
    {
        get => _directory;
        set
        {
            _directory = value ?? string.Empty;  // Ensure we never assign null
                                                 // Normalize directory separators
            if (!string.IsNullOrEmpty(_directory))
            {
                _directory = _directory.Replace('\\', Path.DirectorySeparatorChar)
                                       .Replace('/', Path.DirectorySeparatorChar);
                if (!_directory.EndsWith(Path.DirectorySeparatorChar))
                    _directory += Path.DirectorySeparatorChar;
            }
        }
    }

    public Userdata Userdata { get; set; } = new Userdata();

    #region INotifyPropertyChanged Implementation
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
}

/// <summary>
/// Represents a Morpheus collection that can contain multiple sequences.
/// </summary>
public class MPHCollection : MPHElement
{
    /// <summary>
    /// List of sequences contained within this collection.
    /// </summary>
    public List<MPHSequence> SequenceItems { get; set; } = [];
}


/// <summary>
/// Represents a Morpheus sequence that contains oscillator patterns and settings.
/// </summary>
public class MPHSequence : MPHElement
{
    /// <summary>
    /// Metadata associated with this sequence, containing properties like name, description, etc.
    /// </summary>
    public SequenceMetadata Metadata { get; set; } = new();

    /// <summary>
    /// The actual sequence data containing steps, oscillators, etc.
    /// Can be null if the sequence hasn't been loaded yet.
    /// </summary>
    public Sequence? Sequence { get; set; } = null;

    /// <summary>
    /// Name of the sequence file, used only for playlist elements.
    /// </summary>
    public string FileName { get; set; } = string.Empty;


    /// <summary>
    /// Gets the number of .json and .mp3 files in the sequence directory.
    /// </summary>
    public int FileCount
    {
        get
        {
            if (Path.Exists(DirPath))
            {
                return Directory.GetFiles(DirPath, "metadata.json").Length
                     + Directory.GetFiles(DirPath, "sequence.json").Length
                     + Directory.GetFiles(DirPath, "*.mp3").Length;
            }
            return 0;
        }
    }
}

/// <summary>
/// Root container for Morpheus file-system elements.
/// Contains collections and playlist sequences.
/// </summary>
public class MPHRoot
{
    /// <summary>
    /// Title or name of this database root.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Root directory containing all database files.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// List of all collections in the database.
    /// </summary>
    public List<MPHCollection> Collections { get; set; } = [];

    /// <summary>
    /// List of playlist sequences.
    /// </summary>
    public List<MPHSequence> PlaylistElements { get; set; } = [];

    /// <summary>
    /// Indicates whether this database has been modified since last save.
    /// </summary>
    public bool IsModified { get; set; } = false;

    /// <summary>
    /// Indicates whether this database has been loaded from disk.
    /// </summary>
    public bool IsLoaded { get; set; } = false;
}
