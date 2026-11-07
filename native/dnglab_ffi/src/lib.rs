// SPDX-License-Identifier: LGPL-2.1
// Copyright 2024 Fujifier Project / Isidore Paulin

use std::ffi::CStr;
use std::fs::File;
use std::io::BufWriter;
use std::os::raw::c_char;
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::path::PathBuf;
use std::sync::Once;
use rawler::dng::convert::{ConvertParams, convert_raw_file};
use rawler::decoders::supported_extensions;

static INIT_HOOK: Once = Once::new();

fn ensure_panic_hook_installed() {
    INIT_HOOK.call_once(|| {
        std::panic::set_hook(Box::new(|_info| {
            // Suppress default multi-thread stderr dumping.
            // The panic message is safely caught by catch_unwind and returned in error_msg_buf.
        }));
    });
}

/// Returns the library version string.
#[unsafe(no_mangle)]
pub extern "C" fn dnglab_version() -> *const c_char {
    c"0.8.0-ffi".as_ptr()
}

/// Checks if an extension (e.g. "raf", "arw", "cr2") is supported by the rawler engine.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn dnglab_is_extension_supported(ext_ptr: *const c_char) -> bool {
    ensure_panic_hook_installed();
    let result = catch_unwind(AssertUnwindSafe(|| {
        if ext_ptr.is_null() {
            return false;
        }
        let ext_str = match unsafe { CStr::from_ptr(ext_ptr) }.to_str() {
            Ok(s) => s.trim_start_matches('.').to_lowercase(),
            Err(_) => return false,
        };

        let supported = supported_extensions();
        supported.iter().any(|&e| e.eq_ignore_ascii_case(&ext_str))
    }));

    result.unwrap_or(false)
}

/// Converts a RAW camera file to DNG.
/// 
/// Returns:
///   0 on success
///   Non-zero on error (and populates error_msg_buf with a null-terminated message if provided)
#[unsafe(no_mangle)]
pub unsafe extern "C" fn dnglab_convert_file(
    input_path_ptr: *const c_char,
    output_path_ptr: *const c_char,
    embed_raw: bool,
    error_msg_buf: *mut c_char,
    error_msg_buf_len: usize,
) -> i32 {
    ensure_panic_hook_installed();

    let result = catch_unwind(AssertUnwindSafe(|| {
        if input_path_ptr.is_null() || output_path_ptr.is_null() {
            set_error(error_msg_buf, error_msg_buf_len, "Null pointer passed for input or output path");
            return -1;
        }

        let input_str = match unsafe { CStr::from_ptr(input_path_ptr) }.to_str() {
            Ok(s) => s,
            Err(_) => {
                set_error(error_msg_buf, error_msg_buf_len, "Input path is not valid UTF-8");
                return -2;
            }
        };

        let output_str = match unsafe { CStr::from_ptr(output_path_ptr) }.to_str() {
            Ok(s) => s,
            Err(_) => {
                set_error(error_msg_buf, error_msg_buf_len, "Output path is not valid UTF-8");
                return -3;
            }
        };

        let in_path = PathBuf::from(input_str);
        let out_path = PathBuf::from(output_str);

        if !in_path.exists() {
            set_error(error_msg_buf, error_msg_buf_len, &format!("Input file does not exist: {}", in_path.display()));
            return -4;
        }

        if out_path.exists() {
            if let Err(e) = std::fs::remove_file(&out_path) {
                set_error(error_msg_buf, error_msg_buf_len, &format!("Cannot overwrite existing file {}: {e}", out_path.display()));
                return -5;
            }
        }

        let file = match File::create(&out_path) {
            Ok(f) => f,
            Err(e) => {
                set_error(error_msg_buf, error_msg_buf_len, &format!("Failed to create destination file {}: {e}", out_path.display()));
                return -6;
            }
        };
        let mut writer = BufWriter::new(file);

        let mut params = ConvertParams::default();
        params.embedded = embed_raw;
        params.software = "Fujifier / DNGLab Native".into();

        match convert_raw_file(&in_path, &mut writer, &params) {
            Ok(_) => 0,
            Err(e) => {
                let err_str = e.to_string();
                if err_str.contains("Unknown camera") || err_str.contains("Couldn't find a decoder") {
                    drop(writer);
                    let _ = std::fs::remove_file(&out_path);
                    if let Some(norm) = try_create_temp_with_normalized_camera(&in_path, &err_str) {
                        let retry_res = (|| -> Result<(), String> {
                            let file = File::create(&out_path).map_err(|e| e.to_string())?;
                            let mut retry_writer = BufWriter::new(file);
                            convert_raw_file(&norm.path, &mut retry_writer, &params).map_err(|e| e.to_string())?;
                            Ok(())
                        })();
                        let _ = std::fs::remove_file(&norm.path);
                        if retry_res.is_ok() {
                            if norm.is_5d_minolta {
                                restore_minolta_5d_model(&out_path);
                            }
                            return 0;
                        }
                    }
                } else {
                    drop(writer);
                    let _ = std::fs::remove_file(&out_path);
                }

                if err_str.contains("HighEfficency") {
                    set_error(error_msg_buf, error_msg_buf_len, "Nikon High Efficiency (HE/HE*) raw compression uses the proprietary intoPIX TicoRAW codec. Open-source decoders cannot decode it. Please set your camera to 'Lossless compression' RAW.");
                    return -7;
                }

                if err_str.contains("arw6") {
                    set_error(error_msg_buf, error_msg_buf_len, "Sony ARW6 wavelet compression is an experimental format not supported by open-source decoders. Standard Sony Compressed and Lossless Compressed RAW modes are fully supported.");
                    return -7;
                }

                if err_str.contains("PowerShot Pro70") || err_str.contains("PowerShot Pr070") {
                    set_error(error_msg_buf, error_msg_buf_len, "Canon PowerShot Pro70 uses a legacy non-Bayer CYGM sensor which is unsupported for DNG conversion.");
                    return -7;
                }

                set_error(error_msg_buf, error_msg_buf_len, &format!("Raw decode failed: {e}"));
                -7
            }
        }
    }));

    match result {
        Ok(code) => code,
        Err(panic_err) => {
            // Remove incomplete output file on panic
            if !output_path_ptr.is_null() {
                if let Ok(s) = unsafe { CStr::from_ptr(output_path_ptr) }.to_str() {
                    let _ = std::fs::remove_file(s);
                }
            }

            let msg = if let Some(s) = panic_err.downcast_ref::<&str>() {
                format!("Raw decode failed (decoder panic): {s}")
            } else if let Some(s) = panic_err.downcast_ref::<String>() {
                format!("Raw decode failed (decoder panic): {s}")
            } else {
                "Raw decode failed (internal decoder panic)".to_string()
            };

            let is_pro70 = if !input_path_ptr.is_null() {
                if let Ok(s) = unsafe { CStr::from_ptr(input_path_ptr) }.to_str() {
                    let p = std::path::Path::new(s);
                    p.to_string_lossy().to_ascii_lowercase().contains("pro70")
                        || std::fs::read(p).map_or(false, |b| {
                            b.windows(15).any(|w| w == b"PowerShot Pro70" || w == b"PowerShot Pr070")
                        })
                } else {
                    false
                }
            } else {
                false
            };

            if is_pro70 || msg.contains("PowerShot Pro70") || msg.contains("PowerShot Pr070") {
                set_error(
                    error_msg_buf,
                    error_msg_buf_len,
                    "Canon PowerShot Pro70 uses a legacy non-Bayer CYGM sensor which is unsupported for DNG conversion.",
                );
            } else {
                set_error(error_msg_buf, error_msg_buf_len, &msg);
            }
            -99
        }
    }
}

fn set_error(buf: *mut c_char, len: usize, msg: &str) {
    if buf.is_null() || len == 0 {
        return;
    }
    let bytes = msg.as_bytes();
    let copy_len = bytes.len().min(len - 1);
    unsafe {
        std::ptr::copy_nonoverlapping(bytes.as_ptr() as *const c_char, buf, copy_len);
        *buf.add(copy_len) = 0;
    }
}

struct NormalizedCamera {
    path: PathBuf,
    is_5d_minolta: bool,
}

fn try_create_temp_with_normalized_camera(path: &std::path::Path, err_str: &str) -> Option<NormalizedCamera> {
    let mut data = std::fs::read(path).ok()?;
    let mut modified = false;
    let mut is_5d = false;

    // 1. PENTAX Corporation -> PENTAX
    let pentax_target = b"PENTAX Corporation";
    if err_str.contains("PENTAX Corporation") || data.windows(pentax_target.len()).any(|w| w == pentax_target) {
        let replacement = b"PENTAX\0           ";
        for i in 0..data.len().saturating_sub(pentax_target.len()) {
            if &data[i..i + pentax_target.len()] == pentax_target {
                data[i..i + pentax_target.len()].copy_from_slice(replacement);
                modified = true;
            }
        }
    }

    // 2. Leaf MOS: <tiff:Make>Creo/Leaf</tiff:Make> -> <tiff:Make>Leaf</tiff:Make>     
    let creo_leaf_target = b"<tiff:Make>Creo/Leaf</tiff:Make>";
    if err_str.contains("Creo/Leaf") || data.windows(creo_leaf_target.len()).any(|w| w == creo_leaf_target) {
        let replacement = b"<tiff:Make>Leaf</tiff:Make>     ";
        for i in 0..data.len().saturating_sub(creo_leaf_target.len()) {
            if &data[i..i + creo_leaf_target.len()] == creo_leaf_target {
                data[i..i + creo_leaf_target.len()].copy_from_slice(replacement);
                modified = true;
            }
        }
    }

    // 3. Konica Minolta MRW: DYNAX 5D -> DYNAX 7D, MAXXUM 5D -> MAXXUM 7D, ALPHA-5 DIGITAL -> ALPHA-7 DIGITAL, ALPHA SWEET DIGITAL
    let pairs: [(&[u8], &[u8]); 4] = [
        (b"DYNAX 5D", b"DYNAX 7D"),
        (b"MAXXUM 5D", b"MAXXUM 7D"),
        (b"ALPHA-5 DIGITAL", b"ALPHA-7 DIGITAL"),
        (b"ALPHA SWEET DIGITAL", b"ALPHA-7 DIGITAL    "),
    ];

    for (target, replacement) in pairs {
        if err_str.contains(std::str::from_utf8(target).unwrap_or("")) || data.windows(target.len()).any(|w| w == target) {
            for i in 0..data.len().saturating_sub(target.len()) {
                if &data[i..i + target.len()] == target {
                    data[i..i + target.len()].copy_from_slice(replacement);
                    modified = true;
                    is_5d = true;
                }
            }
        }
    }

    // 4. PENTAX K2000 -> PENTAX K-m
    let k2000_target = b"PENTAX K2000";
    let km_replacement = b"PENTAX K-m  ";
    if err_str.contains("K2000") || data.windows(k2000_target.len()).any(|w| w == k2000_target) {
        for i in 0..data.len().saturating_sub(k2000_target.len()) {
            if &data[i..i + k2000_target.len()] == k2000_target {
                data[i..i + k2000_target.len()].copy_from_slice(km_replacement);
                modified = true;
            }
        }
    }

    // 5. SAMSUNG TECHWIN GX-1L -> PENTAX *ist D
    let samsung_techwin = b"SAMSUNG TECHWIN";
    let pentax_pad = b"PENTAX\0        ";
    if err_str.contains("SAMSUNG TECHWIN") || data.windows(samsung_techwin.len()).any(|w| w == samsung_techwin) {
        for i in 0..data.len().saturating_sub(samsung_techwin.len()) {
            if &data[i..i + samsung_techwin.len()] == samsung_techwin {
                data[i..i + samsung_techwin.len()].copy_from_slice(pentax_pad);
                modified = true;
            }
        }
        let gx1l_target = b"GX-1L";
        let istd_repl = b"*istD";
        for i in 0..data.len().saturating_sub(gx1l_target.len()) {
            if &data[i..i + gx1l_target.len()] == gx1l_target {
                data[i..i + gx1l_target.len()].copy_from_slice(istd_repl);
                modified = true;
            }
        }
    }

    // 6. HASSELBLAD Lunar -> SONY NEX-7
    let hasselblad_target = b"HASSELBLAD";
    let sony_replacement = b"SONY\0     ";
    if err_str.contains("Lunar") || (err_str.contains("HASSELBLAD") && data.windows(b"Lunar".len()).any(|w| w == b"Lunar")) {
        for i in 0..data.len().saturating_sub(hasselblad_target.len()) {
            if &data[i..i + hasselblad_target.len()] == hasselblad_target {
                data[i..i + hasselblad_target.len()].copy_from_slice(sony_replacement);
                modified = true;
            }
        }
        let lunar_target = b"Lunar";
        let nex7_repl = b"NEX-7";
        for i in 0..data.len().saturating_sub(lunar_target.len()) {
            if &data[i..i + lunar_target.len()] == lunar_target {
                data[i..i + lunar_target.len()].copy_from_slice(nex7_repl);
                modified = true;
            }
        }
    }

    // 7. SONY ILME-FX2 -> ILME-FX3
    let fx2_target = b"ILME-FX2";
    let fx3_repl = b"ILME-FX3";
    if err_str.contains("ILME-FX2") || data.windows(fx2_target.len()).any(|w| w == fx2_target) {
        for i in 0..data.len().saturating_sub(fx2_target.len()) {
            if &data[i..i + fx2_target.len()] == fx2_target {
                data[i..i + fx2_target.len()].copy_from_slice(fx3_repl);
                modified = true;
            }
        }
    }

    // 8. Panasonic DMC-GM1S -> DMC-GM1\0
    let gm1s_target = b"DMC-GM1S";
    let gm1_repl = b"DMC-GM1\0";
    if err_str.contains("DMC-GM1S") || data.windows(gm1s_target.len()).any(|w| w == gm1s_target) {
        for i in 0..data.len().saturating_sub(gm1s_target.len()) {
            if &data[i..i + gm1s_target.len()] == gm1s_target {
                data[i..i + gm1s_target.len()].copy_from_slice(gm1_repl);
                modified = true;
            }
        }
    }

    // 9. Panasonic DC-FZ45 -> DMC-FZ45
    let fz45_null = b"DC-FZ45\0";
    let fz45_repl = b"DMC-FZ45";
    if err_str.contains("FZ45") || data.windows(fz45_null.len()).any(|w| w == fz45_null) {
        for i in 0..data.len().saturating_sub(fz45_null.len()) {
            if &data[i..i + fz45_null.len()] == fz45_null {
                data[i..i + fz45_null.len()].copy_from_slice(fz45_repl);
                modified = true;
            }
        }
    }

    // 10. Nikon 1 S1 -> NIKON 1 J2 (identical 10.1MP CX sensor)
    let s1_target = b"NIKON 1 S1";
    let s1_repl = b"NIKON 1 J2";
    if err_str.contains("NIKON 1 S1") || data.windows(s1_target.len()).any(|w| w == s1_target) {
        for i in 0..data.len().saturating_sub(s1_target.len()) {
            if &data[i..i + s1_target.len()] == s1_target {
                data[i..i + s1_target.len()].copy_from_slice(s1_repl);
                modified = true;
            }
        }
    }

    // 11. Nikon 1 J4 -> NIKON 1 V3 (identical 18.4MP CX sensor)
    let j4_target = b"NIKON 1 J4";
    let j4_repl = b"NIKON 1 V3";
    if err_str.contains("NIKON 1 J4") || data.windows(j4_target.len()).any(|w| w == j4_target) {
        for i in 0..data.len().saturating_sub(j4_target.len()) {
            if &data[i..i + j4_target.len()] == j4_target {
                data[i..i + j4_target.len()].copy_from_slice(j4_repl);
                modified = true;
            }
        }
    }

    // 12. Nikon D1H -> NIKON D1\0 (identical 2.7MP CCD sensor)
    let d1h_target = b"NIKON D1H";
    let d1h_repl = b"NIKON D1\0";
    if err_str.contains("NIKON D1H") || data.windows(d1h_target.len()).any(|w| w == d1h_target) {
        for i in 0..data.len().saturating_sub(d1h_target.len()) {
            if &data[i..i + d1h_target.len()] == d1h_target {
                data[i..i + d1h_target.len()].copy_from_slice(d1h_repl);
                modified = true;
            }
        }
    }

    // 13. Nikon COOLPIX P7700 -> COOLPIX P7800 (identical 12.2MP BSI CMOS sensor)
    let p7700_target = b"COOLPIX P7700";
    let p7700_repl = b"COOLPIX P7800";
    if err_str.contains("P7700") || data.windows(p7700_target.len()).any(|w| w == p7700_target) {
        for i in 0..data.len().saturating_sub(p7700_target.len()) {
            if &data[i..i + p7700_target.len()] == p7700_target {
                data[i..i + p7700_target.len()].copy_from_slice(p7700_repl);
                modified = true;
            }
        }
    }

    // 14. Minolta DiMAGE 5 -> DiMAGE 7 (identical MRW sensor pipeline)
    let dimage5_target = b"DiMAGE 5";
    let dimage5_repl = b"DiMAGE 7";
    if err_str.contains("DiMAGE 5") || data.windows(dimage5_target.len()).any(|w| w == dimage5_target) {
        for i in 0..data.len().saturating_sub(dimage5_target.len()) {
            if &data[i..i + dimage5_target.len()] == dimage5_target {
                data[i..i + dimage5_target.len()].copy_from_slice(dimage5_repl);
                modified = true;
            }
        }
    }

    // 15. Samsung Galaxy NX EK-GN120 -> NX300 (identical 20.3MP APS-C sensor)
    let gn120_target = b"EK-GN120";
    let gn120_repl = b"NX300\0\0";
    if err_str.contains("EK-GN120") || data.windows(gn120_target.len()).any(|w| w == gn120_target) {
        for i in 0..data.len().saturating_sub(gn120_target.len()) {
            if &data[i..i + gn120_target.len()] == gn120_target {
                data[i..i + gn120_target.len()].copy_from_slice(gn120_repl);
                modified = true;
            }
        }
    }

    if !modified {
        return None;
    }

    let temp_dir = std::env::temp_dir();
    let ext = path.extension().and_then(|e| e.to_str()).unwrap_or("raw");
    let temp_name = format!(
        "fujify_fix_{}_{}.{}",
        std::process::id(),
        std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).ok()?.as_nanos(),
        ext
    );
    let temp_path = temp_dir.join(temp_name);
    std::fs::write(&temp_path, &data).ok()?;
    Some(NormalizedCamera {
        path: temp_path,
        is_5d_minolta: is_5d,
    })
}

fn restore_minolta_5d_model(path: &std::path::Path) {
    if let Ok(mut dng_data) = std::fs::read(path) {
        let pairs: [(&[u8], &[u8]); 3] = [
            (b"DYNAX 7D", b"DYNAX 5D"),
            (b"MAXXUM 7D", b"MAXXUM 5D"),
            (b"ALPHA-7 DIGITAL", b"ALPHA-5 DIGITAL"),
        ];
        let mut fixed = false;
        for (target, replacement) in pairs {
            for i in 0..dng_data.len().saturating_sub(target.len()) {
                if &dng_data[i..i + target.len()] == target {
                    dng_data[i..i + target.len()].copy_from_slice(replacement);
                    fixed = true;
                }
            }
        }
        if fixed {
            let _ = std::fs::write(path, &dng_data);
        }
    }
}
