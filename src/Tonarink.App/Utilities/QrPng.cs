using Net.Codecrete.QrCodeGenerator;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

static class QrPng
{
    public static async Task<string> WriteAsync(string text, CancellationToken cancellationToken = default)
    {
        var qr = QrCode.EncodeText(text, QrCode.Ecc.Medium);
        const int scale = 8;
        const int border = 4;
        var size = (qr.Size + border * 2) * scale;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < size; x++)
            {
                var moduleX = x / scale - border;
                var moduleY = y / scale - border;
                var dark = qr.GetModule(moduleX, moduleY);
                var index = (y * size + x) * 4;
                var value = dark ? (byte)0 : (byte)255;
                pixels[index] = value;
                pixels[index + 1] = value;
                pixels[index + 2] = value;
                pixels[index + 3] = 255;
            }
        }

        var path = Path.Combine(Path.GetTempPath(), "tonarink-webshare-qr.png");
        await using var file = File.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        using var random = file.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, random);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            (uint)size,
            (uint)size,
            96,
            96,
            pixels);
        await encoder.FlushAsync();
        return path;
    }
}
