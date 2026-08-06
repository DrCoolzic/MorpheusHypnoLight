MorpheusHypno - Progress Notes
===============================
Last updated: 2026-08-05

## Session summary (Editor/Player mode + MainPage UI)

### 1. HTML rendering in sequence summary
- Added `MPHEditor/Converters/HtmlToFormattedStringConverter.cs`: parses simple HTML
  (via HtmlAgilityPack) into a `FormattedString` (bold/italic/br/p supported).
- Wired into `MainPage.xaml` (`DisplaySummary` label uses `FormattedText` + this converter).

### 2. RealtimeEditorPage scroll fix
- `RealtimeEditorPage.xaml`: wrapped the 5-oscillator `StepEditor` in a `ScrollView`
  (Play/Stop buttons stay fixed at top). Confirmed working on a Tab S6 Lite (10.4",
  1200x2000) where oscillator 5 was previously clipped with no way to reach it.

### 3. Editor vs Player mode mechanism (single project, no code duplication)
Decision history (see chat for full reasoning):
- Rejected: two separate .csproj (too much duplication risk).
- Rejected: platform-based flag (Windows=Editor, Android=Player) - too rigid.
- CHOSEN: dynamic window-width threshold (600 dp). Single source of truth:
  `MPHEditor/Services/EditorModeService.cs` (`IEditorModeService`), registered as a
  singleton in `MauiProgram.cs`.
  - `App.xaml.cs`: subscribes to `Window.SizeChanged`, calls
    `_editorModeService.UpdateFromWindowWidth(window.Width)` (also once at startup).
  - `AppShell.xaml.cs`: subscribes to the service's `PropertyChanged` to show/hide the
    "Realtime Editor" `FlyoutItem` (`x:Name="RealtimeEditorFlyoutItem"`, default
    `IsVisible="False"` in XAML to avoid a flash before first resize event).
  - `MainViewModel.cs`: injects `IEditorModeService`, exposes `[ObservableProperty] bool
    IsEditorMode`, synced via the service's `PropertyChanged` (marshalled onto
    `MainThread`).
- Known Android limitation (documented, NOT fixed - low priority since Android is
  Player-only for now): `RotaryButton` (`MPHEditor/Controls/RotaryButton.cs`) uses a
  `PanGestureRecognizer` on a `GraphicsView`; nested inside `ScrollView`s (horizontal in
  `OscillatorEditor.cs`, vertical in `RealtimeEditorPage.xaml`), Android's ScrollView
  touch interception breaks drag-to-change-value (tap-to-enter-value still works).
  Would need platform-specific `requestDisallowInterceptTouchEvent` if Android editor
  support becomes a priority again.

### 4. MainPage.xaml UI changes (editor-only affordances)
- Collection row: `Add`/`Delete` buttons added, `IsVisible="{Binding IsEditorMode}"`,
  bound to `AddProgramCommand`/`DeleteProgramCommand` (stubs) in `MainViewModel`.
- Per-sequence expanded row: 3 `ImageButton`s (plus.png / edit.png / play.png) bound to
  `PlaylistCommand` / `GoToEditCommand` / `GoToPlayCommand` (stubs, take `MPHSequence`
  parameter). `edit.png` button also has `IsVisible` bound to `IsEditorMode`.
  - IMPORTANT XAML pattern used: the `CollectionView.ItemTemplate`'s `DataTemplate` has
    `x:DataType="models:MPHSequence"`, so bindings to `MainViewModel` properties/commands
    from inside it must use:
    `{Binding Source={x:Reference SequenceList}, Path=BindingContext.XXX, x:DataType=vm:MainViewModel}`
    (`SequenceList` is the `x:Name` of the `CollectionView`).
- Bottom row: replaced the `BleStatusMessage` label with a sequence counter
  (`{Binding Sequences.Count, StringFormat='{0} Sequences'}`), added `Add`/`Delete`
  buttons (editor-only) bound to `AddSequenceCommand`/`DeleteSequenceCommand` (stubs).
  BLE icon + `ActivityIndicator` kept, moved to the last grid column.
  NOTE: `Sequences.Count` binding only refreshes when `Sequences` is reassigned wholesale
  (as done in `OnSelectedCollectionChanged`), not on in-place Add/Remove - keep this in
  mind once `AddSequenceCommand`/`DeleteSequenceCommand` are actually implemented.

## All RelayCommand stubs added to MainViewModel.cs (empty, TODO comments)
- `AddProgramCommand` / `DeleteProgramCommand` (collection add/delete)
- `AddSequenceCommand` / `DeleteSequenceCommand` (sequence add/delete)
- `PlaylistCommand(MPHSequence)` / `GoToEditCommand(MPHSequence)` / `GoToPlayCommand(MPHSequence)`

## Pending / next steps (not started)
- Implement the actual logic behind all the stub commands above.
- Decide on real navigation target/mechanism for `GoToEditCommand` (presumably a future
  `EditorPage`, not yet created) and `GoToPlayCommand`.
- Consider extracting shared UI (MainPage/MainViewModel/converters) into a separate MAUI
  class library (e.g. `MPHUI`) IF/WHEN a real `MPHPlayer` project is started - explicitly
  deferred for now per user decision (avoid premature generalization).
- Revisit Android RotaryButton drag bug if/when editor mode needs to work on Android.

Build status: solution builds successfully (`dotnet build MorpheusHypno.sln`) as of the
last change in this session.

## Session 2 (2026-08-06): PlayerPage adapted from old Morpheus project

Copied `PlayerPage.xaml`, `PlayerPage.xaml.cs`, `PlayerViewModel.cs` from the old Morpheus
project and adapted them in place (kept the same file names/locations):
- `MPHEditor/ViewModels/PlayerViewModel.cs`: renamed namespaces (`MPEditor.ViewModel` ->
  `MPHEditor.ViewModels`), `DmSequence` -> `MPHSequence`, `BaseViewModel` -> `ObservableObject`.
  Removed Category/Level display, language picker/`ILanguageService`/`CustomPickerPopup`
  dependency, and the `DmDSP`-based "Frequencies" clock readout entirely (none of that
  infra exists in this project). Aligned with the existing `ISequencePlayerService` API
  (`SetPlayerAsync`, `PausePlayerAsync` instead of the old sync `SetPlayer`/`PausePlayer`).
  Uses `IMPHElementService.LoadSequenceAsync` to lazily load full `Sequence` content
  on `[QueryProperty(nameof(MphSequence), "MPHSequence")]` change. Rating persists via
  `Userdata.SaveJsonFileAsync` (already existed on `JsonBase`).
  IMPORTANT FIX vs old code: `ISequencePlayerService` is a shared singleton in this project
  (unlike the old project) - removed the erroneous `_sequencePlayerService.Dispose()` call
  in `PlayerPage.xaml.cs`'s `OnDisappearing` (would have broken the service for the whole app).
- `MPHEditor/ViewModels/MainViewModel.cs`: added `PlaylistMode` stub property (used by
  `PlayerViewModel.SequenceEnded` for loop-vs-advance-to-next-playlist-item logic).
  `GoToPlayCommand` now navigates via `Shell.Current.GoToAsync(nameof(PlayerPage), ...)`
  passing the `MPHSequence` as a query parameter.
- `MPHEditor/AppShell.xaml.cs`: registered `PlayerPage` route via `Routing.RegisterRoute`
  (not a flyout item - reached only via `GoToPlayCommand` with a sequence parameter).
- New converters created (none of these existed, and none of the old project's XAML
  styles like `GradientPageStyle`/`DescriptionTitleLabel`/`IconImage`/`HtmlDetailLabel`
  existed either - replaced with plain inline properties):
  - `MPHEditor/Converters/PlayImageConverter.cs` (PlayerStateEnum -> play/pause icon)
  - `MPHEditor/Converters/RatingToStarsConverter.cs` (int 0-5 -> row of star images)
  - `MPHEditor/Converters/StringToBoolConverter.cs`, `NullToBoolConverter.cs` (visibility
    helpers for the optional Author/Version/CreatedAt metadata row - these DO exist on
    `MPHCore.Models.Sequence`, so that row was kept, just needed local converters)
- DI registration added in `MauiProgram.cs` for `PlayerViewModel`/`PlayerPage` (singleton,
  consistent with `RealtimeEditorPage` pattern).

Build status: solution builds successfully after this adaptation.

Deferred/not implemented in this pass:
- `MainPage.NeedsRefresh` static-flag refresh mechanism from the old project was dropped -
  not needed since `MPHSequence`/`Userdata` are shared references between `MainViewModel`'s
  `Sequences` collection and the `PlayerViewModel`, so rating changes are reflected in the
  same objects automatically (no explicit list refresh required with current UI).
- Tested on a real Android device (2026-08-06): PlayerPage navigation, BLE brightness,
  play/pause/stop, audio playback, slider drag seek, and rating persistence/display all work.
  Remaining: loop/playlist-advance behavior and any final polish.

## Player mode functional milestone (2026-08-06)

The player mode can be considered fully functional after end-to-end validation on Android:
- Editor-only controls are correctly hidden in player mode (no Realtime Editor flyout, no edit button).
- MainPage -> PlayerPage navigation passes the selected `MPHSequence` and loads it.
- BLE connection, sequence upload, playback, and audio are working.
- Brightness slider is debounced to avoid BLE flooding.
- User rating is displayed on MainPage and updated from PlayerPage.

Build status: solution builds successfully and has been validated on device.
