using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NHSE.WinForms;

internal static partial class WorkspaceImageLibrary
{
    internal sealed record Progress(string Phase, long Received = 0, long? Total = null, int Files = 0, int FileCount = 0);
    internal const long MaxDownload = 1024L * 1024 * 1024;
    private const long MaxExpanded = 3 * MaxDownload;
    [GeneratedRegex(@"^[0-9]{5}_[0-9]+\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpriteName();
    public static int Count(string destination) => Directory.Exists(destination) ? Directory.EnumerateFiles(destination).Count(path => SpriteName().IsMatch(Path.GetFileName(path))) : 0;
    public static async Task<long?> SizeAsync(HttpClient client, Uri source, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, source);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented) return null;
        response.EnsureSuccessStatusCode(); return response.Content.Headers.ContentLength;
    }
    public static async Task<int> InstallAsync(HttpClient client, Uri source, string destination, Action<Progress> progress, CancellationToken token)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(destination)) ?? throw new ArgumentException("invalidRequest");
        Directory.CreateDirectory(parent);
        var id = Guid.NewGuid().ToString("N");
        var archive = Path.Combine(parent, ".nhse-images-" + id + ".zip");
        var stage = Path.Combine(parent, ".nhse-images-" + id);
        var backup = Path.Combine(parent, ".nhse-images-old-" + id);
        var movedOld = false;
        try
        {
            using var response = await client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            if (total > MaxDownload) throw new InvalidDataException("imagesTooLarge");
            long received = 0;
            var clock = Stopwatch.StartNew();
            progress(new("downloading", 0, total));
            await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536]; int read;
                while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    received += read; if (received > MaxDownload) throw new InvalidDataException("imagesTooLarge");
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    if (clock.ElapsedMilliseconds >= 100) { progress(new("downloading", received, total)); clock.Restart(); }
                }
            }
            if (total is { } expected && expected != received) throw new InvalidDataException("imagesInvalidPackage");
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(stage);
            int installed;
            using (var zip = ZipFile.OpenRead(archive))
            {
                if (zip.Entries.Count > 250000) throw new InvalidDataException("imagesTooLarge");
                var entries = new List<ZipArchiveEntry>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long expanded = 0;
                foreach (var entry in zip.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    var path = entry.FullName.Replace('\\', '/');
                    if (path.StartsWith('/') || path.Contains(':') || path.Split('/').Contains("..")) throw new InvalidDataException("imagesInvalidPackage");
                    var name = path.Split('/')[^1];
                    if (!SpriteName().IsMatch(name)) continue;
                    if (!names.Add(name)) throw new InvalidDataException("imagesInvalidPackage");
                    expanded += entry.Length;
                    if (entry.Length > 16 * 1024 * 1024 || expanded > MaxExpanded) throw new InvalidDataException("imagesTooLarge");
                    entries.Add(entry);
                }
                if (entries.Count == 0) throw new InvalidDataException("imagesInvalidPackage");
                installed = 0;
                progress(new("extracting", received, total, 0, entries.Count)); clock.Restart();
                foreach (var entry in entries)
                {
                    token.ThrowIfCancellationRequested();
                    var name = entry.FullName.Replace('\\', '/').Split('/')[^1];
                    await using (var input = entry.Open())
                    await using (var output = new FileStream(Path.Combine(stage, name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                    {
                        var buffer = new byte[65536]; long copied = 0; int read;
                        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                        {
                            copied += read;
                            if (copied > entry.Length || copied > 16 * 1024 * 1024) throw new InvalidDataException("imagesInvalidPackage");
                            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                        }
                        if (copied != entry.Length) throw new InvalidDataException("imagesInvalidPackage");
                    }
                    // Reject a wrong/corrupt asset before replacing the installed library.
                    await using (var image = File.OpenRead(Path.Combine(stage, name)))
                    {
                        var header = new byte[24]; await image.ReadExactlyAsync(header, token).ConfigureAwait(false);
                        if (image.Length < 57 || BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8)) != 13 || !header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                            || !header.AsSpan(12, 4).SequenceEqual("IHDR"u8)
                            || BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(16)) is 0 or > 8192
                            || BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(20)) is 0 or > 8192) throw new InvalidDataException("imagesInvalidPackage");
                        var end = new byte[12]; image.Seek(-12, SeekOrigin.End); await image.ReadExactlyAsync(end, token).ConfigureAwait(false);
                        if (!end.AsSpan().SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 })) throw new InvalidDataException("imagesInvalidPackage");
                    }
                    installed++;
                    if (clock.ElapsedMilliseconds >= 100 || installed == entries.Count) { progress(new("extracting", received, total, installed, entries.Count)); clock.Restart(); }
                }
            }
            token.ThrowIfCancellationRequested(); progress(new("installing", received, total, installed, installed));
            if (Directory.Exists(destination)) { Directory.Move(destination, backup); movedOld = true; }
            try { Directory.Move(stage, destination); }
            catch { if (movedOld) { Directory.Move(backup, destination); movedOld = false; } throw; }
            if (movedOld)
            {
                try { Directory.Delete(backup, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* A locked backup may be removed later; the new library is complete. */ }
            }
            progress(new("complete", received, total, installed, installed)); return installed;
        }
        finally
        {
            if (File.Exists(archive)) File.Delete(archive);
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
        }
    }
}
