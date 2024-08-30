# FUJIFY - Fujifilm Film Simulation Unlocker
After getting all my photography gear stolen and not having the funds and time to buy back on Fujifilm, I settled down on buying used Sony gear. I immediately missed the film simulations which had become one of the pillars of my editing process. That's what motivated in developing this program to get back some Fujifilm magic. After some trials and errors I'm glad to present you Fujify.   
Unlock the Fujifilm Film Simulations for any camera's RAW files in Lightroom. This program allows you to convert DNG ( or any other RAW files with conversion ) from other camera brands to be compatible with Fujifilm's film simulation profiles.

## Features

- **Convert RAW to DNG**: Supports various RAW file formats and converts them to DNG to enable film simulations.
- **Uses Powerful Tools**: Built using ExifTool, LibRaw, and DNGLab for reliable and efficient conversion.
- **Easy to Use**: Drag and drop files or folders, or use the context menu and toolbar options to add files.
- **Windows Only**: Currently available for Windows users.

## How to Use

1. **Add Files**: Drag and drop files or folders into the app window, or use the context menu or toolbar to add files and folders.
2. **Choose Destination Folder**: Select a folder where the processed files will be saved. If no folder is selected, the processed files will be saved in the same location as the originals.
   - Note: If the "Backup Original" option is not selected and not destination folder is set, the original DNG files will be overwritten.
3. **Start Processing**: Click the "Process" button to begin processing your files.
   
## Beta Notice

The app is currently in **beta stage**, so there may be bugs. You are welcome to report any issues you encounter! When submitting a bug report, please include:

- The RAW files that caused the issue.
- Any error messages.
- Screenshots (if relevant).
  
## Downloads

- Installer (recommended)
- Portable ZIP File
See the [Release](#) page to download the latest version.

## FAQ

**Q: Does it work with Capture One or other editors?**  
**A:** Currently, only Adobe Lightroom is supported. I do not own or use Capture One. If there is enough interest, I might look into adding support, but I can't guarantee its feasibility.

**Q: The conversion of my RAW file failed.**  
**A:** The conversion might fail if the RAW file has been altered in any way, or if your camera is not supported by the program, which can happen with newly released models. If any problem arises during conversion, please try converting to DNG beforehand using software that supports your camera.

**Q: After conversion, the DNG file looks different from the original RAW file.**  
**A:** In most cases, the DNG and original should look identical. However, if you notice discrepancies, converting to DNG manually beforehand is advised. Additionally, when opening the processed file in Lightroom, it might automatically apply the Fujifilm Provia Standard profile, causing the image to look different from the original. To compare accurately, set both images to the same profile (e.g., Adobe Color Standard).

**Q: Can you add support for X camera or brand?**  
**A:** RAW conversion is done through third-party libraries. I will try to update them as often as possible to keep the app compatible with the majority of cameras, but I cannot add compatibility myself.

**Q: Will I get the same result as shooting with a Fujifilm camera?**  
**A:** The profiles are the same that are used on the Fujifilm camera in Lightroom, but keep in mind that these profile are tuned for Fujifilm sensors and color science, the result seems pretty accurate in my experience with Sony A7RII files, but I was not able to do any direct comparison. The experience of shooting Fujifilm also goes much further than just the film simulations with their unique layout and handling and that's something no software tricks can achieve. I highly recommend anyone to try it out as it's been a real pleasure to shoot with Fuji for many years and I can't wait to get back to it.

## License

This project is licensed under the GNU General Public License v3.0. See the [LICENSE](LICENSE) file for details.
ExifTool is licenced under the Perl Artistic Licence
DNGLab
LibRaw is licensed under LGPL2

## Contributing

Contributions are welcome! I'm more of Web Developer myslef and I'm sure a lot of improvements can be made to this app, feel free to submit you commits with some explanation (remember I'm pretty new to C# and .NET).

## Bug Reports

Found a bug? Please report it [here](#) with as much detail as possible.

## Credits

This app could not have been made without the incredible work of:

- **Phil Harvey**, creator of ExifTool
- **Iliah Borg**, creator of LibRaw
- **The DNGLab Project**
- All the contributors to these projects

## Donations

I made this project on my own and spent a considerable amount of time doing so, I provide it for free and open source for anyone to use so if you find this app helpful, consider supporting my work. As a freelance developer donations are greatly appreciated and help me continue to improve and maintain the app.
