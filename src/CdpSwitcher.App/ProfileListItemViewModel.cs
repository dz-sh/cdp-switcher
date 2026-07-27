using System.ComponentModel;
using System.Runtime.CompilerServices;
using CdpSwitcher.Core.Profiles;

namespace CdpSwitcher.App;

public sealed class ProfileListItemViewModel : INotifyPropertyChanged
{
    private BrowserProfile _profile;
    private string _relationshipLabel = string.Empty;

    public ProfileListItemViewModel(BrowserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profile = profile;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BrowserProfile Profile => _profile;

    public Guid Id => _profile.Id;

    public string Name => _profile.Name;

    public IReadOnlyList<ProfileTag> Tags => _profile.Tags;

    public string RelationshipLabel => _relationshipLabel;

    public void UpdateProfile(BrowserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Id != Id)
        {
            throw new ArgumentException(
                "The profile identifier cannot change.",
                nameof(profile));
        }

        _profile = profile;
        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Tags));
    }

    public void SetActive(bool isActive)
    {
        var next = isActive ? "Active" : string.Empty;
        if (string.Equals(
                next,
                _relationshipLabel,
                StringComparison.Ordinal))
        {
            return;
        }

        _relationshipLabel = next;
        OnPropertyChanged(nameof(RelationshipLabel));
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
