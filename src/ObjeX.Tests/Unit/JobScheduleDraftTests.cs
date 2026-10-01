using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Unit;

public class JobScheduleDraftTests
{
    private const string Zurich = "Europe/Zurich";

    [Fact]
    public void ACustomCron_KeepsItsZone_WhenTheScheduleIsNotEdited()
    {
        var draft = new JobScheduleDraft("0 2 * * 1-5", "America/New_York", new DateTime(2026, 10, 5, 8, 0, 0), Zurich);

        Assert.Equal(("0 2 * * 1-5", "America/New_York"), (draft.Cron, draft.Zone));
    }

    [Fact]
    public void ACustomCron_KeepsItsZone_WhenItsTextIsEdited()
    {
        var draft = new JobScheduleDraft("0 2 * * 1-5", "America/New_York", new DateTime(2026, 10, 5, 8, 0, 0), Zurich);

        draft.SetCron("0 3 * * 1-5");

        Assert.Equal(("0 3 * * 1-5", "America/New_York"), (draft.Cron, draft.Zone));
    }

    [Fact]
    public void TheDefault_StaysAsItIs_WhenOnlyTheSettingChanges()
    {
        var draft = new JobScheduleDraft("0 3 * * 0", "UTC", new DateTime(2026, 10, 4, 5, 0, 0), Zurich);

        Assert.Equal(new JobPreset(JobFrequency.Weekly, DayOfWeek.Sunday, new TimeOnly(5, 0)), draft.Preset);
        Assert.Equal(("0 3 * * 0", "UTC"), (draft.Cron, draft.Zone));
    }

    [Fact]
    public void AnEditedPreset_IsSavedInTheBrowserZone()
    {
        var draft = new JobScheduleDraft("0 3 * * 0", "UTC", new DateTime(2026, 10, 4, 5, 0, 0), Zurich);

        draft.SetTime(new TimeOnly(6, 0));

        Assert.Equal(("0 6 * * 0", Zurich), (draft.Cron, draft.Zone));
    }

    [Fact]
    public void SwitchingAPresetToCustom_StartsFromTheLocalPresetInTheBrowserZone()
    {
        var draft = new JobScheduleDraft("0 3 * * 0", "UTC", new DateTime(2026, 10, 4, 5, 0, 0), Zurich);

        draft.SetFrequency(JobFrequency.Custom);

        Assert.Equal(("0 5 * * 0", Zurich), (draft.Cron, draft.Zone));
    }

    [Fact]
    public void InTheSameZone_DayAndTimeComeFromTheCron_NotFromANextRunMovedBySpringForward()
    {
        var draft = new JobScheduleDraft("0 2 * * *", Zurich, new DateTime(2027, 3, 28, 3, 0, 0), Zurich);

        Assert.Equal(new TimeOnly(2, 0), draft.Preset.Time);
    }
}
