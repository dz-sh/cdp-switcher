using CdpSwitcher.Core.Chrome;
using CdpSwitcher.Core.Profiles;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CdpSwitcher.App;

internal enum RemovedProfileAction
{
    None,
    Restore,
    DeletePermanently,
}

internal sealed record RemovedProfileDialogResult(
    RemovedProfileAction Action,
    RemovedProfileDialogItem? Item);

internal sealed class RemovedProfileDialogItem
{
    private RemovedProfileDialogItem(
        Guid id,
        string name,
        string details,
        IReadOnlyList<ProfileTag> tags,
        ProfileCatalogEntry? entry,
        UnlinkedProfileData? unlinkedData)
    {
        Id = id;
        Name = name;
        Details = details;
        Tags = tags;
        Entry = entry;
        UnlinkedData = unlinkedData;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string Details { get; }

    public IReadOnlyList<ProfileTag> Tags { get; }

    public ProfileCatalogEntry? Entry { get; }

    public UnlinkedProfileData? UnlinkedData { get; }

    public static RemovedProfileDialogItem FromEntry(
        ProfileCatalogEntry entry)
    {
        return new RemovedProfileDialogItem(
            entry.Profile.Id,
            entry.Profile.Name,
            entry.Profile.Tags.Count == 0 ? "No tags" : string.Empty,
            entry.Profile.Tags,
            entry,
            null);
    }

    public static RemovedProfileDialogItem FromUnlinked(
        UnlinkedProfileData data)
    {
        return new RemovedProfileDialogItem(
            data.Id,
            "Unlinked local data",
            $"{data.Id:N}  ·  Modified " +
            data.LastModifiedAt.LocalDateTime.ToString("g"),
            [],
            null,
            data);
    }
}

internal static class RemovedProfilesDialog
{
    public static async Task<RemovedProfileDialogResult> ShowAsync(
        XamlRoot xamlRoot,
        IReadOnlyList<RemovedProfileDialogItem> items)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(items);

        var rows = new StackPanel
        {
            MinWidth = 500,
            Spacing = 6,
        };
        var scrollViewer = new ScrollViewer
        {
            Content = rows,
            MaxHeight = 360,
            VerticalScrollBarVisibility =
                ScrollBarVisibility.Auto,
        };
        var content = new StackPanel
        {
            Spacing = 12,
        };
        content.Children.Add(
            new TextBlock
            {
                Text =
                    "Restore a profile to the main list, or permanently " +
                    "delete its local browser data.",
                TextWrapping = TextWrapping.Wrap,
            });
        content.Children.Add(scrollViewer);

        var result = new RemovedProfileDialogResult(
            RemovedProfileAction.None,
            null);
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Removed profiles",
            Content = content,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        };
        foreach (var item in items)
        {
            rows.Children.Add(
                CreateRow(
                    item,
                    action =>
                    {
                        result = new RemovedProfileDialogResult(
                            action,
                            item);
                        dialog.Hide();
                    }));
        }

        await dialog.ShowAsync();
        return result;
    }

    public static async Task<string?> AskForNameAsync(
        XamlRoot xamlRoot,
        Func<string, bool> profileNameExists)
    {
        var nameBox = new TextBox
        {
            Header = "Profile name",
            PlaceholderText = "For example, Restored account",
            MinWidth = 360,
        };
        var validation = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
        };
        var content = new StackPanel
        {
            Spacing = 8,
        };
        content.Children.Add(nameBox);
        content.Children.Add(validation);

        string? result = null;
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Restore unlinked profile data",
            Content = content,
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var name = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                validation.Text = "Enter a profile name.";
                validation.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }

            if (profileNameExists(name))
            {
                validation.Text =
                    "A profile with that name already exists.";
                validation.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }

            result = name;
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? result
            : null;
    }

    private static UIElement CreateRow(
        RemovedProfileDialogItem item,
        Action<RemovedProfileAction> selectAction)
    {
        var metadata = new StackPanel
        {
            Spacing = 4,
        };
        metadata.Children.Add(
            new TextBlock
            {
                Text = item.Name,
                FontWeight =
                    Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            });
        if (!string.IsNullOrWhiteSpace(item.Details))
        {
            metadata.Children.Add(
                new TextBlock
                {
                    Text = item.Details,
                    TextWrapping = TextWrapping.Wrap,
                });
        }

        if (item.Tags.Count > 0)
        {
            metadata.Children.Add(
                new ItemsControl
                {
                    ItemsSource = item.Tags,
                    ItemTemplate =
                        (DataTemplate)Application
                            .Current
                            .Resources["TagChipTemplate"],
                });
        }

        var restoreButton = new Button
        {
            Content = "Restore",
            VerticalAlignment = VerticalAlignment.Center,
        };
        restoreButton.Click +=
            (_, _) => selectAction(RemovedProfileAction.Restore);
        var deleteButton = new Button
        {
            Content = "Delete permanently...",
            VerticalAlignment = VerticalAlignment.Center,
        };
        deleteButton.Click +=
            (_, _) =>
                selectAction(
                    RemovedProfileAction.DeletePermanently);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        actions.Children.Add(restoreButton);
        actions.Children.Add(deleteButton);

        var grid = new Grid
        {
            ColumnSpacing = 16,
            Padding = new Thickness(10, 8, 10, 8),
        };
        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto,
            });
        grid.Children.Add(metadata);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }
}
