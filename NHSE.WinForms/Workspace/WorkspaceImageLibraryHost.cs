using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NHSE.Core;
using NHSE.Sprites;

namespace NHSE.WinForms;

public sealed partial class WorkspaceForm
{
    private sealed record ImageSource(int Id, string Name, string Url);
    private static readonly ImageSource[] ImageSources = Properties.Resources.hosts_images.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select((url, index) => { var uri = new Uri(url.Trim(), UriKind.Absolute); return new ImageSource(index, uri.Host + uri.AbsolutePath[..uri.AbsolutePath.LastIndexOf('/')], uri.AbsoluteUri); }).ToArray();
    private CancellationTokenSource? ImageCancellation;
    private Task? ImageTask;
    private WorkspaceImageLibrary.Progress ImageProgress = new("idle");
    private string? ImageError;
    private long? ImageSourceBytes;
    private int ImageSelectedSource, ImageInstalledCount = -1;
    private bool ImageCloseApproved;
    private object ImageLibraryState()
    {
        if (ImageInstalledCount < 0) ImageInstalledCount = WorkspaceImageLibrary.Count(ItemSprite.PlatformAppDataImagePath);
        return new { sources = ImageSources, source = ImageSelectedSource, installed = ImageInstalledCount, size = ImageSourceBytes,
            phase = ImageProgress.Phase, received = ImageProgress.Received, total = ImageProgress.Total, files = ImageProgress.Files,
            fileCount = ImageProgress.FileCount, error = ImageError };
    }
    private static ImageSource ImageSourceAt(int id) => id >= 0 && id < ImageSources.Length && Uri.TryCreate(ImageSources[id].Url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps ? ImageSources[id] : throw new ArgumentException("invalidRequest");
    private async Task<object> ImageSourceSizeAsync(int id)
    {
        var source = ImageSourceAt(id);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        ImageSourceBytes = await WorkspaceImageLibrary.SizeAsync(client, new Uri(source.Url), CancellationToken.None);
        ImageSelectedSource = id; return ImageLibraryState();
    }
    private object StartImageLibrary(int id)
    {
        if (ImageTask is { IsCompleted: false } || ImageProgress.Phase is "downloading" or "extracting" or "installing" or "cancelling") throw new ArgumentException("busy");
        var source = ImageSourceAt(id);
        ImageCancellation?.Dispose(); ImageCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        ImageProgress = new("downloading"); ImageError = null; ImageSelectedSource = id;
        ImageTask = RunImageLibraryAsync(source, ImageCancellation.Token); return ImageLibraryState();
    }
    private void PostImageProgress(WorkspaceImageLibrary.Progress progress, string? error = null)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() => {
            if (IsDisposed) return;
            if (ImageCancellation?.IsCancellationRequested == true && progress.Phase is "downloading" or "extracting") return;
            ImageProgress = progress; ImageError = error;
            if (progress.Phase == "complete") { ItemSprite.Initialize(GameInfo.GetStrings("en").itemlist); ImageInstalledCount = progress.Files; }
            Reply(new { eventName = "imageLibrary", data = ImageLibraryState() });
        });
    }
    private async Task RunImageLibraryAsync(ImageSource source, CancellationToken token)
    {
        try
        {
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            await WorkspaceImageLibrary.InstallAsync(client, new Uri(source.Url), ItemSprite.PlatformAppDataImagePath, p => PostImageProgress(p), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { PostImageProgress(new("cancelled")); }
        catch (Exception ex) { PostImageProgress(new("failed"), ex.Message == "imagesTooLarge" ? "imagesTooLarge" : ex is InvalidDataException or EndOfStreamException ? "imagesInvalidPackage" : "imagesDownloadFailed"); }
    }
    private object CancelImageLibrary()
    {
        if (ImageTask is { IsCompleted: false } && ImageProgress.Phase != "installing") { ImageCancellation?.Cancel(); ImageProgress = ImageProgress with { Phase = "cancelling" }; }
        return ImageLibraryState();
    }
    private async Task CloseAfterImagesAsync()
    {
        try { if (ImageTask is { } task) await task; }
        finally { ImageCloseApproved = true; Close(); }
    }
}
