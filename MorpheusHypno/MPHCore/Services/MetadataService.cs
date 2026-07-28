using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using MPHCore.Models;

namespace MPHCore.Services;

public class MetadataService(ILogger<MetadataService> logger)
{
    private readonly ILogger<MetadataService> _logger = logger;


    /// <summary>
    /// Reads sequence metadata files in a sequenceDir.
    /// If there is no metadata.json file, it will create one using the old metadata format.
    /// </summary>
    /// <param name="sequenceDir">Directory containing metadata.json file</param>
    /// <returns>metadata info for the sequence</returns>
    public async Task<SequenceMetadata> LoadSequenceMetadataAsync(string sequenceDir)
    {
        string metadataFile = Path.Combine(sequenceDir, "metadata.json");
        if (File.Exists(metadataFile))
        {
            // ****************************************************************
            // This section uses the new metadata format (found metadata files)
            // ****************************************************************
            SequenceMetadata metadata = await JsonBase.LoadJsonFileAsync<SequenceMetadata>(metadataFile);
            if (metadata.Version < ProgramMetadata.MetadataVersion) // unless new version
                _logger?.LogWarning("The metadata file {} uses an older version {}", metadataFile, metadata.Version);

            return metadata;
        }
        _logger?.LogWarning("Need to create/update metadata for sequence {}", sequenceDir);

        // Load sequence from JSON format
        var jsonPath = Path.Combine(sequenceDir, "sequence.json");

        if (!File.Exists(jsonPath))
            throw new FileNotFoundException($"sequence.json not found in {sequenceDir}");

        _logger?.LogInformation("Loading sequence from JSON: {Path}", jsonPath);
        var sequence = await JsonBase.LoadJsonFileAsync<Sequence>(jsonPath);
        string? parent = Directory.GetParent(sequenceDir)?.Name;
        SequenceMetadata content;

        // Check for all description files
        var desc1EnPath = Path.Combine(sequenceDir, "description_1_en.txt");
        var desc2EnPath = Path.Combine(sequenceDir, "description_2_en.txt");
        var desc1FrPath = Path.Combine(sequenceDir, "description_1_fr.txt");
        var desc2FrPath = Path.Combine(sequenceDir, "description_2_fr.txt");
        if (File.Exists(desc1EnPath) && File.Exists(desc2EnPath) &&
            File.Exists(desc1FrPath) && File.Exists(desc2FrPath))
        {
            // Read description files
            var desc1En = await File.ReadAllTextAsync(desc1EnPath, Encoding.UTF8);
            var desc2En = await File.ReadAllTextAsync(desc2EnPath, Encoding.UTF8);
            var desc1Fr = await File.ReadAllTextAsync(desc1FrPath, Encoding.UTF8);
            var desc2Fr = await File.ReadAllTextAsync(desc2FrPath, Encoding.UTF8);

            var (categoryEn, levelEn, summaryEn) = ParseDescriptionFile(desc1En);
            var (cat2En, level2En, detailEn) = ParseDescriptionFile(desc2En);
            var (categoryFr, levelFr, summaryFr) = ParseDescriptionFile(desc1Fr);
            var (cat2Fr, level2Fr, detailFr) = ParseDescriptionFile(desc2Fr);

            // Check level consistency
            var levels = new[] { levelEn, level2En, levelFr, level2Fr }
                .Where(l => l != 0)  // Ignore unset levels (0)
                .Distinct()
                .ToList();
            if (levels.Count > 1)
            {
                _logger?.LogWarning("Inconsistent levels found in {}: {}", sequenceDir, string.Join(", ", levels));
            }

            // Use the first non-zero level found, or default to 0
            int finalLevel = levels.FirstOrDefault(l => l != 0);
            if (finalLevel == 0)
                _logger?.LogWarning("No valid level found in {}, using default level 0", sequenceDir);

            // Check category consistency and map to numbers
            var categoryEnNum = MapCategoryToNumber(categoryEn);
            var category2EnNum = MapCategoryToNumber(cat2En);
            var categoryFrNum = MapCategoryToNumber(categoryFr);
            var category2FrNum = MapCategoryToNumber(cat2Fr);

            var categories = new[] { categoryEnNum, category2EnNum, categoryFrNum, category2FrNum }
                .Where(c => c != 0)  // Ignore unset categories (0)
                .Distinct()
                .ToList();

            if (categories.Count > 1)
            {
                _logger?.LogWarning("Inconsistent categories found in {}: {}", sequenceDir, string.Join(", ", categories));
                _logger?.LogWarning("Categories found: EN1='{}', EN2='{}', FR1='{}', FR2='{}'", categoryEn, cat2En, categoryFr, cat2Fr);
            }

            // Use the first non-zero category found
            int finalCategory = categories.FirstOrDefault();
            if (finalCategory == 0)
            {
                _logger?.LogError("No valid category found in {}", sequenceDir);
            }

            content = new SequenceMetadata
            {
                SummaryItems = new Dictionary<string, string>
                {
                    { "en", summaryEn },
                    { "fr", summaryFr }
                },
                DetailItems = new Dictionary<string, string>
                {
                    { "en", detailEn },
                    { "fr", detailFr }
                },
                Category = finalCategory,
                Level = finalLevel,
                NameItems = new Dictionary<string, string>
                {
                    { "en", sequence.Name },
                    { "fr", sequence.Name }
                },
                Duration = sequence.DurationMs,
                Parent = parent ?? string.Empty,
                Version = ProgramMetadata.MetadataVersion,
                LastUpdated = DateTime.Now
            };
        }
        else
        {
            // No description files found we put bare minimum information
            content = new SequenceMetadata
            {
                NameItems = new Dictionary<string, string>
                {
                    { "en", sequence.Name },
                    { "fr", sequence.Name }
                },
                SummaryItems = new Dictionary<string, string>
                {
                    { "en", "Sorry no description" },
                    { "fr", "Désolé pas de description" }
                },
                DetailItems = new Dictionary<string, string>
                {
                    { "en", "Sorry no description" },
                    { "fr", "Désolé pas de description" }
                },
                Category = 0,
                Level = 0,
                Duration = sequence.DurationMs,
                Parent = parent ?? string.Empty,
                Version = ProgramMetadata.MetadataVersion,
                LastUpdated = DateTime.Now
            };
        }

        // if metafile did not exist we create one for further use
        await content.SaveJsonFileAsync(metadataFile);
        return content;
    }

    public async Task SaveSequenceMetadataAsync(SequenceMetadata metadata, string sequenceDir)
    {
        var metadataFile = Path.Combine(sequenceDir, "metadata.json");
        _logger?.LogInformation("Saving metadata for sequence {}", sequenceDir);
        await metadata.SaveJsonFileAsync(metadataFile);
    }



    /// <summary>
    /// Reads all metadata files in a sequenceDir and combines them into a LibMetadataContent object
    /// </summary>
    /// <param name="directory">Directory containing metadata_xx.json files</param>
    /// <returns>Combined metadata sb from all language files</returns>
    public async Task<ProgramMetadata> LoadProgramMetadataAsync(string directory)
    {
        try
        {
            // Get metadata
            var metadataFile = Path.Combine(directory, "metadata.json");
            if (File.Exists(metadataFile))
            {
                var metadata = await JsonBase.LoadJsonFileAsync<ProgramMetadata>(metadataFile);
                if (metadata.Version >= ProgramMetadata.MetadataVersion) // unless new version
                    return metadata;
            }

            _logger?.LogWarning("Need to create/update metadata for program {}", directory);
            var content = new ProgramMetadata
            {
                Parent = Directory.GetParent(directory)?.Name ?? string.Empty,
                NameItems = new Dictionary<string, string>
                    {
                        { "en", Path.GetFileName(directory) },
                        { "fr", Path.GetFileName(directory) }
                    },
                SummaryItems = new Dictionary<string, string>
                    {
                        { "en", "Sorry no description" },
                        { "fr", "Désolé pas de description" }
                    },
                Version = ProgramMetadata.MetadataVersion,
                LastUpdated = DateTime.Now
            };
            // if metafile did not exist we create one for further use
            await content.SaveJsonFileAsync(metadataFile);
            return content;
        }
        catch (Exception ex)
        {
            _logger?.LogError("Error reading metadata content from {sequenceDir}: {ex.Message}", directory, ex.Message);
            return new ProgramMetadata();
        }
    }

    public async Task SaveProgramMetadataAsync(ProgramMetadata metadata, string programDir)
    {
        var metadataFile = Path.Combine(programDir, "metadata.json");
        _logger?.LogInformation("Saving metadata for program {}", programDir);
        await metadata.SaveJsonFileAsync(metadataFile);
    }

    public static async Task<SequenceMetadata> LoadPlaylistMetadataAsync(string fileName)
    {
        // playlist metadata are the same as SequenceMetadata
        if (File.Exists(fileName))
        {
            var content = await JsonBase.LoadJsonFileAsync<SequenceMetadata>(fileName);
            return content;
        }
        else
        {
            throw new FileNotFoundException($"Playlist metadata file not found: {fileName}");
        }
    }



    static private (string category, int level, string content) ParseDescriptionFile(string htmlContent)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(htmlContent);

        // Get category - try both h4 and p tags
        var category = string.Empty;
        var categoryNodes = doc.DocumentNode.SelectNodes("//h4|//p");
        if (categoryNodes != null)
        {
            foreach (var node in categoryNodes)
            {
                // First try to find category in <em> tags
                var emTag = node.SelectSingleNode(".//em");
                if (emTag != null)
                {
                    // Check if it's inside a strong tag
                    var strongInEm = emTag.SelectSingleNode(".//strong");
                    if (strongInEm != null)
                    {
                        category = strongInEm.InnerText.Trim();
                        break;
                    }
                    category = emTag.InnerText.Trim();
                    break;
                }

                // If not found in em, try strong tags
                var strongTag = node.SelectSingleNode(".//strong");
                if (strongTag != null && IsCategory(strongTag.InnerText))
                {
                    category = strongTag.InnerText.Trim();
                    break;
                }
            }
        }

        // Get level from any h4 or p containing "Level" or "Niveau"
        int level = 0; // Default level (0 means not found)
        var levelPattern = @"(?:Level|Niveau)\s*(\d+)";
        var levelRegex = new Regex(levelPattern, RegexOptions.IgnoreCase);

        if (categoryNodes != null)
        {
            foreach (var node in categoryNodes)
            {
                var match = levelRegex.Match(node.InnerText);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int parsedLevel))
                {
                    level = parsedLevel;
                    break;
                }
            }
        }

        // Get sb from p tags, excluding the ones that contain category or level
        //var sb = string.Empty;
        StringBuilder sb = new();
        var pNodes = doc.DocumentNode.SelectNodes("//p");
        if (pNodes != null)
        {
            foreach (var p in pNodes)
            {
                // Skip if this p contains category or level
                if (p.InnerText.Contains("Level", StringComparison.OrdinalIgnoreCase) ||
                    p.InnerText.Contains("Niveau", StringComparison.OrdinalIgnoreCase) ||
                    IsCategory(p.InnerText))
                {
                    continue;
                }

                sb.AppendLine(HttpUtility.HtmlDecode(p.InnerHtml.Trim()));
                //break; // Take the first p that's not category or level
            }
        }
        string content = sb.ToString();

        //_logger.LogInformation($"Parsed description Category: {category} Level: {level} Length: {content.Length} chars");
        //_logger.LogInformation($"Parsed Content: {sb}");

        return (category, level, content);
    }

    static private bool IsCategory(string text)
    {
        var normalizedText = text.Trim().ToUpperInvariant();
        return normalizedText == "RELAXATION" ||
               normalizedText == "EXPLORATION" ||
               normalizedText == "EXPLORATON" ||
               normalizedText == "STIMULATION" ||
               normalizedText == "DÉTENTE";
    }

    static private int MapCategoryToNumber(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return 0;   // unknown category

        // Remove any whitespace and convert to uppercase for comparison
        category = category.Trim().ToUpperInvariant();
        // map categories to numbers
        var result = category switch
        {
            "RELAXATION" => 1,
            "EXPLORATION" => 2,
            "STIMULATION" => 3,
            "DÉTENTE" => 1,      // Relaxation
            "EXPLORATON" => 2,   // Exploration misspelled
            _ => 0 // Unknown category
        };
        return result;
    }
}
