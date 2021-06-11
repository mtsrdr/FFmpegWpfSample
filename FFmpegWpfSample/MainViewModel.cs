using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Media.Imaging;
using MvvmHelpers;
using MvvmHelpers.Commands;

namespace FFmpegWpfSample
{
    public class RealPlayModel : ObservableObject
    {
        public string Id { get; set; } 

        private BitmapSource _currentFrame;
        public BitmapSource CurrentFrame
        {
            get => _currentFrame;
            set => _ = SetProperty(ref _currentFrame, value);
        }

        private string _errorMessage;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => _ = SetProperty(ref _errorMessage, value);
        }
    }

    public class MainViewModel : ObservableObject
    {
        public EventHandler CamerasStarted { get; set; }
        public EventHandler CamerasStopped { get; set; }
        public Dictionary<string, VideoStreamDecoder> Decoders { get; }
        public Dictionary<string, RealPlayModel> RealPlayModels { get; }
        public ObservableRangeCollection<int> NumberOfStreamOptions { get; }
        public Command StartCommand { get; }
        public Command StopCommand { get; }

        private string _address;
        public string Address
        {
            get => _address;
            set => _ = SetProperty(ref _address, value, onChanged: StartCommand.RaiseCanExecuteChanged);
        }

        private int? _numberOfStreams;
        public int? NumberOfStreams
        {
            get => _numberOfStreams;
            set => _ = SetProperty(ref _numberOfStreams, value, onChanged: StartCommand.RaiseCanExecuteChanged);
        }

        private bool _isRunning;
        public bool IsRunning 
        {
            get => _isRunning;
            set => _ = SetProperty(ref _isRunning, value, onChanged: () => 
            {
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
            });
        }

        public MainViewModel()
        {
            Decoders = new Dictionary<string, VideoStreamDecoder>();
            RealPlayModels = new Dictionary<string, RealPlayModel>();
            NumberOfStreamOptions = new ObservableRangeCollection<int>(Enumerable.Range(1, 32));
            StartCommand = new Command(Start, (_) => !IsRunning && !string.IsNullOrEmpty(Address) && NumberOfStreams.HasValue);
            StopCommand = new Command(Stop, (_) => IsRunning);
        }

        private void Start(object obj)
        {
            Stop(null);

            for (int i = 0; i < _numberOfStreams; i++)
            {
                string referenceId = Guid.NewGuid().ToString(); 
                VideoStreamDecoder decoder = new VideoStreamDecoder();
                decoder.Id = referenceId;
                decoder.OnNewFrame += OnNewFrame;
                decoder.OnError += OnError;
                decoder.Start(_address);

                RealPlayModel realPlay = new RealPlayModel();
                realPlay.Id = referenceId;

                RealPlayModels.Add(referenceId, realPlay);
                Decoders.Add(referenceId, decoder);
            }

            CamerasStarted?.Invoke(this, EventArgs.Empty);
            IsRunning = true;
        }

        private void Stop(object obj)
        {
            foreach (var decoder in Decoders.Values)
            {
                decoder.OnNewFrame -= OnNewFrame;
                decoder.OnError -= OnError;
                decoder.Stop();
            }

            RealPlayModels.Clear();
            Decoders.Clear();
            CamerasStopped?.Invoke(this, EventArgs.Empty);
            IsRunning = false;
        }

        private void OnNewFrame(object sender, BitmapSource bitmap)
        {
            if (!(sender is VideoStreamDecoder decoder))
                return;
            RealPlayModel realPlay = RealPlayModels[decoder.Id];

            try
            {
                realPlay.CurrentFrame = bitmap;

                /*
                MemoryStream ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Bmp);
                _ = ms.Seek(0, SeekOrigin.Begin);

                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                realPlay.CurrentFrame = bi;
                */
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                realPlay.CurrentFrame = null;
                realPlay.ErrorMessage = ex.Message;
            }
        }

        private void OnError(object sender, string error)
        {
            if (!(sender is VideoStreamDecoder decoder))
                return;

            RealPlayModel realPlay = RealPlayModels[decoder.Id];
            realPlay.CurrentFrame = null;
            realPlay.ErrorMessage = error;
        }
    }
}
