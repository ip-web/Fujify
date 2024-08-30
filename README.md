After getting all my photography gear stolen and not having the funds and time to buy back on Fujifilm, I settled down to buying used Sony gear. I immediately missed the Fuji film simulations which had become one of the pillars of my editing process. That's what motivated me to develop this program to bring back some Fujifilm magic into my shots. After some trials and errors, I'm glad to present you with Fujify.   

# FUJIFY - Fujifilm Film Simulation Unlocker
Unlock the Fujifilm Film Simulations for any camera's RAW files in Lightroom. This program allows you to convert DNG ( or any other RAW files with conversion ) from other camera brands to be compatible with Fujifilm's film simulation profiles.

## Features

- **Convert RAW to DNG**: Supports various RAW file formats and converts them to DNG to enable film simulations.
- **Uses Powerful Tools**: Built using ExifTool, LibRaw, and DNGLab for reliable and efficient conversion.
- **Easy to Use**: Drag and drop files or folders, or use the context menu and toolbar options to add files.
- **Windows Only**: Currently available for Windows users.

## How to Use

1. **Add Files**: Drag and drop files or folders into the app window, or use the context menu or toolbar to add files and folders.
2. **Choose Destination Folder**: Select a folder where the processed files will be saved. If no folder is selected, the processed files will be saved in the same location as the originals.
   - Note: If the "Backup Original" option is not selected and no destination folder is set, the original DNG files will be overwritten.
3. **Start Processing**: Click the "Process" button to begin processing your files.
   
## Bug Reports

The app is currently in **beta stage**, so there may be bugs. You are welcome to report any issues you encounter! When submitting a bug report, please include:

- The RAW files that caused the issue.
- Any error messages.
- Screenshots (if relevant).

Please report it [here](https://github.com/delasalle-sio-paulin-i/Fujify/issues) with as much detail as possible.

## Downloads

- Installer (recommended)
- Portable ZIP File
See the [Release](https://github.com/delasalle-sio-paulin-i/Fujify/releases) page to download the latest version.

## FAQ

**Q: Does it work with Capture One or other editors?**  
**A:** Currently, only Adobe Lightroom is supported. I do not own or use Capture One or other editors. If there is enough interest, I might look into adding support, but I can't guarantee its feasibility.

**Q: What RAW files are currently supported?**  
**A:** Here is a list of currently supproted RAW files for direct use in Fujifier "ARI, CR3, CR2, CRW, ERF, RAF, 3FR, KDC, DCS, DCR, IIQ, MOS, MEF, MRW, NEF, NRW, ORF, RW2, PEF, IIQ, SRW, ARW, SRF, SR2" any RAW file converted to DNG should be compatible. For a list of supported camera you can refere to [DNGLab supported camera list](https://github.com/dnglab/dnglab/blob/main/SUPPORTED_CAMERAS.md)

**Q: The conversion of my RAW file failed.**  
**A:** The conversion might fail if the RAW file has been altered in any way, or if your camera is not supported by the program, which can happen with newly released models. If any problem arises during conversion, please try converting to DNG beforehand using software that supports your camera.

**Q: After conversion, the DNG file looks different from the original RAW file.**  
**A:** In most cases, the DNG and original should look identical. However, if you notice discrepancies, converting to DNG manually beforehand is advised. Additionally, when opening the processed file in Lightroom, it might automatically apply the Fujifilm Provia Standard profile, causing the image to look different from the original. To compare accurately, set both images to the same profile (e.g., Adobe Color Standard).

**Q: What software can I use to convert my RAW files to DNG ?**  
**A:** I've had successful results converting my RAW file with export to DNG and advanced NR / enhanced details in Lightroom and from DxO PureRAW 3. You can try and report here with other software conversion results.

**Q: Can you add support for X camera or brand?**  
**A:** RAW conversion is done through third-party libraries. I will try to update them as often as possible to keep the app compatible with the majority of cameras, but I cannot add compatibility myself.

**Q: Will I get the same result as shooting with a Fujifilm camera?**  
**A:** The profiles are the same that are used on the Fujifilm camera in Lightroom, but keep in mind that these profile are tuned for Fujifilm sensors and color science, the result seems pretty accurate in my experience with Sony A7RII files, but I was not able to do any direct comparison. The experience of shooting Fujifilm also goes much further than just the film simulations with their unique layout and handling and that's something no software trickery can achieve. I highly recommend anyone that have the opportunity to try it out as it's been a real pleasure to shoot with Fuji for many years and I can't wait to get back at it.

## License

This project is licensed under the GNU General Public License v3.0. See the [LICENSE](LICENSE) file for details.
ExifTool is licenced under the Perl Artistic Licence
DNGLab is licensed under LGPL2.1
LibRaw is licensed under LGPL2

## Contributing

Contributions are welcome! I'm more of a Web Developer myself and I'm sure a lot can be improved, feel free to submit your proposals with some explanation (remember I'm relatively new to C# and .NET).
I'm also taking feature requests and will try implementing them as much as time and skills allow me to.

## Credits

This app could not have been made without the incredible work of:

- **Phil Harvey**, creator of [ExifTool](https://exiftool.org/)
- **Iliah Borg**, creator of [LibRaw](https://www.libraw.org/)
- [**DNGLab**](https://github.com/dnglab/dnglab/tree/main)
- All the contributors to these projects

## Donations

I made this project on my own and spent a considerable amount of time doing so, I provide it for free and open source for anyone to use, so if you find this program useful, consider supporting my work. As a freelance developer donations are greatly appreciated and help me continue to improve and maintain this project.

- [**PayPal**](https://www.paypal.com/donate/?hosted_button_id=7UJ9B3LBLTN4J)
- USDT (BSC): 0xac487782e8a66d21d1fd099dda1872ee23376f6e

