using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NHSE.WinForms;

internal static class ImageLibraryChecks
{
    private static void Check(bool condition, string text) { if (!condition) throw new Exception(text); Console.WriteLine("PASS " + text); }
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l1sAAAAASUVORK5CYII=");
    private static readonly Uri Source = new("https://example.test/images.zip");
    private static byte[] Package(params (string Name, byte[] Data)[] entries)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var entry in entries) { using var stream = zip.CreateEntry(entry.Name).Open(); stream.Write(entry.Data); }
        return output.ToArray();
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request));
    }
    private sealed class NonSeekable(byte[] bytes) : Stream
    {
        private readonly MemoryStream Inner = new(bytes);
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) Inner.Dispose(); base.Dispose(disposing); }
    }
    public static void Run() => RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "nhse-image-checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            using (var client = new HttpClient(new Handler(request => { Check(request.Method == HttpMethod.Head, "image size lookup uses HEAD without downloading the package body");
                var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) }; result.Content.Headers.ContentLength = 123456; return result; })))
                Check(await WorkspaceImageLibrary.SizeAsync(client, Source, CancellationToken.None) == 123456, "image size metadata preserves the server's byte length");
            using (var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed))))
                Check(await WorkspaceImageLibrary.SizeAsync(client, Source, CancellationToken.None) is null, "unsupported HEAD responses leave download size unknown without fetching the archive");
            string Existing()
            {
                var path = Path.Combine(root, Guid.NewGuid().ToString("N"), "img"); Directory.CreateDirectory(path); File.WriteAllBytes(Path.Combine(path, "00001_0.png"), Png); return path;
            }
            var good = Package(("raw/00002_0.png", Png), ("raw/00003_1.png", Png), ("README.txt", new byte[] { 1, 2 }));
            var destination = Existing(); var progress = new List<WorkspaceImageLibrary.Progress>();
            using (var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekable(good)) })))
                Check(await WorkspaceImageLibrary.InstallAsync(client, Source, destination, progress.Add, CancellationToken.None) == 2,
                    "image packages install with unknown content lengths and normalized nested sprite paths");
            Check(WorkspaceImageLibrary.Count(destination) == 2 && !File.Exists(Path.Combine(destination, "00001_0.png"))
                && File.ReadAllBytes(Path.Combine(destination, "00002_0.png")).SequenceEqual(Png), "successful installation replaces the library only with complete staged PNG files");
            Check(progress.First().Phase == "downloading" && progress.First().Total is null && progress.Last().Phase == "complete"
                && progress.Any(p => p.Phase == "extracting" && p.Files == 2), "download, extraction and installation progress report real byte and file counts");
            Check(Directory.GetFileSystemEntries(Path.GetDirectoryName(destination)!).Length == 1, "successful image installation removes its archive, staging directory and backup");
            async Task Reject(byte[] content, string text, long? length = null, HttpStatusCode status = HttpStatusCode.OK)
            {
                var target = Existing(); var failed = false;
                using var client = new HttpClient(new Handler(_ => { var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(content) }; if (length is not null) response.Content.Headers.ContentLength = length; return response; }));
                try { await WorkspaceImageLibrary.InstallAsync(client, Source, target, _ => { }, CancellationToken.None); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException) { failed = true; }
                Check(failed && WorkspaceImageLibrary.Count(target) == 1 && File.ReadAllBytes(Path.Combine(target, "00001_0.png")).SequenceEqual(Png)
                    && Directory.GetFileSystemEntries(Path.GetDirectoryName(target)!).Length == 1, text);
            }
            await Reject([1, 2, 3], "invalid image archives preserve the previous library and clean temporary files");
            await Reject(Package(("../00002_0.png", Png)), "escaping archive paths reject before touching the installed library");
            await Reject(Package(("raw/00002_0.png", Png), ("00002_0.png", Png)), "duplicate sprite names cannot silently overwrite another archive entry");
            await Reject(Package(("README.txt", [1, 2])), "archives without supported sprite names cannot replace a working library");
            await Reject(Package(("00002_0.png", new byte[24])), "wrong image headers reject while the old library remains available");
            await Reject(Package(("00002_0.png", Png[..24])), "truncated PNG payloads cannot replace installed sprites");
            await Reject(good, "truncated HTTP transfers reject before installation", good.Length + 10);
            await Reject(good, "oversized image downloads reject from response headers", WorkspaceImageLibrary.MaxDownload + 1);
            await Reject(good, "HTTP download failures preserve installed images", status: HttpStatusCode.BadGateway);
            var cancelTarget = Existing(); using var cancellation = new CancellationTokenSource(); var cancelled = false;
            using (var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(good) })))
                try { await WorkspaceImageLibrary.InstallAsync(client, Source, cancelTarget, p => { if (p.Phase == "extracting") cancellation.Cancel(); }, cancellation.Token); }
                catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && WorkspaceImageLibrary.Count(cancelTarget) == 1 && Directory.GetFileSystemEntries(Path.GetDirectoryName(cancelTarget)!).Length == 1,
                "cancelling an in-progress installation cleans staging and leaves the old images intact");
            var rollbackTarget = Existing(); var rolledBack = false;
            using (var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(good) })))
                try { await WorkspaceImageLibrary.InstallAsync(client, Source, rollbackTarget, p => {
                    if (p.Phase == "installing") Directory.Delete(Directory.GetDirectories(Path.GetDirectoryName(rollbackTarget)!, ".nhse-images-*").Single(), true);
                }, CancellationToken.None); }
                catch (IOException) { rolledBack = true; }
            Check(rolledBack && WorkspaceImageLibrary.Count(rollbackTarget) == 1 && Directory.GetFileSystemEntries(Path.GetDirectoryName(rollbackTarget)!).Length == 1,
                "a failed final directory swap restores the previous image library");
        }
        finally { Directory.Delete(root, true); }
    }
}
