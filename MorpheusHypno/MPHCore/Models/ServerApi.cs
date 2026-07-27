// Ignore Spelling: Username Auth deserialize deserialization

using MPHCore.Utilities;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace MPHCore.Models;

public class ApiProgram
{
    [JsonProperty("id")]
    public int Id { get; set; }

    private string _dirName = string.Empty;

    [JsonProperty("dir_name")]
    public string DirName
    {
        get => _dirName;
        set => _dirName = StringNormalizer.NormalizeString(value);
    }

    [JsonProperty("last_modified")]
    public DateTime LastModified { get; set; }

    [JsonProperty("files")]
    public List<ApiFile> Files { get; set; } = [];

    [JsonIgnore]
    public int FileCount => Files.Count;

    [JsonIgnore]
    public List<ApiSequence>? Sequences { get; set; }

    [JsonIgnore]
    public int SequenceCount => Sequences?.Count ?? 0;
}


public class ApiSequence
{
    [JsonProperty("id")]
    public int Id { get; set; }

    private string _dirName = string.Empty;

    [JsonProperty("dir_name")]
    public string DirName
    {
        get => _dirName;
        set => _dirName = StringNormalizer.NormalizeString(value);
    }

    [JsonProperty("program_id")]
    public int ProgramId { get; set; }

    [JsonProperty("last_modified")]
    public DateTime LastModified { get; set; }

    [JsonProperty("files")]
    public List<ApiFile> Files { get; set; } = [];

    [JsonIgnore]
    public int FileCount => Files.Count;
}


public class ApiFile
{
    [JsonProperty("id")]
    public int Id { get; set; } = 0;

    [JsonProperty("name")]
    public string FileName { get; set; } = string.Empty;

    [JsonProperty("size")]
    public long Size { get; set; }

    [JsonProperty("file_hash")]
    public string FileHash { get; set; } = string.Empty;
    
    [JsonProperty("program_id")]
    public int ProgramId { get; set; }
    
    [JsonProperty("sequence_id")]
    public int? SequenceId { get; set; }
}


/// <summary>
/// Represents a user as returned by the API.
/// </summary>
public class ApiUser
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("username")]
    public string Username { get; set; } = string.Empty;

    [JsonProperty("email")]
    public string Email { get; set; } = string.Empty;

    [JsonProperty("role")]
    public string Role { get; set; } = string.Empty;

    public bool IsAdmin 
    { 
        get => Role.Equals("admin", StringComparison.CurrentCultureIgnoreCase);
        set => Role = value ? "admin" : "user";
    }

    [JsonProperty("last_login")]
    public DateTime? LastLogin { get; set; }

    [JsonProperty("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonProperty("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Represents authentication data returned by the API.
/// </summary>
public class ApiAuthData
{
    [JsonProperty("token")]
    public string Token { get; set; } = string.Empty;

    [JsonProperty("user_id")]
    public int UserId { get; set; }

    [JsonProperty("role")]
    public string Role { get; set; } = string.Empty;
}

/// <summary>
/// Represents token validation data returned by the API.
/// </summary>
public class ApiTokenValidation
{
    [JsonProperty("valid")]
    public bool Valid { get; set; }

    [JsonProperty("user_id")]
    public int UserId { get; set; }

    [JsonProperty("role")]
    public string Role { get; set; } = string.Empty;
}

/// <summary>
/// Generic API response wrapper for direct deserialization from the API.
/// </summary>
public class ApiJson<T>
{
    /// <summary>
    /// whether the API request was successful
    /// </summary>
    [JsonProperty("success")]
    public bool Success { get; set; }

    /// <summary>
    /// data returned by the API
    /// </summary>
    [JsonProperty("data")]
    public T? Data { get; set; }

    /// <summary>
    /// error returned by the API
    /// </summary>
    [JsonProperty("error")]
    public ApiError? Error { get; set; }
}

/// <summary>
/// Generic API response wrapper.
/// </summary>
/// <summary>
/// Represents the type of error that occurred during an API operation
/// </summary>
public enum ApiErrorType
{
    /// <summary>
    /// No error occurred
    /// </summary>
    None,
    
    /// <summary>
    /// Authentication failed due to invalid credentials
    /// </summary>
    AuthenticationFailed,
    
    /// <summary>
    /// Server returned an error response
    /// </summary>
    ServerError,
    
    /// <summary>
    /// Connection to the server failed (server down or network issue)
    /// </summary>
    ConnectionFailed,
    
    /// <summary>
    /// Error parsing the server response
    /// </summary>
    ResponseParsingError,
    
    /// <summary>
    /// Other unspecified error
    /// </summary>
    Other
}

public class ApiResponse<T>
{
    /// <summary>
    /// whether the API request was successful
    /// </summary>
    [JsonProperty("success")]
    public bool Success { get; set; }

    /// <summary>
    /// data returned by the API
    /// </summary>
    [JsonProperty("data")]
    public T? Data { get; set; }

    /// <summary>
    /// error returned by the API
    /// </summary>
    [JsonProperty("error")]
    public string? Error { get; set; }
    
    /// <summary>
    /// Detailed error information
    /// </summary>
    [JsonProperty("errorDetails")]
    public ApiError? ErrorDetails { get; set; }
    
    /// <summary>
    /// Type of error that occurred
    /// </summary>
    [JsonProperty("errorType")]
    public ApiErrorType ErrorType { get; set; } = ApiErrorType.None;
    
    /// <summary>
    /// Creates an error response
    /// </summary>
    public static ApiResponse<T> CreateError(ApiError error, ApiErrorType errorType = ApiErrorType.ServerError)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Error = error.Message,
            ErrorDetails = error,
            ErrorType = errorType
        };
    }
    
    /// <summary>
    /// Creates an error response with just a message
    /// </summary>
    public static ApiResponse<T> CreateError(string errorMessage, ApiErrorType errorType = ApiErrorType.Other)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Error = errorMessage,
            ErrorType = errorType
        };
    }
    
    /// <summary>
    /// Creates a success response with data
    /// </summary>
    public static ApiResponse<T> CreateSuccess(T data)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data
        };
    }
}

/// <summary>
/// Represents an error returned by the API.
/// </summary>
public class ApiError
{
    /// <summary>
    /// error code
    /// </summary>
    [JsonProperty("code")]
    public string? Code { get; set; }

    /// <summary>
    /// error message
    /// </summary>
    [JsonProperty("message")]
    public string? Message { get; set; }
    
    /// <summary>
    /// Default constructor
    /// </summary>
    public ApiError()
    {
    }
    
    /// <summary>
    /// Constructor with code and message
    /// </summary>
    public ApiError(string code, string message)
    {
        Code = code;
        Message = message;
    }

    // Custom converter for string errors
    public static implicit operator ApiError(string message)
    {
        return new ApiError { Message = message };
    }
}

/// <summary>
/// Represents the root of the remote database containing all programs and sequences.
/// </summary>
public class RemoteRoot
{
    /// <summary>
    /// List of all programs in the remote database
    /// </summary>
    public List<ApiProgram> Programs { get; set; } = [];

    /// <summary>
    /// Total number of programs
    /// </summary>
    [JsonIgnore]
    public int ProgramCount => Programs.Count;

    /// <summary>
    /// Total number of sequences across all programs
    /// </summary>
    [JsonIgnore]
    public int SequenceCount => Programs.Sum(p => p.SequenceCount);

    /// <summary>
    /// Total number of files across all programs and sequences
    /// </summary>
    [JsonIgnore]
    public int FileCount => Programs.Sum(p =>
    {
        static int selector(ApiSequence s) =>
                    s.Files?.Count(f => f.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                                       f.FileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) ?? 0;
        return p.Files.Count(f => f.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                                  f.FileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) +
                (p.Sequences?.Sum(selector) ?? 0);
    });

    [JsonIgnore]
    public bool IsLoaded = false;
}
