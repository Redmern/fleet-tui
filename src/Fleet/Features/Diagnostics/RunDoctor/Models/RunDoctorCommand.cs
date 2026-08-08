namespace Fleet.Features.Diagnostics.RunDoctor.Models;

public sealed record RunDoctorCommand(string ChosenDriver, string? UnsupportedReason);
