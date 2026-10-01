namespace ObjeX.Web.Helpers;

/// <summary>
/// The schedule part of the job dialog. A preset is shown and saved in the browser's zone; a custom cron keeps the
/// zone it is read in. Until the schedule itself is edited, the job's own cron and zone are saved, so changing only
/// Enabled or the setting never moves the job.
/// </summary>
public sealed class JobScheduleDraft
{
    private readonly string _jobCron;
    private readonly string _jobZone;
    private readonly string _browserZone;

    /// <param name="nextLocal">The job's next run in the browser's zone.</param>
    public JobScheduleDraft(string jobCron, string jobZone, DateTime? nextLocal, string browserZone)
    {
        _jobCron = jobCron;
        _jobZone = jobZone;
        _browserZone = browserZone;
        // In the same zone the cron itself has day and time; the next run can sit an hour later on a spring-forward day.
        Preset = CronPreset.Read(jobCron, jobZone == browserZone ? null : nextLocal);
        CustomCron = jobCron;
        CustomZone = jobZone;
    }

    public JobPreset Preset { get; private set; }

    public string CustomCron { get; private set; }

    /// <summary>The zone the custom cron is read in.</summary>
    public string CustomZone { get; private set; }

    public bool IsEdited { get; private set; }

    public string Cron => !IsEdited ? _jobCron : Preset.Frequency == JobFrequency.Custom ? CustomCron : CronPreset.ToCron(Preset);

    public string Zone => !IsEdited ? _jobZone : Preset.Frequency == JobFrequency.Custom ? CustomZone : _browserZone;

    /// <summary>A custom cron started from a preset is that preset in the browser's zone.</summary>
    public void SetFrequency(JobFrequency frequency)
    {
        if (frequency == JobFrequency.Custom && Preset.Frequency != JobFrequency.Custom)
        {
            CustomCron = CronPreset.ToCron(Preset);
            CustomZone = _browserZone;
        }
        Edit(Preset with { Frequency = frequency });
    }

    public void SetDay(DayOfWeek day) => Edit(Preset with { Day = day });

    public void SetTime(TimeOnly time) => Edit(Preset with { Time = time });

    public void SetCron(string cron)
    {
        CustomCron = cron;
        IsEdited = true;
    }

    private void Edit(JobPreset preset)
    {
        Preset = preset;
        IsEdited = true;
    }
}
