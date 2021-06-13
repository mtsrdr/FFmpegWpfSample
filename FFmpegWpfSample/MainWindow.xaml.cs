using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FFmpegWpfSample
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _viewModel = new MainViewModel();
            _viewModel.CamerasStarted += OnCamerasStarted;
            _viewModel.CamerasStopped += OnCamerasStopped;
        }

        private void OnCamerasStarted(object sender, EventArgs e)
        {
            ClearCameraLayout();
            SetRowsAndColumns();
            SetCameraControls();
        }

        private void OnCamerasStopped(object sender, EventArgs e)
        { 
            ClearCameraLayout();
        }

        private void ClearCameraLayout()
        {
            CamerasLayout.Children.Clear();
            CamerasLayout.RowDefinitions.Clear();
            CamerasLayout.ColumnDefinitions.Clear();
        }

        private void SetCameraControls()
        {
            int rowsCount = CamerasLayout.RowDefinitions.Count;
            int columnsCount = CamerasLayout.ColumnDefinitions.Count;
            int realPlaysCount = _viewModel.Decoders.Count;
            int nextColumn, nextRow = 0, cameraControlsCount = 0;
            var realPlayModels = _viewModel.RealPlayModels.Values.ToList();

            for (int row = 0; row < rowsCount; row++)
            {
                nextColumn = 0;

                for (int column = 0; column < columnsCount; column++)
                {
                    Image imageCam = new Image()
                    {
                        Stretch = Stretch.Fill
                    };
                    Grid cell = new Grid() 
                    { 
                        Background = Brushes.DarkRed, 
                        Margin = new Thickness(8) 
                    };
                    TextBlock txtMessage = new TextBlock() 
                    { 
                        FontSize = 22.0,
                        Foreground = Brushes.White, 
                        HorizontalAlignment = HorizontalAlignment.Center, 
                        VerticalAlignment = VerticalAlignment.Center 
                    };

                    imageCam.Margin = new Thickness(6);
                    cell.Children.Add(imageCam);
                    cell.Children.Add(txtMessage);

                    Grid.SetRow(cell, nextRow);
                    Grid.SetColumn(cell, nextColumn);
                    CamerasLayout.Children.Add(cell);
                    cameraControlsCount++;

                    if (cameraControlsCount <= realPlaysCount)
                    {
                        int realPlayIndex = cameraControlsCount - 1;
                        RealPlayModel realPlay = realPlayModels[realPlayIndex];
                        cell.DataContext = realPlay;

                        imageCam.SetBinding(Image.SourceProperty, nameof(realPlay.CurrentFrame));
                        txtMessage.SetBinding(TextBlock.TextProperty, nameof(realPlay.ErrorMessage));
                    }

                    if (nextColumn < (columnsCount - 1))
                        nextColumn++;
                }

                nextRow++;
            }
        }

        private void SetRowsAndColumns()
        {
            Grid grid = CamerasLayout;
            int maxColumns = GetMaxColumns();
            GridLength singleGridLength = new GridLength(1, GridUnitType.Star);

            for (int i = 0; i < maxColumns; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = singleGridLength });
                grid.RowDefinitions.Add(new RowDefinition() { Height = singleGridLength });
            }
        }

        private int GetMaxColumns()
        {
            var channelsCount = _viewModel.Decoders.Count;

            if (channelsCount == 1)
                return 1;
            else if (channelsCount <= 4)
                return 2;
            else if (channelsCount <= 9)
                return 3;
            else if (channelsCount <= 16)
                return 4;
            else if (channelsCount <= 25)
                return 5;
            else if (channelsCount <= 36)
                return 6;
            else if (channelsCount <= 49)
                return 7;
            else
                throw new NotImplementedException("Unhandled number of channels.");
        }
    }
}
