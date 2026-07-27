namespace CdpSwitcher.Core.Chrome;

public sealed class ChromeProfileUseDetector
{
    private const int SharingViolation = 32;

    public bool IsInUse(string profileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);

        var lockFile = Path.Combine(profileDirectory, "lockfile");
        try
        {
            using var stream = new FileStream(
                lockFile,
                FileMode.Open,
                FileAccess.Write,
                FileShare.Read);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException exception)
            when ((exception.HResult & 0xFFFF) == SharingViolation)
        {
            return true;
        }
    }
}
