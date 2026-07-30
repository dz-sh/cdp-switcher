using System.Collections.ObjectModel;
using System.Diagnostics;
using CdpSwitcher.Core.Chrome;
using CdpSwitcher.Core.Diagnostics;
using CdpSwitcher.Core.Gateway;
using CdpSwitcher.Core.Profiles;
using CdpSwitcher.Core.Switching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CdpSwitcher.App;

public sealed partial class MainWindow : Window
{
    private readonly CdpGateway _gateway;
    private readonly ManagedChromeController _chromeController;
    private readonly CdpSwitchCoordinator _switchCoordinator;
    private readonly ProfileStore _profileStore;
    private readonly ManagedProfilePaths _profilePaths;
    private readonly ManagedProfileDataRemover _profileDataRemover;
    private readonly SanitizedDiagnosticLog _diagnosticLog;
    private readonly Task _startupTask;
    private readonly List<ProfileCatalogEntry> _catalogEntries = [];
    private IReadOnlyList<UnlinkedProfileData> _unlinkedProfileData = [];
    private bool _configurationLoaded;
    private bool _operationInProgress;
    private bool _shutdownInProgress;
    private bool _shutdownCompleted;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.SetIcon(
            Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "CdpSwitcher.ico"));

        _gateway = new CdpGateway();
        _profilePaths = ManagedProfilePaths.CreateDefault();
        var profileUseDetector = new ChromeProfileUseDetector();
        _profileDataRemover = new ManagedProfileDataRemover(
            _profilePaths,
            profileUseDetector);
        _diagnosticLog = SanitizedDiagnosticLog.CreateDefault();
        _chromeController = new ManagedChromeController(
            new ChromeLocator(),
            _profilePaths,
            new ChromeBackendVerifier(),
            profileUseDetector);
        _switchCoordinator = new CdpSwitchCoordinator(
            _gateway,
            _chromeController);
        _switchCoordinator.StateChanged +=
            SwitchCoordinator_StateChanged;
        _profileStore = ProfileStore.CreateDefault();
        RenderLifecycleState(_switchCoordinator.State);
        RecordLifecycleTransition(_switchCoordinator.State);
        _startupTask = StartGatewayAsync();
        UpdateButtonState();

        ProfileList.SelectionChanged += ProfileList_SelectionChanged;
        AppWindow.Closing += AppWindow_Closing;
    }

    public ObservableCollection<ProfileListItemViewModel> Profiles { get; } =
        [];

    private async void AddProfileButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var profile = await ProfileEditorDialog.ShowAsync(
            Content.XamlRoot,
            existingProfile: null,
            name => ProfileNameExists(name));
        if (profile is null)
        {
            return;
        }

        await RunOperationAsync(
            async () =>
            {
                var proposedEntries = _catalogEntries
                    .Append(ProfileCatalogEntry.Create(profile))
                    .ToArray();
                await SaveCatalogAsync(proposedEntries);
                var item = new ProfileListItemViewModel(profile);
                Profiles.Add(item);
                UpdateProfileCollectionState();
                ProfileList.SelectedItem = item;
            });
    }

    private async void EditButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not
            ProfileListItemViewModel selected)
        {
            return;
        }

        var edited = await ProfileEditorDialog.ShowAsync(
            Content.XamlRoot,
            selected.Profile,
            name => ProfileNameExists(name, selected.Id));
        if (edited is null)
        {
            return;
        }

        var proposedEntries = _catalogEntries
            .Select(
                entry => entry.Profile.Id == edited.Id
                    ? entry.WithProfile(edited)
                    : entry)
            .ToArray();

        await RunOperationAsync(
            async () =>
            {
                await SaveCatalogAsync(proposedEntries);
                await _switchCoordinator.UpdateProfileAsync(
                    edited,
                    CancellationToken.None);
                selected.UpdateProfile(edited);
                UpdateSelectedProfilePanel();
            });
    }

    private async void RemoveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not
            ProfileListItemViewModel selected)
        {
            return;
        }

        var profile = selected.Profile;
        if (_switchCoordinator.State.ManagedProfile?.Id == profile.Id)
        {
            ShowTransientMessage(
                $"Stop {profile.Name} before removing it.");
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = $"Remove \"{profile.Name}\" from the app?",
            Content =
                "The profile will leave the main list and can be restored " +
                "later. Its Chrome browser data will remain on this computer.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await RunOperationAsync(
            async () =>
            {
                var proposedEntries = _catalogEntries
                    .Select(
                        entry => entry.Profile.Id == profile.Id
                            ? entry.WithState(
                                ProfileCatalogState.Removed)
                            : entry)
                    .ToArray();
                await SaveCatalogAsync(proposedEntries);

                Profiles.Remove(selected);
                ProfileList.SelectedItem = null;
                UpdateProfileCollectionState();
            });
    }

    private async void RemovedProfilesButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var items = _catalogEntries
            .Where(
                entry =>
                    entry.State == ProfileCatalogState.Removed)
            .Select(RemovedProfileDialogItem.FromEntry)
            .Concat(
                _unlinkedProfileData.Select(
                    RemovedProfileDialogItem.FromUnlinked))
            .ToArray();
        if (items.Length == 0)
        {
            UpdateProfileCollectionState();
            return;
        }

        var result = await RemovedProfilesDialog.ShowAsync(
            Content.XamlRoot,
            items);
        if (result.Item is null ||
            result.Action == RemovedProfileAction.None)
        {
            return;
        }

        if (result.Action == RemovedProfileAction.Restore)
        {
            await RestoreRemovedProfileAsync(result.Item);
            return;
        }

        await DeleteRemovedProfileAsync(result.Item);
    }

    private async Task RestoreRemovedProfileAsync(
        RemovedProfileDialogItem item)
    {
        if (item.Entry is not null)
        {
            await RunOperationAsync(
                async () =>
                {
                    var proposedEntries = _catalogEntries
                        .Select(
                            entry => entry.Profile.Id == item.Id
                                ? entry.WithState(
                                    ProfileCatalogState.Visible)
                                : entry)
                        .ToArray();
                    await SaveCatalogAsync(proposedEntries);
                    var restored = new ProfileListItemViewModel(
                        item.Entry.Profile);
                    Profiles.Add(restored);
                    UpdateProfileCollectionState();
                    ProfileList.SelectedItem = restored;
                });
            return;
        }

        var name = await RemovedProfilesDialog.AskForNameAsync(
            Content.XamlRoot,
            candidate => ProfileNameExists(candidate));
        if (name is null)
        {
            return;
        }

        await RunOperationAsync(
            async () =>
            {
                var profile = BrowserProfile.Restore(
                    item.Id,
                    name,
                    []);
                var proposedEntries = _catalogEntries
                    .Append(ProfileCatalogEntry.Create(profile))
                    .ToArray();
                await SaveCatalogAsync(proposedEntries);
                var restored = new ProfileListItemViewModel(profile);
                Profiles.Add(restored);
                UpdateProfileCollectionState();
                ProfileList.SelectedItem = restored;
            });
    }

    private async Task DeleteRemovedProfileAsync(
        RemovedProfileDialogItem item)
    {
        if (_switchCoordinator.State.ManagedProfile?.Id == item.Id)
        {
            ShowTransientMessage(
                "Stop this profile before permanently deleting it.");
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = item.Entry is null
                ? "Delete unlinked local data permanently?"
                : $"Delete \"{item.Entry.Profile.Name}\" permanently?",
            Content =
                "This deletes its cookies, sign-ins, history, and settings " +
                "from this computer. This cannot be undone.",
            PrimaryButtonText = "Delete permanently",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await RunOperationAsync(
            async () =>
            {
                _profileDataRemover.Delete(item.Id);
                if (item.Entry is not null)
                {
                    var proposedEntries = _catalogEntries
                        .Where(entry => entry.Profile.Id != item.Id)
                        .ToArray();
                    await SaveCatalogAsync(proposedEntries);
                }
                else
                {
                    RefreshUnlinkedProfileData();
                }

                UpdateProfileCollectionState();
            });
    }

    private async void ActivateButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not
            ProfileListItemViewModel selected)
        {
            return;
        }

        var profile = selected.Profile;
        var lifecycle = _switchCoordinator.State;
        if (lifecycle.Status == CdpLifecycleStatus.Active &&
            lifecycle.ManagedProfile?.Id == profile.Id)
        {
            return;
        }

        if (!await ConfirmActivationAsync(profile))
        {
            return;
        }

        await RunOperationAsync(
            async () =>
            {
                await ExecuteWithForceCloseConfirmationAsync(
                    () => _switchCoordinator.ActivateAsync(
                        profile,
                        CancellationToken.None));
            });
    }

    private async void StopButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RunOperationAsync(
            async () =>
            {
                await ExecuteWithForceCloseConfirmationAsync(
                    () => _switchCoordinator.StopAsync(
                        CancellationToken.None));
            });
    }

    private async Task StartGatewayAsync()
    {
        try
        {
            var configuration = await _profileStore.LoadAsync(
                CancellationToken.None);
            _catalogEntries.AddRange(configuration.Entries);
            RefreshUnlinkedProfileData();
            foreach (var profile in configuration.VisibleProfiles)
            {
                Profiles.Add(new ProfileListItemViewModel(profile));
            }

            _configurationLoaded = true;
            UpdateProfileCollectionState();
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            await _switchCoordinator.ReportInitializationFailureAsync(
                exception);
            UpdateButtonState();
            return;
        }

        try
        {
            await _switchCoordinator.InitializeAsync(
                Profiles
                    .Select(item => item.Profile)
                    .ToArray(),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }
        finally
        {
            UpdateButtonState();
        }
    }

    private async Task RunOperationAsync(Func<Task> operation)
    {
        if (_operationInProgress)
        {
            return;
        }

        _operationInProgress = true;
        var stopwatch = Stopwatch.StartNew();
        UpdateButtonState();

        try
        {
            await _startupTask;
            await operation();
        }
        catch (Exception exception)
        {
            RecordFailure(exception, stopwatch.Elapsed);
            ShowTransientMessage(GetUserMessage(exception));
        }
        finally
        {
            _operationInProgress = false;
            UpdateButtonState();
        }
    }

    private async Task<bool> ConfirmActivationAsync(
        BrowserProfile profile)
    {
        var content = new StackPanel
        {
            Spacing = 10,
        };
        content.Children.Add(
            new TextBlock
            {
                Text =
                    "The connected CDP client will be able to control " +
                    "this browser profile, including while you sign in, " +
                    "until you stop it. Control continues if the RDP " +
                    "window is disconnected or Windows is locked.",
                TextWrapping = TextWrapping.Wrap,
            });
        if (profile.Tags.Count > 0)
        {
            content.Children.Add(
                new ItemsControl
                {
                    ItemsSource = profile.Tags,
                    ItemTemplate =
                        (DataTemplate)Application
                            .Current
                            .Resources["TagChipTemplate"],
                });
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = $"Activate \"{profile.Name}\"?",
            Content = content,
            PrimaryButtonText = "Activate",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ExecuteWithForceCloseConfirmationAsync(
        Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (ManagedChromeDidNotCloseException)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Chrome did not close",
                Content =
                    "CDP Switcher can force-close only the Chrome process " +
                    "it started. Unsaved browser activity may be lost.",
                PrimaryButtonText = "Force close",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                throw new InvalidOperationException(
                    "Chrome is still open. Close it manually and try again.");
            }

            await _switchCoordinator.ForceStopAsync(
                CancellationToken.None);
            await operation();
        }
    }

    private void ProfileList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateSelectedProfilePanel();
        UpdateButtonState();
    }

    private void UpdateButtonState()
    {
        var selected =
            ProfileList.SelectedItem as ProfileListItemViewModel;
        var lifecycle = _switchCoordinator.State;
        var lifecycleBusy =
            lifecycle.Status is
                CdpLifecycleStatus.Starting or
                CdpLifecycleStatus.Switching;
        var canEditMetadata =
            _configurationLoaded &&
            !_operationInProgress &&
            !lifecycleBusy;
        var canOperateLifecycle =
            lifecycle.OperationsAvailable &&
            !_operationInProgress &&
            !lifecycleBusy;
        var selectedIsActive =
            selected is not null &&
            lifecycle.Status == CdpLifecycleStatus.Active &&
            lifecycle.ManagedProfile?.Id == selected.Id;
        var selectedIsManaged =
            selected is not null &&
            lifecycle.ManagedProfile?.Id == selected.Id;

        AddProfileButton.IsEnabled = canEditMetadata;
        RemovedProfilesButton.IsEnabled = canEditMetadata;
        EditButton.IsEnabled =
            canEditMetadata &&
            selected is not null;
        RemoveButton.IsEnabled =
            canEditMetadata &&
            selected is not null &&
            !selectedIsManaged;
        ActivateButton.IsEnabled =
            canOperateLifecycle &&
            selected is not null &&
            !selectedIsActive;
        ActivateButton.Content =
            selectedIsActive
                ? "Active"
                : "Activate";
        StopButton.IsEnabled =
            canOperateLifecycle &&
            lifecycle.IsChromeRunning;
        StopButton.Visibility = lifecycle.IsChromeRunning
            ? Visibility.Visible
            : Visibility.Collapsed;
        StopButton.Content = lifecycle.ManagedProfile is null
            ? "Stop"
            : $"Stop {lifecycle.ManagedProfile.Name}";
    }

    private bool ProfileNameExists(
        string name,
        Guid? excludedProfileId = null)
    {
        return _catalogEntries.Any(
            entry =>
                entry.Profile.Id != excludedProfileId &&
                string.Equals(
                    entry.Profile.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateProfileCollectionState()
    {
        var empty = Profiles.Count == 0;
        EmptyState.Visibility = empty
            ? Visibility.Visible
            : Visibility.Collapsed;
        var removedCount =
            _catalogEntries.Count(
                entry =>
                    entry.State == ProfileCatalogState.Removed) +
            _unlinkedProfileData.Count;
        RemovedProfilesButton.Content =
            $"Removed profiles ({removedCount})";
        RemovedProfilesButton.Visibility = removedCount > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task SaveCatalogAsync(
        IReadOnlyCollection<ProfileCatalogEntry> entries)
    {
        await _profileStore.SaveAsync(
            entries,
            CancellationToken.None);
        _catalogEntries.Clear();
        _catalogEntries.AddRange(entries);
        RefreshUnlinkedProfileData();
        UpdateProfileCollectionState();
    }

    private void RefreshUnlinkedProfileData()
    {
        _unlinkedProfileData =
            _profilePaths.FindUnlinkedProfileData(
                _catalogEntries.Select(entry => entry.Profile.Id));
    }

    private void UpdateSelectedProfilePanel()
    {
        if (ProfileList.SelectedItem is not
            ProfileListItemViewModel selected)
        {
            SelectedProfilePanel.Visibility =
                Visibility.Collapsed;
            SelectedProfileName.Text = string.Empty;
            SelectedProfileTags.ItemsSource = null;
            return;
        }

        SelectedProfilePanel.Visibility = Visibility.Visible;
        SelectedProfileName.Text = selected.Name;
        SelectedProfileTags.ItemsSource = selected.Tags;
    }

    private void SwitchCoordinator_StateChanged(
        object? sender,
        CdpLifecycleStateChangedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(
            () =>
            {
                RenderLifecycleState(args.State);
                UpdateRelationshipMarkers(args.State);
                RecordLifecycleTransition(args.State);
                UpdateButtonState();
            });
    }

    private void RenderLifecycleState(CdpLifecycleState state)
    {
        var detail = state.Status switch
        {
            CdpLifecycleStatus.Starting =>
                state.OperationsAvailable
                    ? "opening managed Chrome..."
                    : "checking Chrome and port 9222...",
            CdpLifecycleStatus.Active
                when state.ManagedProfile is not null =>
                $"{state.ManagedProfile.Name}. Sign in here when needed; " +
                "the CDP client can control this Chrome.",
            CdpLifecycleStatus.Switching =>
                "updating the managed Chrome...",
            CdpLifecycleStatus.Error
                when state.Failure is not null =>
                GetUserMessage(state.Failure),
            _ => null,
        };

        StatusText.Text = string.IsNullOrWhiteSpace(detail)
            ? state.Status.ToString()
            : $"{state.Status} — {detail}";
        CurrentSessionTags.ItemsSource =
            state.ManagedProfile?.Tags;
    }

    private void UpdateRelationshipMarkers(CdpLifecycleState state)
    {
        var activeProfileId =
            state.Status == CdpLifecycleStatus.Active
                ? state.ManagedProfile?.Id
                : null;
        foreach (var profile in Profiles)
        {
            profile.SetActive(profile.Id == activeProfileId);
        }
    }

    private void ShowTransientMessage(string message)
    {
        StatusText.Text =
            $"{_switchCoordinator.State.Status} — {message}";
    }

    private void RecordLifecycleTransition(CdpLifecycleState state)
    {
        var profileId =
            state.ManagedProfile?.Id ??
            (state.Failure switch
            {
                ManagedChromeExitedUnexpectedlyException exception =>
                    exception.Profile.Id,
                ActiveBackendLostException exception =>
                    exception.Profile.Id,
                _ => null,
            });
        _diagnosticLog.TryWrite(
            GetDiagnosticEvent(state.Status),
            profileId);

        if (state.Failure is
            ManagedChromeExitedUnexpectedlyException)
        {
            _diagnosticLog.TryWrite(
                DiagnosticEvent.ChromeExitedUnexpectedly,
                profileId);
        }
        else if (state.Failure is
                 ActiveBackendLostException)
        {
            _diagnosticLog.TryWrite(
                DiagnosticEvent.BackendLost,
                profileId);
        }
    }

    private void RecordFailure(
        Exception exception,
        TimeSpan? duration = null)
    {
        _diagnosticLog.TryWrite(
            DiagnosticEvent.OperationFailed,
            GetCurrentProfileId(),
            duration,
            exception);
    }

    private Guid? GetCurrentProfileId()
    {
        return _switchCoordinator.State.ManagedProfile?.Id ??
            (ProfileList.SelectedItem as
                ProfileListItemViewModel)?.Id;
    }

    private static DiagnosticEvent GetDiagnosticEvent(
        CdpLifecycleStatus state)
    {
        return state switch
        {
            CdpLifecycleStatus.Stopped => DiagnosticEvent.GatewayStopped,
            CdpLifecycleStatus.Starting => DiagnosticEvent.GatewayStarting,
            CdpLifecycleStatus.Active => DiagnosticEvent.GatewayActive,
            CdpLifecycleStatus.Switching => DiagnosticEvent.GatewaySwitching,
            CdpLifecycleStatus.Error => DiagnosticEvent.GatewayError,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
    }

    private static string GetUserMessage(Exception exception)
    {
        return exception switch
        {
            GatewayPortUnavailableException =>
                exception.Message,
            InvalidDataException =>
                "The saved profile list is invalid. Rename " +
                "%LOCALAPPDATA%\\CdpSwitcher\\config.json and restart.",
            UnauthorizedAccessException =>
                "A required local file is not accessible. Check permissions.",
            OperationCanceledException =>
                "The operation timed out.",
            HttpRequestException or
            System.Net.WebSockets.WebSocketException or
            System.Text.Json.JsonException =>
                "The Chrome CDP endpoint could not be verified. " +
                "Choose Activate to retry.",
            IOException =>
                "Local app data is unavailable or the profile is open " +
                "elsewhere. Resolve it and retry.",
            _ => exception.Message,
        };
    }

    private async void AppWindow_Closing(
        AppWindow sender,
        AppWindowClosingEventArgs args)
    {
        if (_shutdownCompleted)
        {
            return;
        }

        args.Cancel = true;
        if (_shutdownInProgress)
        {
            return;
        }

        _shutdownInProgress = true;
        _operationInProgress = true;
        UpdateButtonState();

        try
        {
            await _startupTask;
            await ExecuteWithForceCloseConfirmationAsync(
                () => _switchCoordinator.StopAsync(
                    CancellationToken.None));
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            _shutdownInProgress = false;
            _operationInProgress = false;
            ShowTransientMessage(GetUserMessage(exception));
            UpdateButtonState();
            return;
        }

        try
        {
            await _gateway.DisposeAsync();
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }

        try
        {
            _chromeController.Dispose();
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }

        _shutdownCompleted = true;
        Close();
    }
}
