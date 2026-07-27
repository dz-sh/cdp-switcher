using CdpSwitcher.Core.Profiles;
using CdpSwitcher.Core.Switching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CdpSwitcher.Core.Tests.Switching;

[TestClass]
public sealed class CdpLifecycleStateTests
{
    [TestMethod]
    public void Error_requires_a_failure()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new CdpLifecycleState(
                CdpLifecycleStatus.Error,
                managedProfile: null,
                operationsAvailable: false,
                failure: null));
    }

    [TestMethod]
    public void Non_error_rejects_a_failure()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new CdpLifecycleState(
                CdpLifecycleStatus.Stopped,
                managedProfile: null,
                operationsAvailable: true,
                failure: new InvalidOperationException()));
    }

    [TestMethod]
    public void Active_requires_a_running_profile()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new CdpLifecycleState(
                CdpLifecycleStatus.Active,
                managedProfile: null,
                operationsAvailable: true,
                failure: null));
    }

    [TestMethod]
    public void Stopped_rejects_a_managed_profile()
    {
        var profile = BrowserProfile.Create("Test");

        Assert.ThrowsExactly<ArgumentException>(
            () => new CdpLifecycleState(
                CdpLifecycleStatus.Stopped,
                profile,
                operationsAvailable: true,
                failure: null));
    }

    [TestMethod]
    public void Managed_Chrome_requires_completed_startup_checks()
    {
        var profile = BrowserProfile.Create("Test");

        Assert.ThrowsExactly<ArgumentException>(
            () => new CdpLifecycleState(
                CdpLifecycleStatus.Active,
                profile,
                operationsAvailable: false,
                failure: null));
    }
}
