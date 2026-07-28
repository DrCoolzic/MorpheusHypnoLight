// Ignore Spelling: MPH metadata

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MPHCore.Models;
using MPHCore.Utilities;

namespace MPHCore.Services;

/// <summary>
/// Service for managing Morpheus Hypno elements (collections and sequences).
/// Handles loading, saving, and validation of elements.
/// </summary>
public interface IMPHElementService
{
    /// <summary>
    /// Gets the root of the Morpheus Hypno database.
    /// </summary>
    MPHRoot MPHRoot { get; }

    /// <summary>
    /// Loads the local Morpheus Hypno database.
    /// If the database is already loaded, does nothing.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task LoadLocalDb();

    /// <summary>
    /// Loads a sequence from the specified directory.
    /// </summary>
    Task<Sequence> LoadSequenceAsync(string sequenceDir);
}

/// <summary>
/// Service for managing Morpheus Hypno elements (collections and sequences).
/// Handles loading, saving, and validation of elements.
/// </summary>
public class MPHElementService(ILogger<MPHElementService> logger, MetadataService metaDataService) : IMPHElementService
{
    private readonly ILogger<MPHElementService> _logger = logger;
    private readonly MetadataService _metadataService = metaDataService;
    public MPHRoot MPHRoot { get; } = new();

    /// <summary>
    /// Loads the local Morpheus Hypno database.
    /// If the database is already loaded, does nothing.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task LoadLocalDb()
    {
        if (MPHRoot.IsLoaded)
        {
            _logger.LogInformation("Local database already loaded");
            return;
        }

        // we load the list of MPHCollection (but NOT the sequences inside)
        var collectionsPath = Path.Combine(MPHRoot.RootPath, "collections");
        var collections = await LoadCollectionsAsync(collectionsPath);
        MPHRoot.Collections.Clear();
        foreach (var collection in collections)
            MPHRoot.Collections.Add(collection);
        _logger.LogInformation("Database root contains {Count} collections", MPHRoot.Collections.Count);

        // // if the Sequences directory exists, we load the sequences in Sessions program
        // // this should only happen for MPEditor
        // var sequencesPath = Path.Combine(MPHRoot.RootPath, "Sequences");
        // if (Directory.Exists(sequencesPath))
        // {
        //     _logger.LogInformation("DM directory contains a Sequences folder");
        //     var sequences = await LoadMPHSequencesAsync(sequencesPath);
        //     if (sequences.Count > 0)
        //     {
        //         var program = (new MPHCollection
        //         {
        //             DirPath = sequencesPath,
        //             DirName = "Sessions",
        //             SequenceItems = sequences,
        //             //Metadata = new ProgramMetadata
        //             //{
        //             //    NameItems = new Dictionary<string, string> { { "default", "Sessions" } },
        //             //    SummaryItems = new Dictionary<string, string>
        //             //    {
        //             //        { "en", "Sorry no description" },
        //             //        { "fr", "Désolé pas de description" }
        //             //    },
        //             //    Version = ProgramMetadata.MetadataVersion,
        //             //    LastUpdated = DateTime.Now,
        //             //},
        //             //GradientStops = MatchGradient(0) // Default gradient for sessions
        //         });
        //         MPHRoot.Collections.Add(program);
        //         //// check if metadata file exists otherwise create it
        //         //var metadataFile = Path.Combine(sequencesPath, "metadata.json");
        //         //if (!File.Exists(metadataFile))
        //         //{
        //         //    _logger?.LogInformation("Need to create metadata for program in {}", sequencesPath);
        //         //    var content = program.Metadata;
        //         //    await content.SaveJsonFileAsync(metadataFile);
        //         //}
        //     }
        // }

        // we load the list of playlists but NOT the sequences inside
        var playlistsPath = Path.Combine(MPHRoot.RootPath, "playlists");
        var playlists = await LoadPlaylistsAsync(playlistsPath);
        MPHRoot.PlaylistElements.Clear();
        foreach (var playlist in playlists)
            MPHRoot.PlaylistElements.Add(playlist);
        _logger?.LogInformation("Database root contains {Count} playlists", MPHRoot.PlaylistElements.Count);

        MPHRoot.Title = "Sessions";
        MPHRoot.IsLoaded = true;
    }

    /// <summary>
    /// Loads a sequence from the specified directory (sequence.json).
    /// </summary>
    private async Task<Sequence> LoadSequenceWithFallback(string sequenceDir)
    {
        var jsonPath = Path.Combine(sequenceDir, "sequence.json");

        if (File.Exists(jsonPath))
        {
            _logger.LogInformation("Loading sequence from JSON: {Path}", jsonPath);
            return await JsonBase.LoadJsonFileAsync<Sequence>(jsonPath);
        }

        throw new FileNotFoundException($"sequence.json not found in: {sequenceDir}");
    }

    /// <summary>
    /// Loads a sequence from the specified directory.
    /// </summary>
    public async Task<Sequence> LoadSequenceAsync(string sequenceDir)
    {
        return await LoadSequenceWithFallback(sequenceDir);
    }

    /// <summary>
    /// Load MPHSequences from a sequence
    /// </summary>
    /// <param name="directoryPath"></param>
    /// <returns></returns>
    /// <remarks>
    /// Here we do a Shallow reading of the sequences if possible:
    /// - if metadata exists it provides enough information to display all information in UI
    /// - otherwise we also need to read the actual sequence to get the name and gradient
    /// </remarks>
    public async Task<List<MPHSequence>> LoadMPHSequencesAsync(string directoryPath)
    {
        var sequences = new List<MPHSequence>();

        if (!Directory.Exists(directoryPath))
        {
            _logger?.LogWarning("Creating sequences directory: {}", directoryPath);
            Directory.CreateDirectory(directoryPath);
            return sequences;
        }

        // Get all directories in the Sequences folder
        var sequenceDirs = Directory.GetDirectories(directoryPath);

        foreach (var sequenceDir in sequenceDirs)
        {
            // Check if sequence contains sequence.json
            var sequenceJsonPath = Path.Combine(sequenceDir, "sequence.json");
            if (!File.Exists(sequenceJsonPath))
                continue;

            // read userdata if exists
            var userDataPath = Path.Combine(sequenceDir, "userdata.json");
            Userdata userData;
            if (!File.Exists(userDataPath))
            {
                userData = new Userdata(); // create empty user data
                // _logger.LogInformation("No user data found for sequence: {}", sequenceDir);
            }
            else
            {
                userData = await JsonBase.LoadJsonFileAsync<Userdata>(userDataPath);
                // _logger.LogInformation("User data loaded for sequence: {}", sequenceDir);
            }

            // Read metadata for the sequence
            var metadataContent = await _metadataService.LoadSequenceMetadataAsync(sequenceDir);
            var MPHSequence = new MPHSequence
            {
                Sequence = null,
                Metadata = metadataContent,
                DirPath = sequenceDir,
                DirName = StringNormalizer.NormalizeString(Path.GetFileName(sequenceDir)),
                IsModified = false,
                HasAudio = File.Exists(Path.Combine(sequenceDir, "sound.mp3")),
                Userdata = userData,
            };
            sequences.Add(MPHSequence);
        }
        _logger.LogInformation("{} directory contains {Count} sequences", Path.GetFileName(directoryPath), sequences.Count);
        return sequences;
    }

    /// <summary>
    /// Save the sequence.json and metadata.json to the specified directory path.
    /// DOES NOT save the sound files
    /// </summary>
    /// <param name="directoryPath">The path to the directory where the sequence should be saved</param>
    /// <param name="sequence">The sequence to save</param>
    /// <returns>Task representing the asynchronous operation</returns>
    public async Task SaveMPHSequencesAsync(string directoryPath, MPHSequence sequence)
    {
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
        if (sequence.Metadata is SequenceMetadata seqMetadata)
            await _metadataService.SaveSequenceMetadataAsync(seqMetadata, directoryPath);

        if (sequence.Sequence is not null)
        {
            var sequenceJsonPath = Path.Combine(directoryPath, "sequence.json");
            await sequence.Sequence.SaveJsonFileAsync(sequenceJsonPath);
            _logger.LogInformation("Saved sequence: {Path}", sequenceJsonPath);
        }
    }


    private async Task<List<MPHCollection>> LoadCollectionsAsync(string directoryPath)
    {
        var collections = new List<MPHCollection>();

        try
        {
            if (!Directory.Exists(directoryPath))
            {
                _logger.LogInformation("Creating collections directory: {}", directoryPath);
                Directory.CreateDirectory(directoryPath);
                return collections;    // empty collection list
            }

            // Get all directories in the collections folder
            var collectionDirs = Directory.GetDirectories(directoryPath);

            foreach (var dir in collectionDirs)
            {
                // Check if any subdirectory contains a sequence.json file
                var hasSequences = Directory.GetDirectories(dir)
                    .Any(subDir => File.Exists(Path.Combine(subDir, "sequence.json")));

                if (hasSequences)
                {
                    // we need to load the sequences for this collection
                    var sequenceItems = await LoadMPHSequencesAsync(dir);

                    // // Read metadata for the collection
                    // var collectionMetadata = await _metadataService.LoadProgramMetadataAsync(dir);

                    var collection = new MPHCollection
                    {
                        SequenceItems = sequenceItems,
                        //Metadata = collectionMetadata,
                        DirPath = dir,
                        DirName = StringNormalizer.NormalizeString(Path.GetFileName(dir)),
                        //GradientStops = ["#4A4", "#3f5", "#0F0"] // Gradient for collections
                    };
                    collections.Add(collection);
                }
            }
            return collections;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get collections in directory: {}", directoryPath);
            return collections;
        }
    }

    public async Task SaveCollectionAsync(string directoryPath, MPHCollection collection)
    {
        // Save the collection to the specified directory
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
        // Save the metadata
        //await _metadataService.SaveProgramMetadataAsync(collection.Metadata, directoryPath);

        // Save the sequences TODO ??? not sure if we need to save the sequences here
        foreach (var sequence in collection.SequenceItems)
        {
            await SaveMPHSequencesAsync(directoryPath, sequence);
        }
    }

    public MPHElement? SearchElement(string parent, Dictionary<string, string> nameItems)
    {
        // // Special handling for Sessions program (which uses "Sequences" directory)
        // // Check if we should look in the Sessions program
        // var sessionsProgram = MPHRoot.Collections.FirstOrDefault(p =>
        //     string.Equals(p.DirName, "Sessions", StringComparison.OrdinalIgnoreCase));

        // if (sessionsProgram != null)
        // {
        //     var sessionSequence = sessionsProgram.SequenceItems.FirstOrDefault(s =>
        //         s.Metadata.NameItems.Any(n => nameItems.ContainsKey(n.Key) &&
        //         string.Equals(n.Value, nameItems[n.Key], StringComparison.OrdinalIgnoreCase)));

        //     if (sessionSequence != null)
        //         return sessionSequence;
        // }

        // Search in collections
        var collection = MPHRoot.Collections.FirstOrDefault(p => string.Equals(p.DirName, parent, StringComparison.OrdinalIgnoreCase));
        var seqInCollection = collection?.SequenceItems.FirstOrDefault(s =>
            s.Metadata.NameItems.Any(n => nameItems.ContainsKey(n.Key) &&
            string.Equals(n.Value, nameItems[n.Key], StringComparison.OrdinalIgnoreCase)));
        return seqInCollection;
    }

    public async Task<List<MPHSequence>> LoadPlaylistsAsync(string playlistPath)
    {
        var playlists = new List<MPHSequence>();
        if (!Directory.Exists(playlistPath))
        {
            _logger.LogInformation("Creating playlists directory: {}", playlistPath);
            Directory.CreateDirectory(playlistPath);
            return playlists;   // empty playlist
        }

        var files = Directory.GetFiles(playlistPath, "metadata*.json", SearchOption.TopDirectoryOnly);
        foreach (string file in files)
        {
            var metadata = await MetadataService.LoadPlaylistMetadataAsync(file);

            // List<string> gradientStops = MatchGradient(metadata.Category);

            // we need to find the sequence
            var sequence = SearchElement(metadata.Parent, metadata.NameItems);
            if (sequence is not null)
            {
                // // Check for audio files in different languages and default
                // var audioItems = new Dictionary<string, bool>();
                // var defaultSoundPath = Path.Combine(sequence.DirPath, "son.mp3");
                // var frenchSoundPath = Path.Combine(sequence.DirPath, "son_fr.mp3");
                // var englishSoundPath = Path.Combine(sequence.DirPath, "sound_en.mp3");
                // if (File.Exists(defaultSoundPath))
                //     audioItems["default"] = true;
                // if (File.Exists(frenchSoundPath))
                //     audioItems["fr"] = true;
                // if (File.Exists(englishSoundPath))
                //     audioItems["en"] = true;
                // // Set Audio field based on existence of any sound file 1=hasSound, 2=hasNoSound
                // var audio = audioItems.Count > 0 ? 1 : 2;

                var playlist = new MPHSequence
                {
                    Metadata = metadata,
                    DirPath = sequence.DirPath,
                    IsModified = false,
                    Sequence = null,
                    FileName = file,
                    DirName = Path.GetFileName(sequence.DirPath),
                    HasAudio = File.Exists(Path.Combine(sequence.DirPath, "sound.mp3")),
                    //GradientStops = gradientStops,
                    //Audio = audio
                };
                playlists.Add(playlist);
            }
        }

        return playlists;
    }

}
