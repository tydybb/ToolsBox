namespace ToolsBox.App.Infrastructure;

/// <summary>A per-user/session main-window lease, independent of helper process roles.</summary>
public sealed class MainInstanceGate : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private MainInstanceGate(Mutex mutex,EventWaitHandle activate){_mutex=mutex;_activate=activate;}
    public static MainInstanceGate? Acquire(string name)
    {
        var activate=new EventWaitHandle(false,EventResetMode.AutoReset,name+".Activate");
        try
        {
            var mutex=new Mutex(false,name,out bool created);
            if(created)return new MainInstanceGate(mutex,activate);
            mutex.Dispose();activate.Set();activate.Dispose();return null;
        }
        catch{activate.Dispose();throw;}
    }
    public bool ConsumeActivation()=>_activate.WaitOne(0);
    public void Dispose(){_mutex.Dispose();_activate.Dispose();}
}
