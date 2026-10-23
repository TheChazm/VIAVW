using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayerHost;

internal static class SharedPreviewVerification
{
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output);
        var checks = new Dictionary<string, bool>(); var names = new List<string>(); string? failure = null;
        void Check(string name, bool pass) { checks[name] = pass; if (!pass) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); }
        byte[] Pixels(Bitmap image)
        {
            var bits = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { var bytes = new byte[image.Width * image.Height * 4]; Marshal.Copy(bits.Scan0, bytes, 0, bytes.Length); return SHA256.HashData(bytes); }
            finally { image.UnlockBits(bits); }
        }
        void DrawPattern(Bitmap image, int width, int height, int seed)
        {
            using var g = Graphics.FromImage(image); g.Clear(Color.FromArgb(255, seed % 256, 71, 193));
            using var brush = new SolidBrush(Color.FromArgb(255, 31, seed * 7 % 256, 91)); g.FillRectangle(brush, 1, 2, width / 2, height / 2);
            image.SetPixel(width - 1, height - 1, Color.FromArgb(113, 200, 131, 19));
        }
        Bitmap Pattern(int width,int height,int seed){var image=new Bitmap(width,height,PixelFormat.Format32bppArgb);DrawPattern(image,width,height,seed);return image;}
        bool Gone(string name) { try { using var map = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read); return false; } catch (FileNotFoundException) { return true; } }
        try
        {
            using (var reader = new SharedPreviewReader())
            using (var writer = new SharedPreviewWriter())
            using (var secondReader = new SharedPreviewReader())
            using (var secondWriter = new SharedPreviewWriter())
            {
                string previous = "";
                foreach (var (w, h) in new[] { (127, 93), (127, 93), (41, 27), (401, 211), (39, 19) })
                {
                    using var image = Pattern(w, h, w); var frame = writer.Write(reader.Channel, image, 0, [], Stopwatch.StartNew()); names.Add(frame.Name);
                    using var copy = reader.Read(frame);
                    Check($"exact BGRA and alpha {w}x{h} step {names.Count}", Pixels(image).SequenceEqual(Pixels(copy)));
                    if (names.Count == 2 || names.Count == 3 || names.Count == 5) Check("reuse mapping step " + names.Count, previous == frame.Name);
                    if (names.Count == 4) Check("growth releases old mapping after reader switches", frame.Name != previous && Gone(previous));
                    previous = frame.Name;
                }
                using var independent = Pattern(151, 97, 15); var other = secondWriter.Write(secondReader.Channel, independent, 0, [], Stopwatch.StartNew()); names.Add(other.Name);
                using var otherCopy = secondReader.Read(other); Check("independent layer channel pixels", Pixels(independent).SequenceEqual(Pixels(otherCopy)));
                bool rejected = false; try { using var invalid = reader.Read(other); } catch (InvalidDataException) { rejected = true; }
                Check("reject another channel descriptor", rejected);
                var valid = writer.Write(reader.Channel, independent, 0, [], Stopwatch.StartNew()); names.Add(valid.Name);
                foreach (var invalid in new[] { valid with { Bytes = valid.Bytes - 1 }, valid with { Width = 8193 }, valid with { Height = 0 }, valid with { Sequence = 0 }, valid with { Name = valid.Name + "x" } })
                {
                    rejected = false; try { using var ignored = reader.Read(invalid); } catch (InvalidDataException) { rejected = true; }
                    Check("reject malformed descriptor " + checks.Count, rejected);
                }
                for (int i = 0; i < 50; i++)
                {
                    using var image = Pattern(151, 97, i); var frame = writer.Write(reader.Channel, image, 0, [], Stopwatch.StartNew());
                    using var copy = reader.Read(frame); if (!Pixels(image).SequenceEqual(Pixels(copy))) throw new InvalidDataException("Reused mapping returned stale pixels.");
                }
                Check("50 reused captures contain exact fresh pixels", true);
                foreach(var (w,h) in new[]{(127,93),(127,93),(901,701),(73,29)})
                {
                    using var image=Pattern(w,h,w);
                    var direct=writer.Render(reader.Channel,w,h,destination=>{DrawPattern(destination,w,h,w);return 0;},[],Stopwatch.StartNew());names.Add(direct.Name);
                    using var copy=reader.Read(direct);
                    Check("direct mapped render exact BGRA alpha "+w+"x"+h+" step "+checks.Count,Pixels(image).SequenceEqual(Pixels(copy)));
                }
                bool threw=false;
                try{writer.Render(reader.Channel,151,97,_=>throw new InvalidOperationException("Disposable rendering failure"),[],Stopwatch.StartNew());}catch(InvalidOperationException){threw=true;}
                Check("direct render releases acquired pointer after failure",threw);
                var resumed=writer.Render(reader.Channel,151,97,b=>{using var g=Graphics.FromImage(b);g.Clear(Color.CornflowerBlue);return 0;},[],Stopwatch.StartNew());names.Add(resumed.Name);
                using(var copy=reader.Read(resumed))Check("direct render recovers after failure",copy.GetPixel(10,10).ToArgb()==Color.CornflowerBlue.ToArgb());
                writer.Dispose();
                using var recovered = new SharedPreviewWriter();
                var recovery = recovered.Write(reader.Channel, independent, 0, [], Stopwatch.StartNew()); names.Add(recovery.Name);
                using var recoveredCopy = reader.Read(recovery); Check("new worker mapping preserves reader recovery", Pixels(independent).SequenceEqual(Pixels(recoveredCopy)));
            }
            Check("all owned mappings released", names.Distinct().All(Gone));
        }
        catch (Exception e) { failure = e.ToString(); Console.Error.WriteLine(failure); }
        File.WriteAllText(Path.Combine(output, "shared-preview.json"), JsonSerializer.Serialize(new { passed = failure == null, checks, failure, note = "Transport pixel, resize, isolation, recovery and release checks. Installed VS input/visual workflow is tested separately." }, new JsonSerializerOptions { WriteIndented = true }));
        return failure == null ? 0 : 1;
    }
}
