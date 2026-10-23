using System.Diagnostics;
using System.Text.Json.Nodes;

namespace ViavwDesktop;

internal sealed class ManagerForm:Form
{
    readonly DataGridView grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,RowHeadersVisible=false,BackgroundColor=Color.White,BorderStyle=BorderStyle.None};
    readonly Label status=new(){AutoSize=false,Dock=DockStyle.Bottom,Height=50,Padding=new Padding(16,8,16,8),Text="Ready. Applications keep running when you close this manager."};
    readonly FlowLayoutPanel actions=new(){Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(10),WrapContents=true};
    readonly System.Windows.Forms.Timer timer=new(){Interval=4000};
    bool busy;
    internal int ActionCount=>actions.Controls.Count;
    internal ManagerForm()
    {
        Text="VIAVW — Application layers";Width=1080;Height=590;MinimumSize=new Size(850,440);Font=new Font("Segoe UI",10);StartPosition=FormStartPosition.CenterScreen;
        var heading=new Label{Text="Your applications, separate from your desktop",Dock=DockStyle.Top,Height=62,Padding=new Padding(16,18,16,10),Font=new Font("Segoe UI",15,FontStyle.Bold)};
        grid.Columns.Add("application","Application");grid.Columns.Add("owner","Control");grid.Columns.Add("state","Status");
        Add("New test document",Demo);Add("Launch application…",Launch);Add("Take human control",()=>Selected("viavw_handoff",new(){["reason"]="Human control requested from the VIAVW manager."}));
        Add("Show viewer",()=>Selected("viavw_viewer",new(){["visible"]=true}));Add("Hide viewer",()=>Selected("viavw_viewer",new(){["visible"]=false}));
        Add("Close application",()=>Selected("viavw_close",new()));Add("Refresh",RefreshLayers);Add("Tool connection…",Connection);
        Controls.Add(grid);Controls.Add(status);Controls.Add(actions);Controls.Add(heading);
        grid.CellDoubleClick+=async(_,e)=>{if(e.RowIndex>=0)await Work(()=>Selected("viavw_viewer",new(){["visible"]=true}));};
        Shown+=async(_,_)=>await Work(RefreshLayers);
        timer.Tick+=async(_,_)=>{if(!busy)await Work(RefreshLayers,false);};timer.Start();
        FormClosed+=(_,_)=>timer.Dispose();
    }
    void Add(string text,Func<Task> action)
    {
        var button=new Button{Text=text,AutoSize=true,Padding=new Padding(8,5,8,5),Margin=new Padding(4)};
        button.Click+=async(_,_)=>await Work(action);actions.Controls.Add(button);
    }
    async Task Work(Func<Task> action,bool report=true)
    {
        if(busy)return;busy=true;actions.Enabled=false;
        try{await action();}
        catch(Exception error){status.Text=error.Message;if(report)MessageBox.Show(this,error.Message,"VIAVW",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        finally{busy=false;actions.Enabled=true;}
    }
    async Task RefreshLayers()
    {
        string? selected=grid.SelectedRows.Count>0?grid.SelectedRows[0].Tag as string:null;
        var listed=(await Program.Call("viavw_list",new()))["layers"]!.AsArray();
        var rows=new List<(string Id,string Application,string Owner,string State)>();
        foreach(var entry in listed)
        {
            if(entry is not JsonObject layer)continue;
            string id=layer["layer"]!.GetValue<string>(),state=layer["state"]?.GetValue<string>()??"Needs attention",owner="—";
            bool alive=layer["ownerAlive"]?.GetValue<bool>()??false;
            if(state=="closed")continue;
            if(alive)
                try{var current=(await Program.Call("viavw_status",new(){["layer"]=id}))["data"]!["session"]!;owner=current["owner"]!.GetValue<string>();state=current["state"]!.GetValue<string>();}
                catch{state="Needs inspection";}
            else state="Controller disconnected — application may be retained";
            rows.Add((id,Path.GetFileNameWithoutExtension(layer["executable"]?.GetValue<string>()??"Application"),owner,state));
        }
        if(IsDisposed)return;grid.Rows.Clear();
        foreach(var row in rows){int index=grid.Rows.Add(row.Application,row.Owner,row.State);grid.Rows[index].Tag=row.Id;if(row.Id==selected)grid.Rows[index].Selected=true;}
        status.Text=rows.Count==0?"Ready. Create a disposable test document or launch an application. Your AI can also create layers through the installed tool.":$"{rows.Count} application layer(s). Double-click one to inspect it. Closing this manager preserves the applications.";
    }
    async Task Selected(string operation,JsonObject arguments)
    {
        if(grid.SelectedRows.Count==0)throw new InvalidOperationException("Select an application first.");
        arguments["layer"]=(string)grid.SelectedRows[0].Tag!;
        await Program.Call(operation,arguments);await RefreshLayers();
    }
    async Task Demo()
    {
        string fixture=Path.Combine(Program.ReleaseRoot,"artifacts","human-demo-"+Guid.NewGuid().ToString("N"));
        var preparation=await Program.Run(Program.Host,["--prepare-installed-notepad-gdi-demo",fixture],30);
        if(preparation.Exit!=0)throw new IOException("The disposable Notepad++ demonstration could not be prepared. "+preparation.Error);
        var setup=JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture,"launch.json")))!;
        var created=await Program.Call("viavw_create",new(){["executable"]=setup["Executable"]!.DeepClone(),["arguments"]=setup["Arguments"]!.DeepClone()});
        string id=created["layer"]!.GetValue<string>();
        await Program.Call("viavw_handoff",new(){["layer"]=id,["reason"]="Disposable document ready for Human editing. Choose Return to automation in the viewer when finished."});
        await RefreshLayers();
    }
    async Task Launch()
    {
        using var picker=new OpenFileDialog{Title="Choose an installed Windows application",Filter="Windows applications (*.exe)|*.exe",CheckFileExists=true};
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        using var dialog=new Form{Text="Application launch options",Width=650,Height=340,StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false};
        var prompt=new Label{Text="Enter one launch argument per line. Use this application's independent-instance options so it stays separate from existing windows.",Dock=DockStyle.Top,Height=65,Padding=new Padding(12)};
        var args=new TextBox{Multiline=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical};var ok=new Button{Text="Launch",Dock=DockStyle.Bottom,Height=42,DialogResult=DialogResult.OK};
        dialog.Controls.Add(args);dialog.Controls.Add(ok);dialog.Controls.Add(prompt);dialog.AcceptButton=ok;
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        var created=await Program.Call("viavw_create",new(){["executable"]=picker.FileName,["arguments"]=new JsonArray(args.Lines.Where(a=>a.Length>0).Select(a=>(JsonNode?)JsonValue.Create(a)).ToArray())});
        await Program.Call("viavw_viewer",new(){["layer"]=created["layer"]!.DeepClone(),["visible"]=true});await RefreshLayers();
    }
    Task Connection()
    {
        Program.WriteConnection();
        using var window=new Form{Text="VIAVW tool connection",Width=760,Height=370,StartPosition=FormStartPosition.CenterParent};
        var text=new TextBox{Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Both,WordWrap=false,Text="Installed executable:\r\n"+Program.Tool+"\r\n\r\nMCP arguments: mcp\r\n\r\nConnection file:\r\n"+Path.Combine(Program.InstallRoot,"mcp-config.json")+"\r\n\r\nA tool-capable AI can connect to this executable without embedding VIAVW source code."};
        var folder=new Button{Text="Open installation folder",Dock=DockStyle.Bottom,Height=40};folder.Click+=(_,_)=>Process.Start(new ProcessStartInfo(Program.InstallRoot){UseShellExecute=true});
        window.Controls.Add(text);window.Controls.Add(folder);window.ShowDialog(this);return Task.CompletedTask;
    }
}
