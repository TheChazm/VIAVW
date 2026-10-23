global using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace ViavwDesktop;

internal static class Program
{
    internal static readonly string ReleaseRoot=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../")));
    internal static readonly string InstallRoot=Directory.GetParent(ReleaseRoot)!.Parent!.FullName;
    internal static readonly string Tool=File.Exists(Path.Combine(InstallRoot,"viavw.exe"))?Path.Combine(InstallRoot,"viavw.exe"):Path.Combine(ReleaseRoot,"viavw.exe");
    internal static readonly string Host=Path.Combine(AppContext.BaseDirectory,"LayerHost.exe");
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            ApplicationConfiguration.Initialize();
            if(args.Length>0&&args[0]=="--write-connection")
            {
                WriteConnection();Console.WriteLine("VIAVW connection file is ready.");return 0;
            }
            if(args.Length>0&&args[0]=="--check-idle")
            {
                var running=RunningInstalledProcesses();
                if(running.Length==0)return 0;
                string message="VIAVW is still running. Close its manager and application layers before changing or removing this installation. No application was stopped.";
                if(!args.Contains("--quiet"))MessageBox.Show(message,"VIAVW",MessageBoxButtons.OK,MessageBoxIcon.Information);
                Console.Error.WriteLine(message);return 1;
            }
            if(args.Length>0&&args[0]=="--self-test")
            {
                using var manager=new ManagerForm();
                // No UI message pump is running in this diagnostic mode.
                var info=Task.Run(()=>Call("viavw_info",new())).GetAwaiter().GetResult();
                var list=Task.Run(()=>Call("viavw_list",new())).GetAwaiter().GetResult();
                Console.WriteLine(new JsonObject{["passed"]=File.Exists(Tool)&&manager.ActionCount==8&&info["name"]!.GetValue<string>()=="VIAVW",["buttons"]=manager.ActionCount,["tool"]=Tool,["layers"]=list["layers"]!.DeepClone(),["connectionFile"]=Path.Combine(InstallRoot,"mcp-config.json")}.ToJsonString());return 0;
            }
            Application.Run(new ManagerForm());return 0;
        }
        catch(Exception error)
        {
            if(args.Length>0)Console.Error.WriteLine(error.Message);
            else MessageBox.Show(error.Message,"VIAVW",MessageBoxButtons.OK,MessageBoxIcon.Error);
            return 1;
        }
    }
    static int[] RunningInstalledProcesses()
    {
        var running=new List<int>();
        foreach(var process in Process.GetProcesses())
            using(process)
            {
                if(process.Id==Environment.ProcessId)continue;
                try{if(process.MainModule?.FileName is {} file&&Path.GetFullPath(file).StartsWith(InstallRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))running.Add(process.Id);}
                catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
            }
        return running.ToArray();
    }
    internal static void WriteConnection()
    {
        Directory.CreateDirectory(InstallRoot);
        File.WriteAllText(Path.Combine(InstallRoot,"mcp-config.json"),new JsonObject{["mcpServers"]=new JsonObject{["viavw"]=new JsonObject{["command"]=Tool,["args"]=new JsonArray("mcp")}}}.ToJsonString(new(){WriteIndented=true}),new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(InstallRoot,"installed.json"),new JsonObject{["schema"]=1,["executable"]=Tool,["manager"]=Environment.ProcessPath,["releaseRoot"]=ReleaseRoot,["installedUtc"]=DateTime.UtcNow,["dataPolicy"]="Application documents and layer state are retained on uninstall."}.ToJsonString(new(){WriteIndented=true}),new UTF8Encoding(false));
    }
    internal static async Task<(int Exit,string Output,string Error)> Run(string executable,IEnumerable<string> arguments,int timeoutSeconds=100)
    {
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=ReleaseRoot};
        foreach(string argument in arguments)start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new IOException("Could not open VIAVW.");
        Task<string> output=process.StandardOutput.ReadToEndAsync(),error=process.StandardError.ReadToEndAsync();
        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));}
        catch(TimeoutException)
        {
            // Stop this one CLI adapter, never its independently owned apps.
            try{process.Kill();}catch(InvalidOperationException){}
            throw new TimeoutException("The tool response is unknown. The application is retained. Refresh and inspect its state before making another decision.");
        }
        return(process.ExitCode,await output,await error);
    }
    internal static async Task<JsonObject> Call(string tool,JsonObject arguments)
    {
        string directory=Path.Combine(ReleaseRoot,"artifacts","manager-requests");Directory.CreateDirectory(directory);
        string request=Path.Combine(directory,Guid.NewGuid().ToString("N")+".json");
        await File.WriteAllTextAsync(request,arguments.ToJsonString(),new UTF8Encoding(false));
        try
        {
            var result=await Run(Tool,["call",tool,"--file",request]);
            JsonObject response;
            try{response=JsonNode.Parse(result.Output)!.AsObject();}catch{throw new IOException("VIAVW did not return a usable response. "+result.Error);}
            if(result.Exit!=0||response["ok"]?.GetValue<bool>()!=true)throw new IOException(response["error"]?["message"]?.GetValue<string>()??"The application needs inspection.");
            return response["result"]!.DeepClone().AsObject();
        }
        finally{File.Delete(request);}
    }
}
