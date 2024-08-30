# FUJIFY - Fujifilm Film Simulation Unlocker
Unlock the Fujifilm Film Simulations for any camera's RAW files in Adobe Lightroom. This app allows you to convert DNG files ( or any other RAW with conversion ) from other camera brands to be compatible with Fujifilm's film simulation modes.

## Features

- **Convert RAW to DNG**: Supports various RAW file formats and converts them to DNG to enable film simulations.
- **Uses Powerful Tools**: Built using ExifTool, LibRaw, and DNGRaw for reliable and efficient conversion.
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

- [Installer](#) (recommended)
- [Portable ZIP File](#)

## FAQ

**Q: Does it work with Capture One or other editors?**  
**A:** Currently, only Adobe Lightroom is supported. I do not own or use Capture One or other editors. If there is enough interest, I might look into adding support, but I can't guarantee its feasibility.

**Q: The conversion of my RAW file failed.**  
**A:** The conversion might fail if the RAW file has been altered in any way, or if your camera is not supported by the program, which can happen with newly released models. If any problem arises during conversion, please try converting to DNG beforehand using software that supports your camera.

**Q: After conversion, the DNG file looks different from the original RAW file.**  
**A:** In most cases, the DNG and original should look identical. However, if you notice discrepancies, it is advised to convert to DNG manually beforehand. Additionally, when opening the processed file in Lightroom, it might automatically apply the Fujifilm Provia Standard profile, causing the image to look different from the original. To compare accurately, set both images to the same profile (e.g., Adobe Color Standard).

**Q: Can you add support for X camera or brand?**  
**A:** RAW conversion is done through third-party libraries. I will try to update them as often as possible to keep the app compatible with the majority of cameras, but I cannot add compatibility myself.

## License

This project is licensed under the [MIT License](LICENSE).

## Contributing

Contributions are welcome! I'm more of Web Developer myslef and I'm sure a lot of improvements can be made to this app, feel free to submit you commits with some explanation (remember I'm pretty new to C# and .NET).

## Bug Reports

Found a bug? Please report it [here](#) with as much detail as possible.


## Credits

This app could not have been made without the incredible work of:

- **Phil Harvey**, creator of ExifTool
- **Iliah Borg**, creator of LibRaw
- **The DNGRaw Project**
- All the contributors to these projects

## Donations

I made this project on my own and spent a considerable amount of time doing it, I provide it for free and completely open source so if you find this app helpful, consider supporting my work. Donations are greatly appreciated and help me continue to improve and maintain the app.
