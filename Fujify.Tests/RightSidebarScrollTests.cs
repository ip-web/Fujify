using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Fujify.ViewModels;
using Fujify.Views;
using Xunit;

namespace Fujify.Tests
{
    [Collection("UI")]
    public class RightSidebarScrollTests
    {
        [Fact]
        public void RightSidebar_ShouldBeScrollableAndScrollBarVisible_OnReducedWindowHeight()
        {
            try { Program.BuildAvaloniaApp().SetupWithoutStarting(); } catch {}

            var vm = new MainViewModel();
            vm.CreateSubfolder = true;
            vm.SubfolderUseOriginalName = true;

            var view = new MainView { DataContext = vm };
            var window = new Window { Content = view, Width = 1000, Height = 500 };
            
            window.Measure(new Size(1000, 500));
            window.Arrange(new Rect(0, 0, 1000, 500));

            var scrollViewer = view.FindControl<ScrollViewer>("InspectorScrollViewer");
            Assert.NotNull(scrollViewer);
            Assert.True(scrollViewer.ScrollBarMaximum.Y > 0, "Inspector ScrollViewer should have positive scrollable height when window is short.");

            // Verify outer vertical ScrollBar is present and visible
            var enumerator = scrollViewer.GetVisualChildren().GetEnumerator();
            Assert.True(enumerator.MoveNext());
            var outerGrid = Assert.IsType<Grid>(enumerator.Current);
            
            ScrollBar? verticalScrollBar = null;
            foreach (var child in outerGrid.GetVisualChildren())
            {
                if (child is ScrollBar sb && sb.Orientation == Orientation.Vertical)
                {
                    verticalScrollBar = sb;
                    break;
                }
            }
            Assert.NotNull(verticalScrollBar);
            Assert.True(verticalScrollBar.IsVisible, "Outer vertical scrollbar should be visible when content overflows.");

            // Test pointer wheel event on inspector border forwards to scrollviewer
            var inspectorBorder = view.FindControl<Border>("InspectorPanelBorder");
            Assert.NotNull(inspectorBorder);

            var pointer = new Pointer(1, PointerType.Mouse, true);
            var wheelArgs = new PointerWheelEventArgs(
                inspectorBorder,
                pointer,
                view,
                new Point(10, 10),
                0,
                new PointerPointProperties(),
                KeyModifiers.None,
                new Vector(0, -1) // Scroll down
            )
            {
                RoutedEvent = InputElement.PointerWheelChangedEvent
            };

            inspectorBorder.RaiseEvent(wheelArgs);
            Assert.True(wheelArgs.Handled, "Wheel event on inspector border should be handled.");
            Assert.True(scrollViewer.Offset.Y > 0, "ScrollViewer Offset.Y should increase after scroll down.");
        }
    }
}

