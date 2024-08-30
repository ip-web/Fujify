using MaterialDesignThemes.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using static MaterialDesignThemes.Wpf.Theme;

namespace FujifyNET
{
    public partial class MainWindow : Window
    {
        private const string defArgs = "-CameraProfilesMake=\"FUJIFILM\" -CameraProfilesModel=\"X-T5\" -CameraProfilesUniqueCameraModel=\"Fujifilm X-T5\" -CameraProfilesCameraRawProfile=\"True\" -UniqueCameraModel=\"Fujifilm X-T5\" -overwrite_original";
        private const string custArgs = "-CameraProfilesMake=\"FUJIFILM\" -CameraProfilesModel=\"X-T5\" -CameraProfilesUniqueCameraModel=\"Fujifilm X-T5\" -CameraProfilesCameraRawProfile=\"True\" -UniqueCameraModel=\"Fujifilm X-T5\"";
        private BackgroundWorker backgroundWorker;
        public ObservableCollection<FileItem> Files { get; set; }
        private string defaultArguments = defArgs;
        private string customArguments = custArgs;
        private string currentArguments;
        private string? outputFolderPath = null;
        // Supported file formats
        private readonly HashSet<string> supportedFormats =
        [
            ".dng",".cr3", ".crw", ".erf", ".raf", ".3fr", ".kdc", ".dcs", ".dcr", ".iiq", ".mos",
            ".mef", ".mrw", ".nef", ".nrw", ".orf", ".rw2", ".pef", ".srw", ".arw", ".srf", ".sr2"
        ];
        private bool hasProcessingErrors = false;
        private bool isTextBlockHidden = false; // Track visibility state
        private bool isDialogOpen = false;

        private GridViewColumnHeader _lastHeaderClicked;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            backgroundWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            backgroundWorker.DoWork += BackgroundWorker_DoWork;
            backgroundWorker.ProgressChanged += BackgroundWorker_ProgressChanged;
            backgroundWorker.RunWorkerCompleted += BackgroundWorker_RunWorkerCompleted;

            Files = [];
            DataContext = this;

            Files.CollectionChanged += Files_CollectionChanged;
            processButton.IsEnabled = false;
            clearButton.IsEnabled = false;

            listView.SizeChanged += (s, e) => RecalculateFirstColumnWidth();
            currentArguments = customArguments;

        }
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            DwmDropShadow.DropShadowToWindow(this);
        }
        public static class DwmDropShadow
        {
            [DllImport("dwmapi.dll", PreserveSig = true)]
            private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

            [DllImport("dwmapi.dll")]
            private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref Margins pMarInset);

            public static void DropShadowToWindow(Window window)
            {
                if (!DropShadow(window))
                {
                    window.SourceInitialized += new EventHandler(window_SourceInitialized);
                }
            }

            private static void window_SourceInitialized(object sender, EventArgs e)
            {
                Window window = (Window)sender;
                DropShadow(window);
                window.SourceInitialized -= new EventHandler(window_SourceInitialized);
            }

            private static bool DropShadow(Window window)
            {
                try
                {
                    WindowInteropHelper helper = new WindowInteropHelper(window);
                    int val = 2;
                    int ret1 = DwmSetWindowAttribute(helper.Handle, 2, ref val, 4);

                    if (ret1 == 0)
                    {
                        Margins m = new Margins { Bottom = 0, Left = 0, Right = 0, Top = 0 };
                        int ret2 = DwmExtendFrameIntoClientArea(helper.Handle, ref m);
                        return ret2 == 0;
                    }
                    else
                    {
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    // Handle exceptions here, e.g., log or notify the user
                    return false;
                }
            }
        }

        public class FileItem : INotifyPropertyChanged
        {
            private string filePath = string.Empty;
            private string status = string.Empty;
            private BitmapImage thumbnail = new();
            private string cameraMakeAndModel = string.Empty;

            public bool HasAnimationPlayed { get; set; } = false;

            public required string FilePath
            {
                get => filePath;
                set
                {
                    if (filePath != value)
                    {
                        filePath = value;
                        OnPropertyChanged(nameof(FilePath));
                        OnPropertyChanged(nameof(FileName));
                    }
                }
            }

            public string FileName => System.IO.Path.GetFileName(filePath);

            public string Status
            {
                get => status;
                set
                {
                    if (status != value)
                    {
                        status = value;
                        OnPropertyChanged(nameof(Status));
                    }
                }
            }

            public BitmapImage Thumbnail
            {
                get => thumbnail;
                set
                {
                    if (thumbnail != value)
                    {
                        thumbnail = value;
                        OnPropertyChanged(nameof(Thumbnail));
                    }
                }
            }

            public string CameraMakeAndModel
            {
                get => cameraMakeAndModel;
                set
                {
                    if (cameraMakeAndModel != value)
                    {
                        cameraMakeAndModel = value;
                        OnPropertyChanged(nameof(CameraMakeAndModel));
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            protected virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Retrieve the storyboards from XAML
            var fadeInStoryboard = (Storyboard)FindResource("FadeInStoryboardMain");
            var scaleUpStoryboard = (Storyboard)FindResource("ScaleUpStoryboard");

            // Set the target for the fade-in animation
            Storyboard.SetTarget(fadeInStoryboard, this);
            Storyboard.SetTargetProperty(fadeInStoryboard, new PropertyPath("Opacity"));
            fadeInStoryboard.Begin();

            // Set the target for the scale-up animation
            Storyboard.SetTarget(scaleUpStoryboard, MainGrid); // MainGrid is the target FrameworkElement
            scaleUpStoryboard.Begin();
            RecalculateFirstColumnWidth();
        }
     
        private void MainWindow_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] data && data.All(path => Directory.Exists(path) || File.Exists(path)))
                {
                    e.Effects = System.Windows.DragDropEffects.Copy;
                }
                else
                {
                    e.Effects = System.Windows.DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
        }

        private void MainWindow_DragDrop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
            {
                var files = new List<string>();

                foreach (var path in paths)
                {
                    if (Directory.Exists(path))
                    {
                        // Add all supported files from the folder
                        files.AddRange(GetFilesFromFolder(path));
                    }
                    else if (File.Exists(path))
                    {
                        files.Add(path);
                    }
                }

                // Add files to the ListView
                AddFiles(files.Where(file => File.Exists(file)));
            }
        }

        private IEnumerable<string> GetFilesFromFolder(string folderPath)
        {
            try
            {
                // Get all files in the directory and its subdirectories
                var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                                     .Where(file => supportedFormats.Contains(System.IO.Path.GetExtension(file).ToLower()));
                return files;
            }
            catch (Exception ex)
            {
                // Handle exceptions such as access denied or path too long
                ShowCustomMessageBox("Error", $"Failed to access files in folder: {ex.Message}");
                return [];
            }
        }
        private void AddDngFilesFromFolder(string folderPath)
        {
            // Get all files in the folder and subfolders that match the supported formats or are .dng files
            var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                                 .Where(f => supportedFormats.Contains(System.IO.Path.GetExtension(f).ToLower()));

            // Use the AddFiles method to add these files
            AddFiles(files);
        }

        // Method to add files to the ListView
        public async void AddFiles(IEnumerable<string> filePaths)
        {
            ShowLoadingSpinner(true);
            UpdateTextBlockVisibility();
            try
            {
                await Task.Run(() =>
                {
                    foreach (var filePath in filePaths)
                    {
                        string extension = System.IO.Path.GetExtension(filePath).ToLower();

                        if (!Files.Any(f => f.FilePath == filePath))
                        {
                            if (extension == ".dng" || supportedFormats.Contains(extension))
                            {
                                string processedFilePath = filePath;
                                var thumbnail = GenerateThumbnail(processedFilePath);

                                // Get camera make and model
                                string cameraMakeAndModel = GetCameraMakeAndModel(processedFilePath);

                                Dispatcher.Invoke(() =>
                                {
                                    // Add the file to the list
                                    var newItem = new FileItem
                                    {
                                        FilePath = processedFilePath,
                                        Status = "Pending",
                                        Thumbnail = thumbnail,
                                        CameraMakeAndModel = cameraMakeAndModel
                                    };
                                    Files.Add(newItem);
                                });
                            }
                            else
                            {
                                Dispatcher.Invoke(() => ShowCustomMessageBox("Unsupported File Format", $"The file format {extension} is not supported."));
                            }
                        }
                    }
                });
            }
            finally
            {
                ShowLoadingSpinner(false);
                UpdateButtonState();

            }
        }
        private static string GetCameraMakeAndModel(string filePath)
        {
            string cameraMakeAndModel = string.Empty;

            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string exifToolPath = System.IO.Path.Combine(exeDirectory, "libs", "exiftool.exe");
            string arguments = $"-Make -Model \"{filePath}\"";

            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = exifToolPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);

                if (process == null)
                {
                    cameraMakeAndModel = "Error starting process";
                    return cameraMakeAndModel;
                }

                using var outputReader = process.StandardOutput;
                using var errorReader = process.StandardError;

                var output = outputReader.ReadToEnd();
                var error = errorReader.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    string cameraMake = ExtractExifValue(output, "Make");
                    string cameraModel = ExtractExifValue(output, "Camera Model Name");

                    // Shorten "NIKON CORPORATION" to "NIKON"
                    if (cameraMake.Equals("NIKON CORPORATION", StringComparison.OrdinalIgnoreCase))
                    {
                        cameraMake = "NIKON";
                    }

                    cameraMakeAndModel = $"{cameraMake} {cameraModel}";
                }
                else
                {
                    cameraMakeAndModel = "Error retrieving camera info";
                }
            }
            catch (Exception ex)
            {
                cameraMakeAndModel = $"Exception: {ex.Message}";
            }

            return cameraMakeAndModel;
        }

        // Helper function to parse EXIF output
        private static string ExtractExifValue(string exifOutput, string tagName)
        {
            var match = Regex.Match(exifOutput, $@"{tagName}\s*:\s*(.+)");
            return match.Success ? match.Groups[1].Value.Trim() : "Unknown";
        }

        private static string ExtractThumbnailWithDcraw(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return string.Empty; // Handle null or empty filePath case
            }

            string dcrawPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", "libraw", "simple_dcraw.exe");

            // Set up process info
            ProcessStartInfo startInfo = new()
            {
                FileName = dcrawPath,
                Arguments = $"-e \"{filePath}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process();
            process.StartInfo = startInfo;

            // Start the process
            process.Start();
            process.WaitForExit();

            // Thumbnail paths
            string? directory = System.IO.Path.GetDirectoryName(filePath);
            if (directory == null)
            {
                return string.Empty; // Handle the case where directory is null
            }

            string thumbnailPathJpg = System.IO.Path.Combine(directory, System.IO.Path.GetFileName(filePath) + ".thumb.jpg");
            string thumbnailPathPpm = System.IO.Path.Combine(directory, System.IO.Path.GetFileName(filePath) + ".thumb.ppm");

            // Check for existence of the thumbnail files
            if (File.Exists(thumbnailPathJpg))
            {
                return thumbnailPathJpg;
            }
            else if (File.Exists(thumbnailPathPpm))
            {
                return thumbnailPathPpm;
            }

            return string.Empty; // Return an empty string if no thumbnail is found
        }

        private static BitmapImage GenerateThumbnail(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                // Return placeholder image if filePath is null or empty
                return LoadPlaceholderImage;
            }

            string extension = System.IO.Path.GetExtension(filePath).ToLower();
            BitmapImage? bitmapImage = null;

            try
            {
                if (extension != ".dng")
                {
                    // Extract the thumbnail and check its extension
                    string thumbnailPath = ExtractThumbnailWithDcraw(filePath);
                    if (!string.IsNullOrEmpty(thumbnailPath))
                    {
                        string thumbnailExtension = System.IO.Path.GetExtension(thumbnailPath).ToLower();

                        // Skip unsupported thumbnail file types
                        if (thumbnailExtension == ".ppm")
                        {
                            // Use placeholder for unsupported thumbnail types
                            
                            try
                            {
                                File.Delete(thumbnailPath);
                            }
                            catch (IOException ioEx)
                            {
                                Debug.WriteLine($"Error deleting thumbnail file {thumbnailPath}: {ioEx.Message}");
                            }
                            return LoadPlaceholderImage;
                        }

                        // Generate BitmapImage from the extracted thumbnail
                        bitmapImage = CreateBitmapImageFromPath(thumbnailPath);

                        // Safely delete the thumbnail file after loading
                        if (bitmapImage != null && File.Exists(thumbnailPath) && !thumbnailPath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                File.Delete(thumbnailPath);
                            }
                            catch (IOException ioEx)
                            {
                                Debug.WriteLine($"Error deleting thumbnail file {thumbnailPath}: {ioEx.Message}");
                            }
                        }
                    }
                }
                else
                {
                    bitmapImage = CreateBitmapImageFromPath(filePath); // Handle DNG files
                }
            }
            catch (NotSupportedException notSupEx)
            {
                Debug.WriteLine($"Error generating thumbnail for {filePath}: {notSupEx.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error generating thumbnail for {filePath}: {ex.Message}");
            }

            // Use placeholder image if no thumbnail is available
            return bitmapImage ?? LoadPlaceholderImage;
        }

        private static BitmapImage LoadPlaceholderImage
        {
            get
            {
                string placeholderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "images", "placeholder.png");
                var bitmapImage = CreateBitmapImageFromPath(placeholderPath);

                bitmapImage ??= CreateFallbackImage();

                return bitmapImage;
            }
        }

        private static BitmapImage CreateFallbackImage()
        {
        
                // Create a 1x1 pixel bitmap with a solid color (e.g., transparent)
                Bitmap bmp = new(1, 1);
                bmp.SetPixel(0, 0, System.Drawing.Color.Transparent); // Set the pixel to transparent

            using var memoryStream = new MemoryStream();
            // Save the bitmap to the memory stream in PNG format
            bmp.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);

            // Create a new BitmapImage and load it from the memory stream
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = new MemoryStream(memoryStream.ToArray());
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze(); // Freeze to make it cross-thread accessible

            return bitmapImage;
        }
        private static BitmapImage? CreateBitmapImageFromPath(string path)
        {

            BitmapImage bitmapImage = new();
            try
            {
                bitmapImage.BeginInit();
                bitmapImage.DecodePixelWidth = 70;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.UriSource = new Uri(path, UriKind.Absolute);
                bitmapImage.EndInit();
                bitmapImage.Freeze();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error generating BitmapImage from path {path}: {ex.Message}");
                return null;
            }
            return bitmapImage;
        }

        private void OnListViewItemLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.ListViewItem listViewItem)
            {
                if (listViewItem.DataContext is FileItem fileItem && !fileItem.HasAnimationPlayed)
                {
                    fileItem.HasAnimationPlayed = true;

                    var fadeInStoryboard = (Storyboard)FindResource("FadeInStoryboard");
                    var zoomInStoryboard = (Storyboard)FindResource("ZoomInStoryboard");

                    fadeInStoryboard.Begin(listViewItem);
                    zoomInStoryboard.Begin(listViewItem);
                }
            }
        }
        private void ShowLoadingSpinner(bool isVisible)
        {
            loadingSpinner.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BackgroundWorker_DoWork(object? sender, DoWorkEventArgs e)
        {

            if (e.Argument is not List<string> files)
            {
                return;
            }

            int totalFiles = files.Count;

            for (int index = 0; index < totalFiles; index++)
            {
                if (backgroundWorker.CancellationPending)
                {
                    e.Cancel = true;
                    break;
                }

                var filePath = files[index];

                try
                {
                    ProcessFile(filePath);
                    int progressPercentage = (int)((index + 1) / (double)totalFiles * 100);
                    backgroundWorker.ReportProgress(progressPercentage);
                }
                catch (Exception ex)
                {
                    backgroundWorker.ReportProgress(-1, ex.Message);
                }
            }
        }
        private void BackgroundWorker_ProgressChanged(object? sender, ProgressChangedEventArgs e)
        {
            if (e.ProgressPercentage >= 0)
            {
                // The target value to reach
                double targetValue = e.ProgressPercentage;

                // The current value of the progress bar
                double currentValue = progressBar.Value;

                // Total duration for the animation
                Duration animationDuration = new(TimeSpan.FromMilliseconds(350));

                // Create the animation for the progress bar value
                DoubleAnimation progressBarAnimation = new()
                {
                    From = currentValue,
                    To = targetValue,
                    Duration = animationDuration,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } // Smooth easing
                };

                // Create a storyboard to apply the animation
                Storyboard storyboard = new();
                storyboard.Children.Add(progressBarAnimation);

                // Set the target of the animation
                Storyboard.SetTarget(progressBarAnimation, progressBar);
                Storyboard.SetTargetProperty(progressBarAnimation, new PropertyPath(System.Windows.Controls.ProgressBar.ValueProperty));

                // Start the animation
                storyboard.Begin();

                // Ensure the progress bar is visible
                progressBar.Visibility = Visibility.Visible;
                RecalculateFirstColumnWidth();
            }
            else
            {
                // Handle case where e.UserState is not a string
                if (e.UserState is string errorMessage)
                {
                    System.Windows.MessageBox.Show($"Error: {errorMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    // Handle unexpected type or null case
                    System.Windows.MessageBox.Show("An unknown error occurred.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BackgroundWorker_RunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                ShowCustomMessageBox("", "Processing was cancelled.");
            }
            else if (e.Error != null)
            {
                System.Windows.MessageBox.Show($"An error occurred: {e.Error.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                // After the processing is done, animate the progress bar to 100% if not already there
                if (progressBar.Value < 100)
                {
                    AnimateProgressBarToCompletion();
                }
                else
                {
                    // If already at 100%, hide the progress bar directly
                    HideProgressBar();
                }
                string message = hasProcessingErrors
                    ? "Processing completed with some errors. Please check the status for details."
                    : "Enjoy your new Film simulations!";

                ShowCustomMessageBox("Processing completed", message);

            }

            processButton.Content = "Process";

            foreach (var item in Files.Where(f => f.Status == "Processing"))
            {
                item.Status = e.Cancelled ? "Pending" : "✔";
            }

            UpdateButtonState(); // Update button state after processing

            hasProcessingErrors = false; // Reset the error flag
        }


        private string? ConvertToDng(string filePath)
        {
            string dngFilePath = System.IO.Path.ChangeExtension(filePath, ".dng");

            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string dngLabPath = System.IO.Path.Combine(exeDirectory, "libs", "dnglab.exe");

            if (!File.Exists(dngLabPath))
            {
                UpdateFileItem(filePath, "DNGLab executable not found");
                hasProcessingErrors = true;
                return null;
            }

            try
            {
                UpdateFileItem(filePath, "Converting to DNG");
                var processInfo = new ProcessStartInfo
                {
                    FileName = dngLabPath,
                    Arguments = $"convert  --embed-raw false \"{filePath}\" \"{dngFilePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);

                // Ensure the process started successfully
                if (process == null)
                {
                    UpdateFileItem(filePath, "Failed to start the conversion process.");
                    hasProcessingErrors = true;
                    return null;
                }

                // Ensure the standard output and error are not null
                using var outputReader = process.StandardOutput ?? throw new InvalidOperationException("StandardOutput is null");
                using var errorReader = process.StandardError ?? throw new InvalidOperationException("StandardError is null");

                var output = outputReader.ReadToEnd();
                var error = errorReader.ReadToEnd();
                process.WaitForExit();

                // Check for specific error message in the output
                if (output.Contains("ERROR") || !File.Exists(dngFilePath))
                {
                    string errorMessage = !string.IsNullOrEmpty(error) ? error : "Unknown error during conversion.";
                    UpdateFileItem(filePath, "There was a conversion error, perform the conversion manually or refer to the help page.");
                    hasProcessingErrors = true;
                    return null;
                }
                else
                {
                    // Conversion successful
                    UpdateFileItem(filePath, "Conversion successful");
                }
            }
            catch (Exception ex)
            {
                UpdateFileItem(filePath, $"Exception during conversion: {ex.Message}");
                hasProcessingErrors = true;
                return null;
            }

            return dngFilePath;
        }

        private void ProcessFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                Dispatcher.Invoke(() => UpdateFileItem(filePath, "Invalid file path."));
                hasProcessingErrors = true;
                return;
            }

            // Retrieve the value of customArgumentToggle on the UI thread
            bool isCustomArgumentsEnabled = false;
            Dispatcher.Invoke(() =>
            {
                isCustomArgumentsEnabled = customArgumentToggle.IsChecked == true;
            });

            string? dngFilePath = filePath; // Allow dngFilePath to be nullable
            bool isConvertedDng = filePath.EndsWith(".dng", StringComparison.OrdinalIgnoreCase);
            bool hasBeenConvertedDng = false;

            if (!isConvertedDng)
            {
                // Convert to DNG if it's not already a DNG
                dngFilePath = ConvertToDng(filePath);

                // Check if conversion failed
                if (dngFilePath == null)
                {
                    Dispatcher.Invoke(() => UpdateFileItem(filePath, "Failed to convert to DNG."));
                    hasProcessingErrors = true;
                    return;
                }
                else
                {
                    hasBeenConvertedDng = true;
                }
            }

            string arguments;
            if (!string.IsNullOrEmpty(outputFolderPath))
            {
                string outputFilePath = System.IO.Path.Combine(outputFolderPath, System.IO.Path.GetFileNameWithoutExtension(dngFilePath) + "_processed.dng");

                // Check if the output file already exists and delete it if necessary
                if (System.IO.File.Exists(outputFilePath))
                {
                    try
                    {
                        System.IO.File.Delete(outputFilePath);
                        Debug.WriteLine($"Deleted existing output file: {outputFilePath}");
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => UpdateFileItem(filePath, $"Failed to delete existing output file: {ex.Message}"));
                        hasProcessingErrors = true;
                        return;
                    }
                }

                arguments = $"-CameraProfilesMake=\"FUJIFILM\" -CameraProfilesModel=\"X-T5\" -CameraProfilesUniqueCameraModel=\"Fujifilm X-T5\" -CameraProfilesCameraRawProfile=\"True\" -UniqueCameraModel=\"Fujifilm X-T5\" -o \"{outputFilePath}\" \"{dngFilePath}\"";
                Debug.WriteLine($" {arguments}");

            }
            else
            {
                // Use default arguments if custom arguments are not enabled and no output path is set
                if (!isCustomArgumentsEnabled && string.IsNullOrEmpty(outputFolderPath) && !hasBeenConvertedDng)
                {
                    arguments = $"{defaultArguments} \"{dngFilePath}\"";
                }
                else
                {
                    arguments = $"{currentArguments} \"{dngFilePath}\"";
                }
            }

            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string exifToolPath = System.IO.Path.Combine(exeDirectory, "libs", "exiftool.exe");

            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = exifToolPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);

                if (process == null)
                {
                    Dispatcher.Invoke(() => UpdateFileItem(filePath, "Failed to start the process."));
                    hasProcessingErrors = true;
                    return;
                }

                using var outputReader = process.StandardOutput;
                using var errorReader = process.StandardError;
                var output = outputReader.ReadToEnd();
                var error = errorReader.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    if (output.Contains("image files updated") || output.Contains("image files created"))
                    {
                        Dispatcher.Invoke(() => UpdateFileItem(filePath, "✔"));
                        Debug.WriteLine($"converted : {hasBeenConvertedDng} path  {outputFolderPath}");

                        // Check if the file was converted to DNG and an output path is set
                        if (hasBeenConvertedDng && !string.IsNullOrEmpty(outputFolderPath))
                        {
                            // Generate the temporary file path
                            string temporaryFilePath = System.IO.Path.ChangeExtension(dngFilePath, ".dng_original");

                            // Delete the original DNG file and temporary file after processing
                            try
                            {
                                if (System.IO.File.Exists(dngFilePath))
                                {
                                    System.IO.File.Delete(dngFilePath);
                                    Debug.WriteLine($"Deleted original DNG file: {dngFilePath}");
                                }

                                if (System.IO.File.Exists(temporaryFilePath))
                                {
                                    System.IO.File.Delete(temporaryFilePath);
                                    Debug.WriteLine($"Deleted temporary file: {temporaryFilePath}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Dispatcher.Invoke(() => UpdateFileItem(filePath, $"Failed to delete files: {ex.Message}"));
                                hasProcessingErrors = true;
                            }
                        }
                    }
                    else
                    {
                        Dispatcher.Invoke(() => UpdateFileItem(filePath, "Error writing the metadata"));
                        hasProcessingErrors = true;
                    }
                }
                else
                {
                    Dispatcher.Invoke(() => UpdateFileItem(filePath, $"Error: {error}"));
                    hasProcessingErrors = true;
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => UpdateFileItem(filePath, $"Exception: {ex.Message}"));
                hasProcessingErrors = true;
            }
        }

        private void UpdateFileItem(string filePath, string status)
        {
            Dispatcher.Invoke(() =>
            {
                var fileItem = Files.FirstOrDefault(f => f.FilePath == filePath);
                if (fileItem != null)
                {
                    fileItem.Status = status;

                    // Force the ListView to refresh
                    listView.Items.Refresh();
                }
                else
                {
                }
            });
        }


        private void ProcessButton_Click(object sender, RoutedEventArgs e)
        {
            if (backgroundWorker.IsBusy)
            {
                if (processButton.Content.ToString() == "Stop")
                {
                    backgroundWorker.CancelAsync();
                    processButton.Content = "Stopping...";
                    
                }
                return;
            }
           
            progressBar.Visibility = Visibility.Visible;
            processButton.Content = "Stop";

            var filePaths = Files.Where(f => f.Status == "Pending").Select(f => f.FilePath).ToList();

            if (filePaths.Count == 0)
            {
                System.Windows.MessageBox.Show("No files to process.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                processButton.Content = "Process";
                UpdateButtonState();

                return;
            }
            
            backgroundWorker.RunWorkerAsync(filePaths);
            UpdateButtonState();
        }
        private void ColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is GridViewColumnHeader header)
            {
                Sort(header);
            }
        }

        private void Sort(GridViewColumnHeader header)
        {
            var column = header.Column as GridViewColumn;
            if (column == null) return;

            // Determine the sort direction
            var direction = ListSortDirection.Ascending;
            if (_lastHeaderClicked == header)
            {
                // If the same header is clicked again, reverse the direction
                direction = _lastDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            }

            _lastHeaderClicked = header;
            _lastDirection = direction;

            // Get the sort by property name from the column's binding
            string sortBy = null;
            if (column.DisplayMemberBinding is System.Windows.Data.Binding wpfBinding)
            {
                sortBy = wpfBinding.Path.Path;
            }
            else if (column.DisplayMemberBinding is MultiBinding multiBinding)
            {
                // Handle MultiBinding if needed
                sortBy = string.Join(", ", multiBinding.Bindings.Select(b => (b as System.Windows.Data.Binding)?.Path.Path));
            }

            // Apply sorting
            var view = CollectionViewSource.GetDefaultView(listView.ItemsSource);
            view.SortDescriptions.Clear();
            if (sortBy != null)
            {
                view.SortDescriptions.Add(new SortDescription(sortBy, direction));
                view.Refresh();
            }
        }
        private void ListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var listView = sender as System.Windows.Controls.ListView;

            if (listView?.ContextMenu == null)
                return;

            // Enable or disable the MenuItem based on whether a ListViewItem is selected
            bool hasSelectedItem = listView.SelectedItem != null;

            foreach (MenuItem menuItem in listView.ContextMenu.Items)
            {
                // Enable or disable based on the presence of a selected item
                menuItem.IsEnabled = hasSelectedItem;
            }
        }
        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (listView.SelectedItem is FileItem selectedFile)
            {
                // Find the ListViewItem corresponding to the selectedFile

                if (listView.ItemContainerGenerator.ContainerFromItem(selectedFile) is System.Windows.Controls.ListViewItem listViewItem)
                {
                    // Find the fade-out storyboard
                    var storyboard = (Storyboard)FindResource("FadeOutStoryboard");

                    // Apply the storyboard to the ListViewItem
                    Storyboard.SetTarget(storyboard, listViewItem);
                    Storyboard.SetTargetProperty(storyboard, new PropertyPath("Opacity"));

                    // Remove the selected file after the animation completes
                    storyboard.Completed += (s, args) =>
                    {
                        Files.Remove(selectedFile);
                        listView.UpdateLayout();
                        UpdateButtonState();

                        // Optionally, delete the file from the filesystem
                        // File.Delete(selectedFile.FilePath);
                    };

                    // Start the storyboard
                    storyboard.Begin();
                }
                else
                {
                    // If the ListViewItem is not found (e.g., it may not be realized yet), remove the file immediately
                    Files.Remove(selectedFile);
                    listView.UpdateLayout();
                    UpdateButtonState();
                    UpdateTextBlockVisibility();
                }
            }
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (listView.SelectedItem is FileItem selectedItem) // Replace 'YourFileItemType' with your actual item type
            {
                try
                {
                    // Use the file path associated with the selected item
                    string filePath = selectedItem.FilePath;
                    Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Failed to open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (listView.SelectedItem is FileItem selectedItem) // Replace 'YourFileItemType' with your actual item type
            {
                try
                {
                    // Use the file path associated with the selected item
                    string filePath = selectedItem.FilePath;
                    string ?folderPath = System.IO.Path.GetDirectoryName(filePath);

                    if (folderPath != null)
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", folderPath) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Failed to open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {

            if (listView.SelectedItem is FileItem selectedItem)
            {
                try
                {
                    string filePath = selectedItem.FilePath;
                    Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Failed to open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private void OpenFileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new()
            {
                Filter = "DNG files (*.dng)|*.dng|All files (*.*)|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                AddFiles(openFileDialog.FileNames);
            }
        }

        private void OpenFolderMenuItem_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog folderBrowserDialog = new();

            if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedPath = folderBrowserDialog.SelectedPath;

                // Call AddDngFilesFromFolder to handle adding all supported raw files and DNG files
                AddDngFilesFromFolder(selectedPath);
            }

        }
        private void HelpMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://ipweb.dev/",
                UseShellExecute = true
            });
        }

        private void UpdateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://your-update-link.com",
                UseShellExecute = true
            });
        }

        private void ReportBugMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "mailto:contact@ipweb.dev",
                UseShellExecute = true
            });
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowAboutWindow();
        }
        private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select a folder to save the processed images";
            dialog.ShowNewFolderButton = true;

            var result = dialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                string formattedPath = dialog.SelectedPath.Replace(@"\", "  >  ").Replace(":  >  ", "  >  ");
                SelectedFolderTextBox.Text = formattedPath;
                outputFolderPath = dialog.SelectedPath;
            }
        }

        private async void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if there are items to clear
            if (Files.Count > 0)
            {
                // Create a list to keep track of all ListViewItems
                var listViewItems = new List<System.Windows.Controls.ListViewItem>();

                // Iterate through the collection to gather all ListViewItems
                foreach (var item in Files.ToList())
                {
                    if (listView.ItemContainerGenerator.ContainerFromItem(item) is System.Windows.Controls.ListViewItem listViewItem)
                    {
                        listViewItems.Add(listViewItem);
                    }
                }

                // Find the storyboard
                var storyboard = (Storyboard)FindResource("FadeOutStoryboard");

                // Apply the storyboard to all ListViewItems
                foreach (var listViewItem in listViewItems)
                {
                    Storyboard.SetTarget(storyboard, listViewItem);
                    Storyboard.SetTargetProperty(storyboard, new PropertyPath("Opacity"));
                    storyboard.Begin();
                }
                HideProgressBar();
                // Wait for the animation to complete
                await Task.Delay(500); // Match this duration with the storyboard's duration

                // Remove all items from the collection
                Files.Clear();

                // Ensure the ListView updates and button states are refreshed
                listView.UpdateLayout();
                UpdateButtonState();
                UpdateTextBlockVisibility();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
        private void UpdateButtonState()
        {
            // Enable the 'Clear' button if there are files in the list

            if (backgroundWorker.IsBusy)
            {
                clearButton.IsEnabled = false;
            }
            else
            {
                clearButton.IsEnabled = Files.Count > 0;
            }

            // Enable the 'Process' button if there are files with the status "Pending"
            processButton.IsEnabled = Files.Any(f => f.Status == "Pending");
        }

        private void UpdateTextBlockVisibility()
        {
            var fadeOutStoryboard = (Storyboard)FindResource("FadeOutStoryboard2");



            if (DragDropTextBlock.Visibility == Visibility.Visible && !isTextBlockHidden)
            {
                // Stop the current animation if any
                fadeOutStoryboard.Stop();
                isTextBlockHidden = true;
                fadeOutStoryboard.Begin();
                fadeOutStoryboard.Completed += (s, e) =>
                {
                    DragDropTextBlock.Visibility = Visibility.Collapsed;
                };
            }


        }
        private void ToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            currentArguments = customArguments;
        }

        private void ToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            currentArguments = defaultArguments;
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Get the storyboard from resources
            Storyboard fadeOutStoryboard = (Storyboard)this.Resources["FadeOutStoryboard"];
            Storyboard.SetTarget(fadeOutStoryboard, this);
            Storyboard.SetTargetProperty(fadeOutStoryboard, new PropertyPath("Opacity"));

            // Hook up an event handler to close the window after the animation completes
            fadeOutStoryboard.Completed += (s, args) =>
            {
                this.Close(); // Close the current window

                // Ensure that the application is fully shut down after the window closes
                System.Windows.Application.Current.Shutdown();
            };

            // Start the fade-out animation
            fadeOutStoryboard.Begin();
        }

        private async void ShowCustomMessageBox(string title, string message)
        {
            if (isDialogOpen)
            {
                // If a dialog is already open, exit early
                return;
            }

            try
            {
                // Set the flag to true to indicate that the dialog is open
                isDialogOpen = true;

                // Create an instance of the custom MessageBox
                var view = new CustomMessageBox(title, message);

                // Show the custom MessageBox and wait for the result
                var result = await MaterialDesignThemes.Wpf.DialogHost.Show(view, "RootDialog");

                // Check if result is not null and handle accordingly
                if (result is bool boolResult)
                {
                    if (boolResult)
                    {
                        // OK was pressed
                        return;
                    }
                    else
                    {
                        // Cancel was pressed
                        return;
                    }
                }
                else
                {
                    // Handle unexpected result type or null result
                    // You might want to log an error or handle this case appropriately
                    Debug.WriteLine("Unexpected result or result is null.");
                }
            }
            finally
            {
                // Ensure that the flag is reset when the dialog is closed
                isDialogOpen = false;
            }
        }
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Get the storyboard from resources
            Storyboard fadeOutStoryboard = (Storyboard)this.Resources["FadeOutStoryboard"];
            Storyboard.SetTarget(fadeOutStoryboard, this);
            Storyboard.SetTargetProperty(fadeOutStoryboard, new PropertyPath("Opacity"));

            // Hook up an event handler to close the window after the animation completes
            fadeOutStoryboard.Completed += (s, args) =>
            {
                this.Close(); // Close the current window

                // Ensure that the application is fully shut down after the window closes
                System.Windows.Application.Current.Shutdown();
            };

            // Start the fade-out animation
            fadeOutStoryboard.Begin();
        }
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
        private void CustomTitleBar_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            TitleBarContextMenu.PlacementTarget = sender as UIElement;
            TitleBarContextMenu.IsOpen = true;
        }
        // Command to move the window
        private void Move_Click(object sender, RoutedEventArgs e)
        {
            // Attach mouse event handlers
            this.MouseLeftButtonDown += Window_MouseLeftButtonDown;
            this.CaptureMouse();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                // Release mouse events
                this.MouseLeftButtonDown -= Window_MouseLeftButtonDown;
                this.ReleaseMouseCapture();

                // Begin window drag operation
                this.DragMove();
            }
        }

        // Command to resize the window
        private void Resize_Click(object sender, RoutedEventArgs e)
        {
            WindowInteropHelper helper = new WindowInteropHelper(this);
            SendMessage(helper.Handle, WM_SYSCOMMAND, (IntPtr)SC_SIZE, IntPtr.Zero); // Triggers window resizing
        }

        // P/Invoke declarations for resizing
        private const int WM_SYSCOMMAND = 0x112;
        private const int SC_SIZE = 0xF000;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    
        private void Files_CollectionChanged(object?  sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RecalculateFirstColumnWidth();

        }

        private void RecalculateFirstColumnWidth()
        {
            if (listView.View is GridView gridView)
            {
                double remainingWidth = listView.ActualWidth;
                double minThumbColumnWidth = 85; // Minimum width for the thumbnail column
                double minWidthForOtherColumns = 110; // Minimum width for other columns
                double pictureColumnWidth = 110;

                // Ensure that the picture column has at least the minimum width
                _ = Math.Max(pictureColumnWidth, minWidthForOtherColumns);
                remainingWidth -= minThumbColumnWidth;

                // Iterate through columns to find the other columns
                foreach (var column in gridView.Columns)
                {
                    if (column != gridView.Columns[0]) // Exclude the thumbnail column
                    {
                        double maxContentWidth = 0;

                        // Create a temporary TextBlock to measure the width required by the content
                        var textBlock = new TextBlock { Text = "Conversion successful" }; // Dummy text

                        // Get the maximum width required for the column content
                        foreach (var item in listView.Items)
                        {
                            // Adjust as needed to get the content for measurement

                            if (item is FileItem data) // Check if data is not null
                            {
                                // Set the text for each column's data
                                if (column == gridView.Columns[1]) // Adjust as needed
                                {
                                    textBlock.Text = data.FileName ?? string.Empty; // Handle potential null FileName
                                }
                                else if (column == gridView.Columns[2]) // Adjust as needed
                                {
                                    textBlock.Text = data.CameraMakeAndModel ?? string.Empty; // Handle potential null CameraMakeAndModel
                                }
                                else if (column == gridView.Columns[3]) // Adjust as needed
                                {
                                    textBlock.Text = data.Status ?? string.Empty;
                                    textBlock.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));                                    
                                    //Debug.WriteLine($"status w: {textBlock.DesiredSize.Width}; status text : {textBlock.Text}");
                                }

                                // Measure the width required by the text
                                textBlock.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                                maxContentWidth = Math.Max(maxContentWidth, textBlock.DesiredSize.Width );
                            }
                        }
                        if (column == gridView.Columns[2]) 
                        {
                            column.Width = Math.Max(maxContentWidth + 45, minWidthForOtherColumns); // Add padding as needed, e.g., 20 pixels

                        }
                        else  
                        {
                            column.Width = Math.Max(maxContentWidth + 35, minWidthForOtherColumns);
                        }
                        
                        remainingWidth -= column.Width;
                        //Debug.WriteLine($"Column w : {colsize} remaining : {remW}");
                    }
                }
                if (remainingWidth > 80)
                {
                    gridView.Columns[1].Width += (remainingWidth / 2) - 40;
                    gridView.Columns[2].Width += (remainingWidth / 2) - 40;

                }
                
            }
        }
        private void AnimateProgressBarToCompletion()
        {
            // Animate the progress bar to 100%
            DoubleAnimation progressBarAnimation = new()
            {
                From = progressBar.Value,
                To = 100,
                Duration = new Duration(TimeSpan.FromMilliseconds(350)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } // Smooth easing
            };

            Storyboard storyboard = new();
            storyboard.Children.Add(progressBarAnimation);
            Storyboard.SetTarget(progressBarAnimation, progressBar);
            Storyboard.SetTargetProperty(progressBarAnimation, new PropertyPath(System.Windows.Controls.ProgressBar.ValueProperty));

            // Hide the progress bar after the animation completes
            storyboard.Completed += (s, e) => HideProgressBar();

            storyboard.Begin();
        }
        private async void HideProgressBar()
        {
            await Task.Delay(500);
            // Start the scale animation
            var storyboard = (Storyboard)FindResource("CollapseProgressBarStoryboard");
            storyboard.Begin(progressBarContainer);
            // Wait for the animation to complete
            await Task.Delay(300); // Match this to your animation duration

            // Hide the ProgressBar after the animation completes
            progressBar.Visibility = Visibility.Collapsed;
            progressBar.Value = 0; // Reset the progress bar value

           
        }

       
        private static void ShowAboutWindow()
        {
            var flowDocument = CreateAboutFlowDocument();
            var aboutControl = new AboutUserControl("About FUJIFY", flowDocument);

            // Assuming DialogHost is used to display the UserControl
            DialogHost.Show(aboutControl, "RootDialog");
        }

        private static FlowDocument CreateAboutFlowDocument()
        {
            var flowDocument = new FlowDocument();
            var paragraph = new Paragraph();

            // Define colors
            var brushConverter = new BrushConverter();
            var defaultLinkColor = (System.Windows.Media.Brush?)brushConverter.ConvertFrom("#d5d5d5"); // light Grey
            var hoverLinkColor = (System.Windows.Media.Brush?)brushConverter.ConvertFrom("#006400");   // Green

            // Fallback to a default color if conversion returns null
            defaultLinkColor ??= System.Windows.Media.Brushes.Gray;
            hoverLinkColor ??= System.Windows.Media.Brushes.Green;

            // Add text and hyperlinks
            AddInline(paragraph, "FUJIFY ", null);
            AddNewLine(paragraph);
            AddInline(paragraph, "Version 1.0.0 Beta 1", null);
            AddNewLine(paragraph);
            AddNewLine(paragraph);
            AddInline(paragraph, "Developed by: Isidore Paulin", null);
            AddNewLine(paragraph);

            AddHyperlink(paragraph, "Website", new Uri("https://ipweb.dev"), defaultLinkColor, hoverLinkColor);
            AddNewLine(paragraph);
            AddHyperlink(paragraph, "Instagram", new Uri("https://www.instagram.com/isi.do.re/"), defaultLinkColor, hoverLinkColor);
            AddNewLine(paragraph);
            AddNewLine(paragraph);

            AddInline(paragraph, "Credits: ", null);
            AddNewLine(paragraph);

            AddHyperlink(paragraph, "ExifTool", new Uri("https://exiftool.org/"), defaultLinkColor, hoverLinkColor);
            AddInline(paragraph, " by Phil Harvey ", null);
            AddNewLine(paragraph);

            AddHyperlink(paragraph, "DNGLab", new Uri("https://github.com/dnglab/dnglab"), defaultLinkColor, hoverLinkColor);
            AddNewLine(paragraph);

            AddHyperlink(paragraph, "LibRaw Library", new Uri("https://www.libraw.org/"), defaultLinkColor, hoverLinkColor);
            AddInline(paragraph, " by LibRaw LLC ", null);
            AddNewLine(paragraph);
            AddNewLine(paragraph);

            AddInline(paragraph, "Buy me a coffee: ", null);
            AddNewLine(paragraph);

            AddHyperlink(paragraph, "PayPal", new Uri("https://paypal.me/isidorepaulin1?country.x=FR&locale.x=fr_FR"), defaultLinkColor, hoverLinkColor);
            AddNewLine(paragraph);
            AddInline(paragraph, "USDT (BSC): 0xac487782e8a66d21d1fd099dda1872ee23376f6e", null);

            flowDocument.Blocks.Add(paragraph);
            return flowDocument;
        }

#nullable enable
        private static void AddInline(Paragraph paragraph, string text, System.Windows.Media.Brush? color)
        {
            var run = new Run(text);
            if (color != null)
            {
                run.Foreground = color;
            }
            paragraph.Inlines.Add(run);
        }
#nullable disable
        private static void AddNewLine(Paragraph paragraph)
        {
            paragraph.Inlines.Add(new LineBreak());
        }

        private static void AddHyperlink(Paragraph paragraph, string text, Uri navigateUri,
            System.Windows.Media.Brush defaultColor, System.Windows.Media.Brush hoverColor)
        {
            var hyperlink = new Hyperlink(new Run(text))
            {
                NavigateUri = navigateUri,
                Foreground = defaultColor,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            hyperlink.MouseEnter += (s, e) => hyperlink.Foreground = hoverColor;
            hyperlink.MouseLeave += (s, e) => hyperlink.Foreground = defaultColor;

            paragraph.Inlines.Add(hyperlink);
        }
    }
    



}