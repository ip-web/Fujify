using System;
using System.Threading.Tasks;
using Fujify.ViewModels;
using Xunit;

namespace Fujify.Tests
{
    public class BackNavigationTests
    {
        [Fact]
        public void HandleBackAction_WhenPreferencesModalOpen_ClosesPreferencesAndReturnsTrue()
        {
            var vm = new MainViewModel();
            vm.ShowPreferencesModal();

            Assert.True(vm.IsPreferencesModal, "Preferences modal should be open.");

            bool handled = vm.HandleBackAction();

            Assert.True(handled, "HandleBackAction should return true when closing Settings view.");
            Assert.False(vm.IsPreferencesModal, "Preferences modal should now be closed.");
            Assert.False(vm.IsModalOpen, "No modal should remain open.");
        }

        [Fact]
        public void HandleBackAction_WhenAboutModalOpen_ClosesAboutAndReturnsTrue()
        {
            var vm = new MainViewModel();
            vm.ShowAboutModal();

            Assert.True(vm.IsAboutModal, "About modal should be open.");

            bool handled = vm.HandleBackAction();

            Assert.True(handled, "HandleBackAction should return true when closing About view.");
            Assert.False(vm.IsAboutModal, "About modal should now be closed.");
            Assert.False(vm.IsModalOpen, "No modal should remain open.");
        }

        [Fact]
        public void HandleBackAction_WhenOnMainViewWithNothingOpen_ReturnsFalse()
        {
            var vm = new MainViewModel();
            vm.CloseModal();
            vm.IsMobileSidebarOpen = false;
            vm.IsMobileAddMenuOpen = false;

            bool handled = vm.HandleBackAction();

            Assert.False(handled, "HandleBackAction should return false on the root main screen so app can prompt exit confirmation.");
        }

        [Fact]
        public void HandleBackAction_WhenExitConfirmationModalOpen_DismissesConfirmationAndReturnsTrue()
        {
            var vm = new MainViewModel();
            var task = vm.ShowConfirmationModalAsync("Exit Fujifier", "Are you sure?");

            Assert.True(vm.IsConfirmationModal, "Confirmation modal should be open.");

            bool handled = vm.HandleBackAction();

            Assert.True(handled, "HandleBackAction should return true when dismissing an open confirmation dialog.");
            Assert.False(vm.IsConfirmationModal, "Confirmation modal should be dismissed.");
            Assert.False(vm.IsModalOpen, "Modal should be closed.");
        }

        [Fact]
        public void HandleBackAction_WhenDrawerOrAddMenuOpen_ClosesThemAndReturnsTrue()
        {
            var vm = new MainViewModel();

            // Mobile sidebar (options drawer)
            vm.IsMobileSidebarOpen = true;
            bool handledDrawer = vm.HandleBackAction();
            Assert.True(handledDrawer);
            Assert.False(vm.IsMobileSidebarOpen);

            // Mobile add menu (FAB)
            vm.IsMobileAddMenuOpen = true;
            bool handledAddMenu = vm.HandleBackAction();
            Assert.True(handledAddMenu);
            Assert.False(vm.IsMobileAddMenuOpen);

            // Multi-select mode
            vm.IsMultiSelectMode = true;
            bool handledMulti = vm.HandleBackAction();
            Assert.True(handledMulti);
            Assert.False(vm.IsMultiSelectMode);
        }
    }
}

