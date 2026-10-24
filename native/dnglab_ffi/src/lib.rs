// SPDX-License-Identifier: LGPL-2.1
// Copyright 2024 Fujifier Project / Isidore Paulin

use std::ffi::CStr;
use std::fs::File;
use std::io::BufWriter;
use std::os::raw::c_char;
use std::path::PathBuf;
use rawler::dng::convert::{ConvertParams, convert_raw_file};
use rawler::decoders::supported_extensions;

/// Returns the library version string.
#[unsafe(no_mangle)]
pub extern "C" fn dnglab_version() -> *const c_char {
    c"0.8.0-ffi".as_ptr()
}

/// Checks if an extension (e.g. "raf", "arw", "cr2") is supported by the rawler engine.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn dnglab_is_extension_supported(ext_ptr: *const c_char) -> bool {
    if ext_ptr.is_null() {
        return false;
    }
    let ext_str = match unsafe { CStr::from_ptr(ext_ptr) }.to_str() {
        Ok(s) => s.trim_start_matches('.').to_lowercase(),
        Err(_) => return false,
    };

    let supported = supported_extensions();
    supported.iter().any(|&e| e.eq_ignore_ascii_case(&ext_str))
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
            drop(writer);
            let _ = std::fs::remove_file(&out_path);
            set_error(error_msg_buf, error_msg_buf_len, &format!("Raw decode failed: {e}"));
            -7
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
