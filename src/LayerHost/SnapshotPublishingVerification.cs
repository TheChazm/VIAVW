using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class SnapshotPublishingVerification
{
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output); var checks = new List<object>(); bool passed = true; string? failure = null; string file = Path.Combine(output, "state.json");
        void Check(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        try
        {
            SnapshotFiles.ReplaceTextAsync(file, "{\"value\":0}").GetAwaiter().GetResult();
            using (var reader = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var pending = SnapshotFiles.ReplaceTextAsync(file, "{\"value\":1}"); Thread.Sleep(45); Check("held reader postpones replacement", !pending.IsCompleted);
                reader.Dispose(); pending.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            }
            Check("replacement completes after temporary sharing conflict", JsonDocument.Parse(SnapshotFiles.ReadText(file)).RootElement.GetProperty("value").GetInt32() == 1);
            using(var reader=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            {
                var pending=SnapshotFiles.ReplaceTextAsync(file,"{\"value\":1}",maxRetries:50);Thread.Sleep(180);
                Check("read-write reader without delete sharing postpones metadata replacement",!pending.IsCompleted);
                reader.Dispose();pending.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            }
            Check("metadata replacement survives a longer ordinary-reader lock",JsonDocument.Parse(SnapshotFiles.ReadText(file)).RootElement.GetProperty("value").GetInt32()==1);
            using (var reader = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var clock = Stopwatch.StartNew(); Exception? rejected = null;
                try { SnapshotFiles.ReplaceTextAsync(file, "{\"value\":2}").GetAwaiter().GetResult(); } catch (Exception e) when(e is IOException or UnauthorizedAccessException) { rejected = e; }
                Check("persistent sharing failure is bounded and preserves original", rejected != null && clock.ElapsedMilliseconds < 1000 && JsonDocument.Parse(SnapshotFiles.ReadText(file)).RootElement.GetProperty("value").GetInt32() == 1, new { ms = clock.Elapsed.TotalMilliseconds, error = rejected?.Message, hresult = rejected?.HResult });
            }
            using var stop = new CancellationTokenSource(); int reads = 0;
            var observing = Task.Run(() => { while (!stop.IsCancellationRequested) { using var document = JsonDocument.Parse(SnapshotFiles.ReadText(file)); _ = document.RootElement.GetProperty("value").GetInt32(); Interlocked.Increment(ref reads); } });
            try { for (int i = 3; i < 23; i++) SnapshotFiles.ReplaceTextAsync(file, JsonSerializer.Serialize(new { value = i })).GetAwaiter().GetResult(); }
            finally { stop.Cancel(); observing.GetAwaiter().GetResult(); }
            Check("concurrent shared readers see complete replaced documents", reads > 0 && JsonDocument.Parse(SnapshotFiles.ReadText(file)).RootElement.GetProperty("value").GetInt32() == 22, new { reads, replacements = 20 });
            string pixels = Path.Combine(output, "pixels.png");
            byte[] Png(Color color) { using var image = new Bitmap(17, 11); using (var drawing = Graphics.FromImage(image)) drawing.Clear(color); using var stream = new MemoryStream(); image.Save(stream, System.Drawing.Imaging.ImageFormat.Png); return stream.ToArray(); }
            byte[] red = Png(Color.Red), blue = Png(Color.Blue); SnapshotFiles.ReplaceAsync(pixels, red).GetAwaiter().GetResult();
            using (var pixelStop = new CancellationTokenSource())
            {
                int decoded = 0; var decoding = Task.Run(() => { while(!pixelStop.IsCancellationRequested) { using var image = SnapshotFiles.ReadBitmap(pixels); int color = image.GetPixel(8, 5).ToArgb(); if(image.Width != 17 || image.Height != 11 || color != Color.Red.ToArgb() && color != Color.Blue.ToArgb()) throw new InvalidDataException("Incomplete snapshot image."); Interlocked.Increment(ref decoded); } });
                try { for(int i = 0; i < 20; i++) SnapshotFiles.ReplaceAsync(pixels, i % 2 == 0 ? blue : red).GetAwaiter().GetResult(); }
                finally { pixelStop.Cancel(); decoding.GetAwaiter().GetResult(); }
                Check("concurrent image readers decode complete snapshots", decoded > 0, new { decoded, replacements = 20 });
            }
            bool missingRejected = false; var bad = Stopwatch.StartNew();
            try { SnapshotFiles.ReplaceTextAsync(Path.Combine(output, "missing", "state.json"), "{}").GetAwaiter().GetResult(); } catch (DirectoryNotFoundException) { missingRejected = true; }
            Check("unrelated filesystem error propagates without retry loop", missingRejected && bad.ElapsedMilliseconds < 500, new { ms = bad.Elapsed.TotalMilliseconds });
        }
        catch (Exception e) { passed = false; failure = e.ToString(); File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        File.WriteAllText(Path.Combine(output, "snapshot-publishing.json"), JsonSerializer.Serialize(new { passed, failure, checks, note = "Only fresh project-owned test files and normal report replacement. Deliberate read locks; no ACL/security changes, browser-profile access or application input." }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
}
