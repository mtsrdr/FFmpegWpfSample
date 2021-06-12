using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FFmpeg.AutoGen;
using SixLabors.ImageSharp;

namespace FFmpegWpfSample
{
    public unsafe class VideoStreamDecoder
    {
        private const int _defaultResponse = -1111111;
        public string Id { get; set; }

        private Thread _thread;
        private string _source;
        private bool _keepAlive;
        private SwsContext* _sws_scaler_ctx;
        private int _convertedFrameBufferSize;
        private IntPtr _convertedFrameBufferPtr;
        private Dispatcher _dispacher;

        public EventHandler<BitmapSource> OnNewFrame { get; set; }
        public EventHandler<string> OnError { get; set; }

        public VideoStreamDecoder()
        {
            ffmpeg.RootPath = @"C:\ffmpeg\libs";
            _dispacher = Application.Current.Dispatcher;
        }

        public void Start(string url)
        {
            if (string.IsNullOrEmpty(url))
                return;

            _source = url;
            _keepAlive = true;

            _thread = new Thread(StartWorkerThread);
            _thread.Start();
        }

        public void Stop()
        {
            _keepAlive = false;
            if (_convertedFrameBufferPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_convertedFrameBufferPtr);
                _convertedFrameBufferPtr = IntPtr.Zero;
            }
        }

        private void StartWorkerThread()
        {
            int response = RunFFmpeg();
            if (response < 0 && response != _defaultResponse)
            {
                string error = AVStringError(response);
                Debug.WriteLine($"Error: {error}");
                _dispacher.Invoke(() => OnError?.Invoke(this, error));
            }
        }

        private int RunFFmpeg()
        {
            int response = _defaultResponse;
            AVFormatContext* av_format_ctx = ffmpeg.avformat_alloc_context();
            AVPacket* av_packet = ffmpeg.av_packet_alloc();
            AVFrame* av_frame = ffmpeg.av_frame_alloc();
            AVCodecContext* av_codec_ctx;

            try
            {
                response = ffmpeg.avformat_network_init();
                if (response < 0)
                    return response;

                response = ffmpeg.avformat_open_input(&av_format_ctx, _source, null, null);
                if (response < 0)
                    return response;

                response = ffmpeg.avformat_find_stream_info(av_format_ctx, null);
                if (response < 0)
                    return response;

                AVCodec* av_codec = null;
                int videoStreamIndex = ffmpeg.av_find_best_stream(av_format_ctx, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &av_codec, 0);
                if (videoStreamIndex < 0)
                    return response;

                av_codec_ctx = ffmpeg.avcodec_alloc_context3(av_codec);
                AVStream* avStream = av_format_ctx->streams[videoStreamIndex];
                AVCodecParameters* av_codec_params = avStream->codecpar;

                response = ffmpeg.avcodec_parameters_to_context(av_codec_ctx, av_codec_params);
                if (response < 0)
                    return response;

                response = ffmpeg.avcodec_open2(av_codec_ctx, av_codec, null);
                if (response < 0)
                    return response;

                Uri _sourceUri = new Uri(_source);
                if (!_sourceUri.IsFile)
                {
                    response = ffmpeg.av_read_play(av_format_ctx);
                    if (response < 0)
                        return response;
                }

                ffmpeg.av_frame_unref(av_frame);
                ffmpeg.av_packet_unref(av_packet);

                while (ffmpeg.av_read_frame(av_format_ctx, av_packet) >= 0)
                {
                    if (!_keepAlive)
                        return _defaultResponse;

                    if (av_packet->stream_index != videoStreamIndex)
                        continue;

                    response = ffmpeg.avcodec_send_packet(av_codec_ctx, av_packet);
                    if (response < 0)
                        return response;

                    response = ffmpeg.avcodec_receive_frame(av_codec_ctx, av_frame);
                    if (response == ffmpeg.EAGAIN || response == ffmpeg.AVERROR_EOF)
                        continue;
                    else if (response < 0)
                        return response;

                    AVFrame convertedFrame = ConvertFrameToRGB(av_frame, av_codec_ctx);
                    BitmapSource bmp = ConvertToBitmapSource(convertedFrame);
                    //BitmapSource bmp = ConvertWithImageSharp(convertedFrame);
                    _dispacher.Invoke(() => OnNewFrame?.Invoke(this, bmp));
                }
            }
            finally
            {
                ffmpeg.avformat_free_context(av_format_ctx);
                ffmpeg.av_frame_free(&av_frame);
                ffmpeg.av_packet_free(&av_packet);
                ffmpeg.avformat_network_deinit();
            }

            return response;
        }

        private AVFrame ConvertFrameToRGB(AVFrame* source_frame, AVCodecContext* av_codec_ctx)
        {
            AVPixelFormat destinationPxtFormat = AVPixelFormat.AV_PIX_FMT_BGRA;
            int srcWidth = source_frame->width;
            int srcHeight = source_frame->height;
            int destWidth = srcWidth >= 1280 ? 640 : srcWidth;
            int destHeight = srcHeight >= 720 ? 480 : srcHeight;
            Marshal.FreeHGlobal(_convertedFrameBufferPtr);
            _convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(destinationPxtFormat, destWidth, destHeight, 1);
            _convertedFrameBufferPtr = Marshal.AllocHGlobal(_convertedFrameBufferSize);

            byte_ptrArray4 _dstData = new byte_ptrArray4();
            int_array4 _dstLinesize = new int_array4();

            if (_sws_scaler_ctx == null)
            {
                _sws_scaler_ctx = ffmpeg.sws_getContext(
                    srcWidth,
                    srcHeight,
                    av_codec_ctx->pix_fmt,
                    destWidth,
                    destHeight,
                    destinationPxtFormat,
                    ffmpeg.SWS_FAST_BILINEAR,
                    null,
                    null,
                    null);
            }

            _ = ffmpeg.av_image_fill_arrays(
                ref _dstData,
                ref _dstLinesize,
                (byte*)_convertedFrameBufferPtr,
                destinationPxtFormat,
                destWidth,
                destHeight,
                1);

            _ = ffmpeg.sws_scale(
                _sws_scaler_ctx,
                source_frame->data,
                source_frame->linesize,
                0,
                srcHeight,
                _dstData,
                _dstLinesize);

            byte_ptrArray8 data = new byte_ptrArray8();
            data.UpdateFrom(_dstData);
            int_array8 linesize = new int_array8();
            linesize.UpdateFrom(_dstLinesize);

            AVFrame convertedFrame = new AVFrame
            {
                data = data,
                linesize = linesize,
                width = destWidth,
                height = destHeight
            };

            return convertedFrame;
        }

        //private static BmpEncoder _bmpEncoder = new BmpEncoder(); // Can be passed as an argument to SaveAsBmp
        private BitmapImage ConvertWithImageSharp(AVFrame avFrame)
        {
            byte[] data = new byte[_convertedFrameBufferSize];
            Marshal.Copy((IntPtr)avFrame.data[0], data, 0, _convertedFrameBufferSize);
            Image<SixLabors.ImageSharp.PixelFormats.Bgra32> image = Image.LoadPixelData<SixLabors.ImageSharp.PixelFormats.Bgra32>(data, avFrame.width, avFrame.height);

            var ms = new MemoryStream();
            image.SaveAsBmp(ms);
            ms.Position = 0;
            
            BitmapImage bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private BitmapSource ConvertToBitmapSource(AVFrame avFrame)
        {
            int width = avFrame.width;
            int height = avFrame.height;
            int stride = avFrame.linesize[0];
            int bufferSize = _convertedFrameBufferSize;
            byte_ptrArray8 data = avFrame.data;

            WriteableBitmap wbm = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            wbm.WritePixels(new Int32Rect(0, 0, width, height), (IntPtr)data[0], bufferSize, stride);
            wbm.Freeze();

            /* Exemplo de como codificar o WriteableBitmap em uma Stream corretamente.
            byte[] wbmBytes = wbm.ConvertToByteArray();
            IBuffer buffer = wbmBytes.AsBuffer();
            InMemoryRandomAccessStream ims = new InMemoryRandomAccessStream();
            ims.WriteAsync(buffer).AsTask().GetAwaiter().GetResult();
            ims.FlushAsync().AsTask().GetAwaiter().GetResult();
            ims.Seek(0);

            WinBitmapEncoder encoder = WinBitmapEncoder.CreateAsync(WinBitmapEncoder.PngEncoderId, ims).AsTask().GetAwaiter().GetResult();

            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, (uint)wbm.PixelWidth, (uint)wbm.PixelHeight, 96.0, 96.0, wbmBytes);
            encoder.FlushAsync().AsTask().GetAwaiter().GetResult();
            
            Stream streamResult = ims.AsStream();
            MemoryStream ms = new MemoryStream();
            streamResult.CopyTo(ms);
            ms.Seek(0, SeekOrigin.Begin);

            const string filePath = "output_bitmap.png";
            if (File.Exists(filePath))
                File.Delete(filePath);

            FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            byte[] bytes = ms.ToArray();
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush();

            streamResult.Dispose();
            ms.Dispose();
            fs.Dispose();
            */

            return wbm;
        }

        private unsafe string AVStringError(int error)
        {
            int bufferSize = 1024;
            byte* buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
            string message = Marshal.PtrToStringAnsi((IntPtr)buffer);
            return message;
        }
    }
}