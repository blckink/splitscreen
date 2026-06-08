using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SplitPlay.App.Imaging;

/// <summary>
/// Builds WPF <see cref="ImageSource"/>s from a local file path or an http(s) URL,
/// with sensible performance defaults: images are decoded down to the size they
/// are actually shown at (saving memory), local images are fully loaded then
/// frozen (so they can be shared across threads and are GC-friendly), and remote
/// images load asynchronously so the UI never blocks on the network.
/// </summary>
public static class ImageLoader
{
    /// <summary>
    /// Attempts to load an image. Returns null on any failure so callers can fall
    /// back to a placeholder without try/catch noise.
    /// </summary>
    /// <param name="pathOrUrl">Local file path or http(s) URL.</param>
    /// <param name="decodePixelWidth">Target decode width in pixels (0 = full).</param>
    public static ImageSource? TryLoad(string? pathOrUrl, int decodePixelWidth = 0)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return null;
        }

        try
        {
            bool isRemote = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(pathOrUrl, UriKind.RelativeOrAbsolute);
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;

            // Local files: load fully now so we can freeze and release the handle.
            // Remote: leave on-demand so the download happens off the UI thread.
            bitmap.CacheOption = isRemote ? BitmapCacheOption.OnDemand : BitmapCacheOption.OnLoad;

            if (decodePixelWidth > 0)
            {
                bitmap.DecodePixelWidth = decodePixelWidth;
            }

            bitmap.EndInit();

            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
