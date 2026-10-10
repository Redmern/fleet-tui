using System.Diagnostics;

namespace Fleet.Platform.Sync;

public interface ISyncProcessRunner
{
    Process Start(ProcessStartInfo start);
}
