using System.Windows.Media.Imaging;

namespace FFmpegWpfSample
{
    public static class BitmapExtensions
    {
        public static byte[] ConvertToByteArray(this WriteableBitmap renderTarget)
        {
            if (renderTarget == null || renderTarget.PixelHeight == 0 || renderTarget.PixelWidth == 0)
                return null;

            int stride = renderTarget.PixelWidth * renderTarget.Format.BitsPerPixel / 8;
            int size = stride * renderTarget.PixelHeight;

            byte[] buffer = new byte[size];
            renderTarget.CopyPixels(buffer, stride, 0);
            return buffer;
        }

        public static byte[] ConvertToByteArray(this RenderTargetBitmap renderTarget)
        {
            if (renderTarget == null || renderTarget.PixelHeight == 0 || renderTarget.PixelWidth == 0)
                return null;

            int stride = renderTarget.PixelWidth * renderTarget.Format.BitsPerPixel / 8;
            int size = stride * renderTarget.PixelHeight;

            byte[] buffer = new byte[size];
            renderTarget.CopyPixels(buffer, stride, 0);
            return buffer;
        }
    }
}
