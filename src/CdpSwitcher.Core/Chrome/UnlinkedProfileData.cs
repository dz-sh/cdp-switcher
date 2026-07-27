namespace CdpSwitcher.Core.Chrome;

public sealed record UnlinkedProfileData(
    Guid Id,
    DateTimeOffset LastModifiedAt);
