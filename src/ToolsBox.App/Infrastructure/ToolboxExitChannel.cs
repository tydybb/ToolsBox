using System.Diagnostics;
using System.IO;

namespace ToolsBox.App.Infrastructure;

/// <summary>Current-user/current-session exit signal shared by normal and elevated roles.</summary>
public sealed class ToolboxExitChannel
{
    private readonly string _path;
    private readonly string? _initial;
    public ToolboxExitChannel(string? root=null,int? session=null)
    {
        root??=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ToolsBox");
        using var process=Process.GetCurrentProcess();
        _path=Path.Combine(root,$"exit-session-{session??process.SessionId}.txt");
        _initial=Read();
    }
    private string? Read()
    {
        try
        {
            using var file=new FileStream(_path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            if(file.Length>64)return null;
            using var reader=new StreamReader(file);
            string value=reader.ReadToEnd();return Guid.TryParseExact(value,"N",out _)?value:null;
        }
        catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}
    }
    public bool ShouldExit()=>Read() is { } current && current!=_initial;
    public void RequestExit()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary=_path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,Guid.NewGuid().ToString("N"));File.Move(temporary,_path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
