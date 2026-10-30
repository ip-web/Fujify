using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Fujify.ViewModels;
using Fujify.Views;
using Xunit;
using Xunit.Abstractions;

namespace Fujify.Tests
{
    [Collection("UI")]
    public class ScreenshotVerificationTests
    {
        private readonly ITestOutputHelper _output;

        public ScreenshotVerificationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void TestRenderScreenshot()
        {
            try { Program.BuildAvaloniaApp().SetupWithoutStarting(); } catch {}

            var vm = new MainViewModel();
            vm.CreateSubfolder = true;
            vm.SubfolderUseOriginalName = true;
            vm.AutoOpenOutputFolder = true;

            var view = new MainView { DataContext = vm };
            var window = new Window { Content = view, Width = 1000, Height = 620 };
            
            window.Measure(new Size(1000, 620));
            window.Arrange(new Rect(0, 0, 1000, 620));
            try { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } catch { }

            var scrollViewer = view.FindControl<ScrollViewer>("InspectorScrollViewer");
            Assert.NotNull(scrollViewer);

            _output.WriteLine($"Before scroll: Extent={scrollViewer.Extent}, Viewport={scrollViewer.Viewport}, Max={scrollViewer.ScrollBarMaximum}");

            var scp = scrollViewer.Presenter as Avalonia.Controls.Presenters.ScrollContentPresenter;

            double maxY = scrollViewer.ScrollBarMaximum.Y;
            if (maxY <= 0)
            {
                maxY = scrollViewer.Extent.Height - scrollViewer.Viewport.Height;
            }
            _output.WriteLine($"Scrolling to maxY={maxY}");

            scrollViewer.Offset = new Vector(0, maxY);
            if (scp != null)
            {
                scp.Offset = new Vector(0, maxY);
                if (scp.Child is Visual childVisual)
                {
                    childVisual.RenderTransform = new Avalonia.Media.TranslateTransform(0, -maxY);
                }
            }

            try { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } catch { }
            window.Measure(new Size(1000, 620));
            window.Arrange(new Rect(0, 0, 1000, 620));
            try { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } catch { }

            var inspectorBorder = view.FindControl<Border>("InspectorPanelBorder");
            var footer = view.FindControl<Border>("DesktopBottomStatusBar");
            _output.WriteLine($"Footer Bounds: {footer?.Bounds}");
            _output.WriteLine($"View Bounds: {view.Bounds}");
            _output.WriteLine($"InspectorPanelBorder Bounds: {inspectorBorder?.Bounds}");
            _output.WriteLine($"InspectorScrollViewer Bounds: {scrollViewer?.Bounds}");
            _output.WriteLine($"ScrollContentPresenter Bounds: {scp?.Bounds}");

            foreach (var tb in view.GetVisualDescendants())
            {
                if (tb is TextBlock textBlock && textBlock.Text != null && 
                    (textBlock.Text.Contains("Open folder") || textBlock.Text.Contains("Save in subfolder") || textBlock.Text.Contains("Backup original") || textBlock.Text.Contains("Keep original")))
                {
                    if (inspectorBorder != null)
                    {
                        var pt = textBlock.TranslatePoint(new Point(0, 0), inspectorBorder);
                        _output.WriteLine($"TextBlock '{textBlock.Text}' point in InspectorPanelBorder: {pt}, Bounds={textBlock.Bounds}");
                    }
                }
            }

            string outputPath = @"C:\Users\isisk\.gemini\antigravity\brain\c46d5a16-3ee0-4345-84e3-d6ee5f489f1c\sidebar_scrolled_to_end.png";
            using var rtb = new RenderTargetBitmap(new PixelSize(1000, 620), new Vector(96, 96));
            rtb.Render(view);
            rtb.Save(outputPath);

            // Crop the Inspector region from sidebar_scrolled_to_end.png using SkiaSharp
            using (var stream = File.OpenRead(outputPath))
            using (var skBitmap = SkiaSharp.SKBitmap.Decode(stream))
            {
                var origin = inspectorBorder?.TranslatePoint(new Point(0, 0), view) ?? new Point(680, 70);
                int cropX = (int)Math.Max(0, origin.X - 8);
                int cropY = (int)Math.Max(0, origin.Y - 8);
                int cropW = (int)Math.Min(skBitmap.Width - cropX, (inspectorBorder?.Bounds.Width ?? 295) + 16);
                int cropH = (int)Math.Min(skBitmap.Height - cropY, (inspectorBorder?.Bounds.Height ?? 450) + 16);

                var cropRect = new SkiaSharp.SKRectI(cropX, cropY, cropX + cropW, cropY + cropH);
                using var cropped = new SkiaSharp.SKBitmap(cropRect.Width, cropRect.Height);
                skBitmap.ExtractSubset(cropped, cropRect);

                string cropPath = @"C:\Users\isisk\.gemini\antigravity\brain\c46d5a16-3ee0-4345-84e3-d6ee5f489f1c\sidebar_scrolled_inspector_crop.png";
                using var outStream = File.OpenWrite(cropPath);
                cropped.Encode(outStream, SkiaSharp.SKEncodedImageFormat.Png, 100);
                _output.WriteLine($"Saved zoomed crop to {cropPath}");
            }

            _output.WriteLine($"Saved screenshot to {outputPath}, size: {new FileInfo(outputPath).Length} bytes");
            Assert.True(File.Exists(outputPath));
        }

        [Fact]
        public void TestRenderNewPhotoTableLayout()
        {
            try { Program.BuildAvaloniaApp().SetupWithoutStarting(); } catch {}

            var vm = new MainViewModel();
            var view = new MainView { DataContext = vm };
            var window = new Window { Content = view, Width = 1000, Height = 620 };
            
            window.Measure(new Size(1000, 620));
            window.Arrange(new Rect(0, 0, 1000, 620));
            try { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } catch { }

            string outputPath = @"C:\Users\isisk\.gemini\antigravity\brain\c46d5a16-3ee0-4345-84e3-d6ee5f489f1c\photo_table_layout.png";
            using var rtb = new RenderTargetBitmap(new PixelSize(1000, 620), new Vector(96, 96));
            rtb.Render(view);
            rtb.Save(outputPath);

            // Crop the photo table area
            using (var stream = File.OpenRead(outputPath))
            using (var skBitmap = SkiaSharp.SKBitmap.Decode(stream))
            {
                // Crop the photo table: X=60, Y=100, Width=610, Height=420
                var cropRect = new SkiaSharp.SKRectI(65, 95, 680, 520);
                using var cropped = new SkiaSharp.SKBitmap(cropRect.Width, cropRect.Height);
                skBitmap.ExtractSubset(cropped, cropRect);

                string cropPath = @"C:\Users\isisk\.gemini\antigravity\brain\c46d5a16-3ee0-4345-84e3-d6ee5f489f1c\photo_table_crop.png";
                using var outStream = File.OpenWrite(cropPath);
                cropped.Encode(outStream, SkiaSharp.SKEncodedImageFormat.Png, 100);
                _output.WriteLine($"Saved photo table crop to {cropPath}");
            }

            Assert.True(File.Exists(outputPath));
        }

        [Fact]
        public void TestRenderPopulatedPhotoTableLayout()
        {
            try { Program.BuildAvaloniaApp().SetupWithoutStarting(); } catch {}

            var vm = new MainViewModel();
            vm.Files.Add(new Models.FileItem
            {
                FilePath = @"C:\Photos\DSCF0001.RAF",
                CameraMakeAndModel = "FUJIFILM X-T5",
                Status = "Pending"
            });
            vm.Files.Add(new Models.FileItem
            {
                FilePath = @"C:\Photos\DSCF0002.RAF",
                CameraMakeAndModel = "FUJIFILM X-T5",
                Status = "Pending"
            });
            vm.SelectedFile = vm.Files[0];

            var view = new MainView { DataContext = vm };
            var window = new Window { Content = view, Width = 1000, Height = 620 };
            
            window.Measure(new Size(1000, 620));
            window.Arrange(new Rect(0, 0, 1000, 620));
            try { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } catch { }

            string outputPath = @"C:\Users\isisk\.gemini\antigravity\brain\c46d5a16-3ee0-4345-84e3-d6ee5f489f1c\photo_table_populated.png";
            using var rtb = new RenderTargetBitmap(new PixelSize(1000, 620), new Vector(96, 96));
            rtb.Render(view);
            rtb.Save(outputPath);

            Assert.True(File.Exists(outputPath));
        }

        [Fact]
        public void TestRenderSmallScreenQueueFabClosed()
        {
            try { Program.BuildAvaloniaApp().SetupWithoutStarting(); } catch {}

            var vm = new MainViewModel();
            vm.Files.Add(new Models.FileItem
            {
                FilePath = @"C:\Photos\DSCF0001.RAF",
                CameraMakeAndModel = "FUJIFILM X-T5",
                Status = "Pending"
            });
            vm.Files.Add(new Models.FileItem
            {
                FilePath = @"C:\Photos\DSCF0002.RAF",
                CameraMakeAndModel = "FUJIFILM X-T5",
                Status = "Pending"
            });
            vm.SelectedFile = vm.Files[0];

            var view = new MainView { DataContext = vm };
            var window = new Window { Content = view, Width = 800, Height = 600 };
            
            vm.UpdateLayoutMode(800, 600);
            window.Measure(new Size(800, 600));
            window.Arrange(new Rect(0, 0, 800, 600));
            try { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } catch { }

            string outputPath = @"C:\Users\isisk\.gemini\antigravity\brain\c46d5a16-3ee0-4345-84e3-d6ee5f489f1c\queue_fab_closed.png";
            using var rtb = new RenderTargetBitmap(new PixelSize(800, 600), new Vector(96, 96));
            rtb.Render(view);
            rtb.Save(outputPath);

            Assert.True(File.Exists(outputPath));
        }

        [Fact]
        public void TestRenderSmallScreenQueueFabOpened()
        {
            try { Program.BuildAvaloniaApp().SetupWithoutStarting(); } catch {}

            var vm = new MainViewModel();
            vm.Files.Add(new Models.FileItem
            {
                FilePath = @"C:\Photos\DSCF0001.RAF",
                CameraMakeAndModel = "FUJIFILM X-T5",
                Status = "Pending"
            });
            vm.Files.Add(new Models.FileItem
            {
                FilePath = @"C:\Photos\DSCF0002.RAF",
                CameraMakeAndModel = "FUJIFILM X-T5",
                Status = "Pending"
            });
            vm.SelectedFile = vm.Files[0];

            var view = new MainView { DataContext = vm };
            var window = new Window { Content = view, Width = 800, Height = 600 };
            
            vm.UpdateLayoutMode(800, 600);
            vm.IsQueueFabMenuOpen = true;

            window.Measure(new Size(800, 600));
            window.Arrange(new Rect(0, 0, 800, 600));
            try { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } catch { }

            string outputPath = @"C:\Users\isisk\.gemini\antigravity\brain\c46d5a16-3ee0-4345-84e3-d6ee5f489f1c\queue_fab_opened.png";
            using var rtb = new RenderTargetBitmap(new PixelSize(800, 600), new Vector(96, 96));
            rtb.Render(view);
            rtb.Save(outputPath);

            Assert.True(File.Exists(outputPath));
        }

        [Fact]
        public void TestRenderPhotoInfo90PercentPreview()
        {
            try { Program.BuildAvaloniaApp().SetupWithoutStarting(); } catch {}

            var vm = new MainViewModel();
            var fileItem = new Models.FileItem
            {
                FilePath = @"C:\Photos\DSCF9999.RAF",
                CameraMakeAndModel = "FUJIFILM X-T5",
                Status = "Pending",
                Aperture = "f/2.8",
                ShutterSpeed = "1/250s",
                Iso = "160",
                FocalLength = "35mm"
            };

            using (var ms = new MemoryStream())
            {
                using (var skBmp = new SkiaSharp.SKBitmap(600, 400))
                using (var canvas = new SkiaSharp.SKCanvas(skBmp))
                {
                    canvas.Clear(new SkiaSharp.SKColor(40, 80, 130));
                    using var paint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White, TextSize = 32 };
                    canvas.DrawText("Fujifilm RAW Preview", 80, 200, paint);
                    skBmp.Encode(ms, SkiaSharp.SKEncodedImageFormat.Png, 100);
                }
                ms.Position = 0;
                fileItem.Thumbnail = new Avalonia.Media.Imaging.Bitmap(ms);
            }

            vm.Files.Add(fileItem);
            vm.SelectedFile = fileItem;
            vm.InspectorTabIndex = 1; // Photo info tab

            var view = new MainView { DataContext = vm };
            var window = new Window { Content = view, Width = 1000, Height = 650 };
            
            vm.UpdateLayoutMode(1000, 650);
            window.Measure(new Size(1000, 650));
            window.Arrange(new Rect(0, 0, 1000, 650));
            try { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } catch { }

            string outputPath = @"C:\Users\isisk\.gemini\antigravity\brain\c46d5a16-3ee0-4345-84e3-d6ee5f489f1c\photo_info_90percent_preview.png";
            using var rtb = new RenderTargetBitmap(new PixelSize(1000, 650), new Vector(96, 96));
            rtb.Render(view);
            rtb.Save(outputPath);

            // Also crop the right sidebar photo info region
            using (var stream = File.OpenRead(outputPath))
            using (var skBitmap = SkiaSharp.SKBitmap.Decode(stream))
            {
                // Right sidebar is at X = 1000 - 295 - 24 = 681, Y = 100, Width = 295, Height = 500
                var cropRect = new SkiaSharp.SKRectI(675, 95, 985, 595);
                using var cropped = new SkiaSharp.SKBitmap(cropRect.Width, cropRect.Height);
                skBitmap.ExtractSubset(cropped, cropRect);

                string cropPath = @"C:\Users\isisk\.gemini\antigravity\brain\c46d5a16-3ee0-4345-84e3-d6ee5f489f1c\photo_info_sidebar_crop.png";
                using var outStream = File.OpenWrite(cropPath);
                cropped.Encode(outStream, SkiaSharp.SKEncodedImageFormat.Png, 100);
                _output.WriteLine($"Saved photo info sidebar crop to {cropPath}");
            }

            Assert.True(File.Exists(outputPath));
        }

        [Fact]
        public void TestRenderBreakLayoutCr2()
        {
            try { Program.BuildAvaloniaApp().SetupWithoutStarting(); } catch {}

            var vm = new MainViewModel();
            string rootDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\.."));
            string? cr2Path = Directory.GetFiles(rootDir, "*.CR2").FirstOrDefault();
            if (string.IsNullOrEmpty(cr2Path) || !File.Exists(cr2Path))
            {
                return;
            }

            var thumbService = new Services.ThumbnailService();
            var metaService = new Services.MetadataService();

            var fileItem = new Models.FileItem
            {
                FilePath = cr2Path,
                CameraMakeAndModel = metaService.GetCameraMakeAndModel(cr2Path),
                Thumbnail = thumbService.GenerateThumbnail(cr2Path),
                Status = "Pending"
            };

            _output.WriteLine($"Loaded break_layout.CR2: Camera={fileItem.CameraMakeAndModel}, Thumbnail={fileItem.Thumbnail?.Size.Width}x{fileItem.Thumbnail?.Size.Height}");

            vm.Files.Add(fileItem);
            vm.SelectedFile = fileItem;
            vm.InspectorTabIndex = 1;

            var view = new MainView { DataContext = vm };
            var window = new Window { Content = view, Width = 1000, Height = 650 };
            
            vm.UpdateLayoutMode(1000, 650);
            window.Measure(new Size(1000, 650));
            window.Arrange(new Rect(0, 0, 1000, 650));
            try { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } catch { }

            string outputPath = @"C:\Users\isisk\.gemini\antigravity\brain\c46d5a16-3ee0-4345-84e3-d6ee5f489f1c\break_layout_render.png";
            using var rtb = new RenderTargetBitmap(new PixelSize(1000, 650), new Vector(96, 96));
            rtb.Render(view);
            rtb.Save(outputPath);

            // Crop sidebar
            using (var stream = File.OpenRead(outputPath))
            using (var skBitmap = SkiaSharp.SKBitmap.Decode(stream))
            {
                var cropRect = new SkiaSharp.SKRectI(675, 95, 985, 595);
                using var cropped = new SkiaSharp.SKBitmap(cropRect.Width, cropRect.Height);
                skBitmap.ExtractSubset(cropped, cropRect);

                string cropPath = @"C:\Users\isisk\.gemini\antigravity\brain\c46d5a16-3ee0-4345-84e3-d6ee5f489f1c\break_layout_sidebar_crop.png";
                using var outStream = File.OpenWrite(cropPath);
                cropped.Encode(outStream, SkiaSharp.SKEncodedImageFormat.Png, 100);
            }

            Assert.True(File.Exists(outputPath));
        }
    }
}
