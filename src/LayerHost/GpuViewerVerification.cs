using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class GpuViewerVerification
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var before = Native.GetForegroundWindow(); int[] pids = []; object? evidence = null; string? error = null; bool passed = false;
        ApplicationConfiguration.Initialize(); using var form = new GpuBrowserForm(qa: true);
        form.StartupCompleted += async failure =>
        {
            try
            {
                if (failure != null) throw failure;
                var session = form.CurrentSession!;
                Button Button(string label) => form.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(b => b.Text == label);
                async Task Click(string label) { Button(label).PerformClick(); await form.WaitForCommands(); }
                JsonElement State() => session.Inspect("({text:editor.value,...observations})");
                await Click("Automation: type"); var automated = State();
                await Click("Automation: change color"); var changed = State();
                await Click("Request attention"); string? attention = session.AttentionReason;
                await Click("Human control"); bool rejectedAutomation = false;
                try { session.SendKeyboardInput("REJECT_ME"); } catch (InvalidOperationException) { rejectedAutomation = true; }
                session.ClickElement("editor", ControlOwner.Human);
                session.SendKey("a", "KeyA", 0x41, 2, ControlOwner.Human);
                // Deliver text through the actual preview's KeyPress handler without physical focus.
                foreach (char c in "HUMAN_GPU") { Native.Message(form.InputHandle, Native.WM_CHAR, c, 1); await form.WaitForCommands(); }
                var human = State(); await Click("Refresh");
                using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height)); image.Save(Path.Combine(output, "gpu-viewer.png")); }
                await Click("Return to automation"); var prior = State();
                Native.Message(form.InputHandle, Native.WM_CHAR, 'X', 1); await form.WaitForCommands();
                bool rejectedHuman = State().GetProperty("text").GetString() == prior.GetProperty("text").GetString();
                await Click("Automation: type"); var resumed = State();
                int pid = session.ApplicationProcessId; pids = session.GetProcessIds();
                passed = automated.GetProperty("text").GetString() == "Automation on the GPU page" && changed.GetProperty("changes").GetInt32() == 1 && human.GetProperty("text").GetString() == "HUMAN_GPU" && rejectedAutomation && rejectedHuman && resumed.GetProperty("text").GetString()!.Length > human.GetProperty("text").GetString()!.Length && CompatibilityVerification.Alive(pid);
                evidence = new { automated, changed, human, resumed, attention, rejectedAutomation, rejectedHuman, sameApplicationPid = pid, session.Renderer };
            }
            catch (Exception e) { error = e.ToString(); if (form.CurrentSession != null) pids = form.CurrentSession.GetProcessIds(); }
            finally { form.Close(); }
        };
        Application.Run(form);
        var watch = Stopwatch.StartNew(); while (pids.Any(CompatibilityVerification.Alive) && watch.ElapsedMilliseconds < 5000) Thread.Sleep(50);
        bool cleanup = pids.All(pid => !CompatibilityVerification.Alive(pid));
        var report = new { passed, cleanup, evidence, error, foregroundEquality = Native.GetForegroundWindow() == before, note = "Actual GPU form button handlers exercised through PerformClick; preview KeyPress through targeted messages. Controller transparent/noactivate for QA on Default; no hardware input or preview MouseDown focus request. Human-origin typing and arbitrary Chrome UI interaction require manual acceptance." };
        File.WriteAllText(Path.Combine(output, "gpu-viewer.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(new { output, passed, cleanup, error, report.foregroundEquality }));
        return passed && cleanup ? 0 : 1;
    }
}
