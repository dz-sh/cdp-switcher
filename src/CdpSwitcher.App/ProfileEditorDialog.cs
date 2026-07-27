using CdpSwitcher.Core.Profiles;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CdpSwitcher.App;

internal static class ProfileEditorDialog
{
    private static readonly string[] DefaultColors =
    [
        "#2563EB",
        "#16A34A",
        "#9333EA",
        "#EA580C",
        "#DC2626",
        "#0891B2",
    ];

    public static async Task<BrowserProfile?> ShowAsync(
        XamlRoot xamlRoot,
        BrowserProfile? existingProfile,
        Func<string, bool> profileNameExists)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(profileNameExists);

        var nameBox = new TextBox
        {
            Header = "Profile name",
            PlaceholderText = "For example, Primary account",
            Text = existingProfile?.Name ?? string.Empty,
        };
        var validationText = new TextBlock
        {
            Foreground = new SolidColorBrush(
                Color.FromArgb(255, 196, 43, 28)),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        var tagRows = new StackPanel
        {
            Spacing = 8,
        };
        var rows = new List<TagEditorRow>();
        var addTagButton = new Button
        {
            Content = "Add tag",
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        void AddTag(ProfileTag? tag = null)
        {
            var color = tag?.Color ??
                DefaultColors[rows.Count % DefaultColors.Length];
            var row = CreateTagRow(
                tag?.Name ?? string.Empty,
                color,
                removed =>
                {
                    rows.Remove(removed);
                    tagRows.Children.Remove(removed.Root);
                });
            rows.Add(row);
            tagRows.Children.Add(row.Root);
            row.NameBox.Focus(FocusState.Programmatic);
        }

        addTagButton.Click += (_, _) => AddTag();
        if (existingProfile is not null)
        {
            foreach (var tag in existingProfile.Tags)
            {
                AddTag(tag);
            }
        }

        var tagHeader = new Grid
        {
            ColumnSpacing = 12,
        };
        tagHeader.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });
        tagHeader.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto,
            });
        var tagTitle = new TextBlock
        {
            Text = "Tags",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        tagHeader.Children.Add(tagTitle);
        Grid.SetColumn(addTagButton, 1);
        tagHeader.Children.Add(addTagButton);

        var tagScrollViewer = new ScrollViewer
        {
            Content = tagRows,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollBarVisibility =
                ScrollBarVisibility.Disabled,
            MaxHeight = 280,
            VerticalScrollBarVisibility =
                ScrollBarVisibility.Auto,
        };
        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 400,
            Spacing = 12,
        };
        content.Children.Add(nameBox);
        content.Children.Add(tagHeader);
        content.Children.Add(tagScrollViewer);
        content.Children.Add(validationText);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = existingProfile is null
                ? "Add profile"
                : "Edit profile",
            Content = content,
            PrimaryButtonText = existingProfile is null
                ? "Add"
                : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };

        BrowserProfile? result = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                var normalizedName = nameBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(normalizedName))
                {
                    throw new ArgumentException(
                        "Enter a profile name.");
                }

                if (profileNameExists(normalizedName))
                {
                    throw new ArgumentException(
                        "A profile with that name already exists.");
                }

                var tags = rows
                    .Select(
                        row => ProfileTag.Create(
                            row.NameBox.Text,
                            ToHex(row.ColorPicker.Color)))
                    .ToArray();

                result = existingProfile is null
                    ? BrowserProfile.Create(normalizedName, tags)
                    : existingProfile.Edit(normalizedName, tags);
                validationText.Visibility = Visibility.Collapsed;
            }
            catch (ArgumentException exception)
            {
                args.Cancel = true;
                validationText.Text = exception.Message;
                validationText.Visibility = Visibility.Visible;
            }
        };

        var response = await dialog.ShowAsync();
        return response == ContentDialogResult.Primary
            ? result
            : null;
    }

    private static TagEditorRow CreateTagRow(
        string name,
        string color,
        Action<TagEditorRow> remove)
    {
        var parsedColor = ParseColor(color);
        var nameBox = new TextBox
        {
            PlaceholderText = "Tag name",
            Text = name,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var colorPicker = new ColorPicker
        {
            Color = parsedColor,
            IsAlphaEnabled = false,
        };
        var swatch = new Border
        {
            Width = 24,
            Height = 24,
            Background = new SolidColorBrush(parsedColor),
            CornerRadius = new CornerRadius(4),
        };
        var colorButton = new Button
        {
            Content = swatch,
            Flyout = new Flyout
            {
                Content = colorPicker,
            },
            Padding = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(
            colorButton,
            "Choose tag color");
        colorPicker.ColorChanged += (_, args) =>
        {
            swatch.Background = new SolidColorBrush(args.NewColor);
        };

        var removeButton = new Button
        {
            Content = "Remove",
            VerticalAlignment = VerticalAlignment.Center,
        };

        var root = new Grid
        {
            ColumnSpacing = 8,
        };
        root.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });
        root.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto,
            });
        root.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto,
            });
        root.Children.Add(nameBox);
        Grid.SetColumn(colorButton, 1);
        root.Children.Add(colorButton);
        Grid.SetColumn(removeButton, 2);
        root.Children.Add(removeButton);

        var row = new TagEditorRow(root, nameBox, colorPicker);
        removeButton.Click += (_, _) => remove(row);
        return row;
    }

    private static Color ParseColor(string value)
    {
        return Color.FromArgb(
            255,
            byte.Parse(
                value.AsSpan(1, 2),
                System.Globalization.NumberStyles.HexNumber),
            byte.Parse(
                value.AsSpan(3, 2),
                System.Globalization.NumberStyles.HexNumber),
            byte.Parse(
                value.AsSpan(5, 2),
                System.Globalization.NumberStyles.HexNumber));
    }

    private static string ToHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private sealed record TagEditorRow(
        Grid Root,
        TextBox NameBox,
        ColorPicker ColorPicker);
}
