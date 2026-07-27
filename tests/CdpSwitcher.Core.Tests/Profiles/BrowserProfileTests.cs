using CdpSwitcher.Core.Profiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CdpSwitcher.Core.Tests.Profiles;

[TestClass]
public sealed class BrowserProfileTests
{
    [TestMethod]
    public void Create_trims_the_display_name_and_preserves_tags()
    {
        var profile = BrowserProfile.Create(
            "  Automation  ",
            [
                ProfileTag.Create(" Work ", "#2563eb"),
                ProfileTag.Create("Admin", "#DC2626"),
            ]);

        Assert.AreEqual("Automation", profile.Name);
        Assert.AreEqual(2, profile.Tags.Count);
        Assert.AreEqual("Work", profile.Tags[0].Name);
        Assert.AreEqual("#2563EB", profile.Tags[0].Color);
        Assert.AreEqual("Admin", profile.Tags[1].Name);
    }

    [TestMethod]
    public void Create_rejects_a_blank_display_name()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => BrowserProfile.Create("   "));
    }

    [TestMethod]
    public void Create_rejects_duplicate_tag_names_case_insensitively()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => BrowserProfile.Create(
                "Automation",
                [
                    ProfileTag.Create("Work", "#2563EB"),
                    ProfileTag.Create("work", "#DC2626"),
                ]));
    }

    [TestMethod]
    public void Tag_rejects_an_invalid_color()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => ProfileTag.Create("Work", "blue"));
    }

    [TestMethod]
    public void Restore_preserves_the_stable_identifier()
    {
        var id = Guid.NewGuid();

        var profile = BrowserProfile.Restore(id, "Automation");

        Assert.AreEqual(id, profile.Id);
    }

    [TestMethod]
    public void Edit_preserves_identifier_and_updates_metadata()
    {
        var profile = BrowserProfile.Create(
            "Automation",
            [ProfileTag.Create("Work", "#2563EB")]);

        var edited = profile.Edit(
            "Primary",
            [ProfileTag.Create("Admin", "#DC2626")]);

        Assert.AreEqual(profile.Id, edited.Id);
        Assert.AreEqual("Primary", edited.Name);
        Assert.AreEqual(1, edited.Tags.Count);
        Assert.AreEqual("Admin", edited.Tags[0].Name);
    }
}
