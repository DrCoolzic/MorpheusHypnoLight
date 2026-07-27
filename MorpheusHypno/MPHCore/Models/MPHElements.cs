// Ignore Spelling: Dm

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MPHCore.Models;

/// <summary>
/// Base class for all Dream Machine elements (programs, sequences, etc.).
/// Contains common properties like metadata, audio settings, and gradient information.
/// </summary>
public class DmElement : INotifyPropertyChanged
{
    /// <summary>
    /// Metadata associated with this element, containing properties like name, description, etc.
    /// </summary>
    public virtual ProgramMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Dictionary of audio items associated with this element.
    /// Key is the audio item identifier ("default","fr","en")
    /// Value indicates if it's enabled.
    /// </summary>
    public Dictionary<string, bool> AudioItems { get; set; } = [];

    /// <summary>
    /// Audio configuration value for this element.
    /// 0 = Unknown
    /// 1 = Has sound
    /// 2 = Has no sound
    /// </summary>
    public int Audio { get; set; } = 0;

    /// <summary>
    /// List of gradient color stops in hexadecimal format.
    /// Default gradient goes from light gray to dark gray.
    /// </summary>
    public List<string> GradientStops { get; set; } = ["#DDD", "#BBB", "#999"];

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
    /// Name of the directory containing this program.
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
/// Represents a Dream Machine program that can contain multiple sequences.
/// Programs are organized in directories and can be synchronized with the server.
/// </summary>
public class DmProgram : DmElement
{
    /// <summary>
    /// List of sequences contained within this program.
    /// </summary>
    public List<DmSequence> SequenceItems { get; set; } = [];


    /// <summary>
    /// Gets the number of .json and .mp3 files in the program directory.
    /// </summary>
    public int FileCount
    {
        get
        {
            if (Path.Exists(DirPath))
            {
                return System.IO.Directory.GetFiles(DirPath, "metadata.json").Length;
            }
            return 0;
        }
    }
}

/// <summary>
/// Represents a Dream Machine sequence that contains oscillator patterns and settings.
/// Sequences can exist standalone or within a program.
/// </summary>
public class DmSequence : DmElement
{
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

    private SequenceMetadata _metadata = new();
    /// <summary>
    /// Metadata specific to sequences, including duration, category, level, etc.
    /// </summary>
    public override ProgramMetadata Metadata
    {
        get => _metadata;
        set => _metadata = value as SequenceMetadata ?? new SequenceMetadata();
    }


}

/// <summary>
/// Root container for all Dream Machine elements in a local database.
/// Contains lists of programs, standalone sequences, and playlists.
/// </summary>
public class LocalRoot
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
    /// List of all programs in the database.
    /// </summary>
    public List<DmProgram> Programs { get; set; } = [];

    /// <summary>
    /// List of playlist sequences.
    /// </summary>
    public List<DmSequence> PlaylistElements { get; set; } = [];

    /// <summary>
    /// Indicates whether this database has been modified since last save.
    /// </summary>
    public bool IsModified { get; set; } = false;
    public bool IsLoaded { get; set; } = false;
}
