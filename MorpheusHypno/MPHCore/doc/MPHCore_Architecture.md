# MPHCore Library Architecture

## Table of Contents
1. [Overview](#overview)
2. [Architecture Diagram](#architecture-diagram)
3. [Core Components](#core-components)
   - [Models](#models)
   - [Services](#services)
   - [Utilities](#utilities)
4. [Key Flows](#key-flows)
5. [Design Patterns](#design-patterns)
6. [Error Handling](#error-handling)
7. [Security Considerations](#security-considerations)
8. [Performance Considerations](#performance-considerations)

## Overview

The MPHCore library serves as the backbone of the Dream Machine ecosystem, providing shared functionality between the MPPlayer and MPManager applications. It handles core business logic, data models, and server communication, ensuring consistency across different client applications.

## Architecture Diagram

```mermaid
graph TD
    subgraph Client Applications
        MPPlayer[MPPlayer App]
        MPManager[MPManager App]
    end
    
    subgraph MPHCore Library
        subgraph Models
            MPHElements[MPHElements]
            ServerApi[Server API Models]
            AppSettings[App Settings]
            DbComparison[DB Comparison]
        end
        
        subgraph Services
            DmServerService[DmServerService]
            DbComparisonService[DbComparisonService]
            MPHElementService[MPHElementService]
            SettingsService[Settings Service]
        end
        
        subgraph Utilities
            StringNormalizer[StringNormalizer]
            FileLogger[FileLogger]
            ApiConstants[ApiConstants]
        end
    end
    
    MPPlayer --> MPHCore
    MPManager --> MPHCore
    
    MPHCore --> |HTTP/HTTPS| Server[Server API]
    MPHCore --> |File I/O| LocalStorage[Local Storage]
```

## Core Components

### Models

The Models namespace contains the core data structures used throughout the application:

```mermaid
classDiagram
    class MPHElement {
        +bool HasAudio
        +bool IsModified
        +string DirName
        +string DirPath
        +Userdata Userdata
    }
    
    class MPHCollection {
        +List~MPHSequence~ SequenceItems
    }
    
    class MPHSequence {
        +SequenceMetadata Metadata
        +Sequence Sequence
        +string FileName
        +int FileCount
    }
    
    class MPHRoot {
        +string Title
        +string RootPath
        +List~MPHCollection~ Collections
        +List~MPHSequence~ PlaylistElements
    }
    
    MPHElement <|-- MPHCollection
    MPHElement <|-- MPHSequence
    MPHRoot "1" *-- "0..*" MPHCollection
    MPHRoot "1" *-- "0..*" MPHSequence
```

### Services

Key services that provide business logic and data access:

1. **DmServerService**: Handles all server communication
   - Authentication/Authorization
   - File upload/download
   - Program/Sequence management
   - Remote database operations

2. **DbComparisonService**: Compares local and remote databases
   - Identifies new/updated/deleted items
   - Tracks file differences
   - Generates sync recommendations

3. **MPHElementService**: Manages local Morpheus Hypno elements
   - Load/save operations
   - File system interactions
   - Metadata management

4. **SettingsService**: Manages application settings
   - User preferences
   - Connection settings
   - Local storage paths

### Utilities

- **StringNormalizer**: Ensures consistent string formatting
- **FileLogger**: Provides logging capabilities
- **ApiConstants**: Centralized API endpoint definitions

## Key Flows

### Database Synchronization

```mermaid
sequenceDiagram
    participant App as Client App
    participant MPHCore as MPHCore
    participant Server as Server API
    
    App->>MPHCore: StartSync()
    MPHCore->>Server: GetRemoteDatabase()
    Server-->>MPHCore: RemoteRoot
    MPHCore->>MPHCore: LoadLocalDatabase()
    MPHCore->>DbComparisonService: CompareDatabases(local, remote)
    DbComparisonService-->>MPHCore: DatabaseCompare
    MPHCore->>MPHCore: ResolveConflicts()
    MPHCore->>Server: UploadChanges()
    MPHCore->>Server: DownloadChanges()
    MPHCore->>MPHCore: UpdateLocalDatabase()
    MPHCore-->>App: SyncComplete
```

### File Upload/Download

```mermaid
graph LR
    subgraph Client
        A[Prepare File] --> B[Calculate Hash]
        B --> C[Check Server Status]
        C --> D[Upload in Chunks]
        D --> E[Verify Upload]
    end
    
    subgraph Server
        C --> F[Receive Chunks]
        F --> G[Reassemble File]
        G --> H[Verify Hash]
        H --> I[Update Database]
    end
    
    E -->|Success| J[Update Local State]
    E -->|Failure| K[Retry/Fail]
```

## Design Patterns

1. **Repository Pattern**:
   - Centralized data access through services
   - Abstraction of data sources (local/remote)

2. **Dependency Injection**:
   - Services are injected where needed
   - Promotes testability and loose coupling

3. **Observer Pattern**:
   - Notifications for data changes
   - UI updates through data binding

4. **Strategy Pattern**:
   - Different comparison strategies
   - Pluggable authentication methods

## Error Handling

- Comprehensive exception handling
- Retry mechanisms for network operations
- Detailed logging for troubleshooting
- User-friendly error messages

## Security Considerations

- Secure token-based authentication
- HTTPS for all server communications
- Input validation and sanitization
- Secure credential storage

## Performance Considerations

- Efficient chunked file transfers
- Background processing for long-running operations
- Caching of frequently accessed data
- Lazy loading of large resources

## Future Enhancements

1. **Offline Support**:
   - Queue operations for when offline
   - Conflict resolution strategies

2. **Performance Optimization**:
   - Parallel processing for file operations
   - Compression for network transfers

3. **Enhanced Security**:
   - End-to-end encryption
   - Two-factor authentication

4. **Extensibility**:
   - Plugin architecture for custom features
   - Webhook support for integrations
