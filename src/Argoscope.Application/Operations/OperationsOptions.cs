namespace Argoscope.Application.Operations;

/// <summary>Provider-independent operational targets. Hosting vendor and
/// region remain explicit operator decisions; these are initial targets,
/// not guarantees, until a host-specific rehearsal demonstrates them.</summary>
public sealed class OperationsOptions
{
    public const string SectionName = "Operations";

    /// <summary>Target monthly API availability (percent). Default 99.5.</summary>
    public double SloAvailabilityPercent { get; set; } = 99.5;

    /// <summary>Recovery-point objective in hours. Default 24.</summary>
    public double RpoHours { get; set; } = 24;

    /// <summary>Recovery-time objective in hours. Default 8.</summary>
    public double RtoHours { get; set; } = 8;

    /// <summary>Encrypted backup retention in days. Default 30.</summary>
    public int BackupRetentionDays { get; set; } = 30;

    /// <summary>Active-data purge window after a tenant deletion request, in days. Default 30.</summary>
    public int ActivePurgeDays { get; set; } = 30;

    /// <summary>Hosting region (operator decision; empty means unselected).</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>Launch requires a named operator sign-off on rehearsal evidence.</summary>
    public bool RequireOperatorSignoff { get; set; } = true;
}
