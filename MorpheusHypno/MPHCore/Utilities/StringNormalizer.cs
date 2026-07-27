using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MPHCore.Utilities;

/*
NormalizeString - Handles basic normalization by removing accents, converting to lowercase, and replacing special characters with underscores.
ToSafeFolderName - Builds on the normalized string by handling platform-specific restrictions like Windows reserved names and file length limitations.
*/

/// <summary>
/// Provides string normalization utilities for handling special characters, accents, and formatting
/// </summary>
public static class StringNormalizer
{
    /// <summary>
    /// Normalizes a string by:
    /// 1. Converting accents and ligatures to their basic form (é -> e, æ -> ae)
    /// 2. Converting to lowercase
    /// 3. Replacing non-alphanumeric characters with underscores
    /// </summary>
    /// <param name="input">The string to normalize</param>
    /// <returns>The normalized string</returns>
    public static string NormalizeString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // Step 1: Convert to lowercase first
        input = input.ToLowerInvariant();

        // Step 2: Handle specific ligatures before normalization
        input = input.Replace("æ", "ae")
                    .Replace("œ", "oe")
                    .Replace("ß", "ss");

        // Step 3: Normalize to decomposed form (separate character from diacritic)
        string normalized = input.Normalize(NormalizationForm.FormD);

        // Step 4: Build new string without diacritics
        var newString = new StringBuilder();
        foreach (char c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                newString.Append(c);
            }
        }

        // Step 5: Replace remaining non-alphanumeric characters with underscores
        // This regex matches any character that is not a letter, number, or underscore
        string result = Regex.Replace(newString.ToString(), @"[^\w]", "_");
        
        // Step 6: Replace multiple consecutive underscores with a single one
        result = Regex.Replace(result, @"_+", "_");
        
        // Step 7: Trim underscores from start and end
        result = result.Trim('_');
        
        // Step 8: Ensure the string is not empty after all processing
        if (string.IsNullOrEmpty(result))
            return "unnamed";
            
        return result;
    }
    
    /// <summary>
    /// Creates a safe folder name from the input string by normalizing it
    /// and ensuring it complies with file system restrictions.
    /// </summary>
    /// <param name="input">The string to convert to a safe folder name</param>
    /// <returns>A safe folder name</returns>
    public static string ToSafeFolderName(string input)
    {
        // First normalize the string
        string normalized = NormalizeString(input);
        
        // Check for reserved names in Windows
        string[] reservedNames = { "con", "prn", "aux", "nul", 
                                  "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
                                  "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9" };
                                  
        if (reservedNames.Contains(normalized.ToLowerInvariant()))
            return normalized + "_folder";
            
        // Ensure the name doesn't start with a period (hidden file in Unix)
        if (normalized.StartsWith("."))
            normalized = "f" + normalized;
            
        // Limit length to avoid path length issues
        const int maxLength = 64;
        if (normalized.Length > maxLength)
            normalized = normalized.Substring(0, maxLength);
            
        return normalized;
    }
}
