using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Attendance;

public sealed class AttendanceMainIntegration : IDisposable
{
    private readonly Mutex _presence;
    private readonly AttendanceCountdownBridge _bridge;
    public AttendanceMainIntegration(WorkCountdownViewModel countdown)
    {
        _presence=new Mutex(false,AttendanceHost.MainPresenceName);
        var store=new AttendanceStore();_bridge=new AttendanceCountdownBridge(store,countdown,AttendanceCountdownBridge.Confirm);
        try{if(store.LoadOptions().Enabled){using var process=WebResources.WebResourceLauncher.LaunchAttendance();}}
        catch(Exception){ /* Panel reports stale/missing status; do not disable an opted-in feature. */ }
    }
    public void Dispose(){_bridge.Dispose();_presence.Dispose();}
}
