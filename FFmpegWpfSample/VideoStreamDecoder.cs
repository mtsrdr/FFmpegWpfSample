﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FFmpeg.AutoGen;

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
            _dispacher = System.Windows.Application.Current.Dispatcher;
        }

        public void Start(string url)
        {
            if (string.IsNullOrEmpty(url))
                return;
            if (_thread != null && _thread.IsAlive)
                return;

            _source = url;
            _keepAlive = true;

            _thread = new Thread(StartWorkerThread);
            _thread.Start();
        }

        public void Stop()
        {
            _keepAlive = false;
            Marshal.FreeHGlobal(_convertedFrameBufferPtr);
        }

        private void StartWorkerThread()
        {
            int response = RunFFmpeg();
            if (response < 0 && response != _defaultResponse)
            {
                string error = AVStringError(response);
                Debug.WriteLine($"Error: {error}");
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

                if (_source.StartsWith("rtsp")
                    || _source.StartsWith("rtp")
                    || _source.StartsWith("http"))
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
                    if (response == ffmpeg.AVERROR(ffmpeg.EAGAIN) || response == ffmpeg.AVERROR(ffmpeg.AVERROR_EOF))
                        continue;
                    else if (response < 0)
                        return response;

                    //Bitmap bitmap = ConvertFrameToBitmap(av_frame, av_codec_ctx);
                    //_dispacher.Invoke(() => OnNewFrame?.Invoke(this, bitmap));
                    AVFrame convertedFrame = ConvertFrameToRGB(av_frame, av_codec_ctx);
                    
                    _dispacher.Invoke(() =>
                    {
                        BitmapSource bmp = ConvertToBitmapSource(convertedFrame);
                        OnNewFrame?.Invoke(this, bmp);
                    });
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
            AVPixelFormat destinationPxtFormat = AVPixelFormat.AV_PIX_FMT_BGR24;
            int width = source_frame->width;
            int height = source_frame->height;

            Marshal.FreeHGlobal(_convertedFrameBufferPtr);
            _convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(destinationPxtFormat, width, height, 1);
            _convertedFrameBufferPtr = Marshal.AllocHGlobal(_convertedFrameBufferSize);

            byte_ptrArray4 _dstData = new byte_ptrArray4();
            int_array4 _dstLinesize = new int_array4();

            if (_sws_scaler_ctx == null)
            {
                _sws_scaler_ctx = ffmpeg.sws_getContext(width,
                    height,
                    av_codec_ctx->pix_fmt,
                    width,
                    height,
                    destinationPxtFormat,
                    ffmpeg.SWS_FAST_BILINEAR,
                    null,
                    null,
                    null);
            }

            ffmpeg.av_image_fill_arrays(ref _dstData,
                ref _dstLinesize,
                (byte*)_convertedFrameBufferPtr,
                destinationPxtFormat,
                width,
                height,
                1);

            ffmpeg.sws_scale(_sws_scaler_ctx,
                source_frame->data,
                source_frame->linesize,
                0,
                height,
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
                width = width,
                height = height,
            };
            return convertedFrame;
        }

        private BitmapSource ConvertToBitmapSource(AVFrame avFrame)
        {
            int width = avFrame.width;
            int height = avFrame.height;
            int stride = avFrame.linesize[0];
            int bufferSize = _convertedFrameBufferSize;
            byte_ptrArray8 data = avFrame.data;

            WriteableBitmap wbm = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);
            wbm.WritePixels(new Int32Rect(0, 0, width, height), (IntPtr)data[0], bufferSize, stride);
            wbm.Freeze();
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