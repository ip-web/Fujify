using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Fujify.Services;
using Xunit;

namespace Fujify.Tests
{
    public class MacOsDngLabPathResolutionTests
    {
        [Fact]
        public void FindDngLabPath_ShouldFindBundledOrSystemBinary()
        {
            // Use reflection to access private FindDngLabPath method
            var method = typeof(RawConverterService).GetMethod("FindDngLabPath", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            string? path = method.Invoke(null, null) as string;

            // In our build environment, libs/osx-arm64/dnglab was bundled, or system path exists
            if (path != null)
            {
                Assert.True(File.Exists(path), $"Resolved path '{path}' must exist.");
            }
        }

        [Fact]
        public void MissingDngLabErrorMessage_ShouldContainMacOsGuidance()
        {
            var method = typeof(RawConverterService).GetMethod("GetMissingDngLabErrorMessage", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            string? message = method.Invoke(null, null) as string;
            Assert.NotNull(message);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Contains("brew install dnglab", message);
                Assert.Contains("cargo install dnglab", message);
            }
            else
            {
                Assert.Contains("DNGLab", message);
            }
        }

        [Fact]
        public void BundledMacBinary_ShouldExistInProjectLibs()
        {
            string baseDir = AppContext.BaseDirectory;
            // The build output should contain libs/osx-arm64/dnglab
            string macBinary = Path.Combine(baseDir, "libs", "osx-arm64", "dnglab");
            Assert.True(File.Exists(macBinary), $"Bundled macOS ARM64 dnglab binary should be present at: {macBinary}");
        }

        [Fact]
        public void IsExecutableCompatibleWithCurrentArchitecture_ShouldAcceptShellScripts()
        {
            var method = typeof(RawConverterService).GetMethod("IsExecutableCompatibleWithCurrentArchitecture", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            string tempScript = Path.Combine(Path.GetTempPath(), $"script_{Guid.NewGuid():N}.sh");
            try
            {
                File.WriteAllText(tempScript, "#!/bin/sh\nexec dnglab \"$@\"\n");
                bool compatible = (bool)method.Invoke(null, new object[] { tempScript })!;
                Assert.True(compatible, "Shell script launcher should be considered compatible.");
            }
            finally
            {
                if (File.Exists(tempScript)) File.Delete(tempScript);
            }
        }

        [Fact]
        public async Task ConvertToDngAsync_WhenFileNotFound_HandlesGracefully()
        {
            var converter = new RawConverterService();
            string nonExistent = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.cr2");
            var result = await converter.ConvertToDngAsync(nonExistent, embedOriginalRaw: false);
            
            // Should not crash / throw unhandled exception
            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
        }
    }
}

