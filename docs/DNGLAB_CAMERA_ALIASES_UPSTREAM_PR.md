# DNGLab / rawler Upstream Camera Compatibility & Normalization Roadmap

This document maintains a comprehensive, actionable catalog of camera identification quirks, missing model aliases, and decoder routing omissions discovered during real-world RAW processing in **Fujify**.

The goal of this document is to prepare clean, targeted pull requests to upstream [dnglab/dnglab](https://github.com/dnglab/dnglab) and its underlying `rawler` raw-decoding crate so that these cameras are supported at the source without downstream byte-level workarounds.

---

## 📋 Catalog of Cameras & Required Upstream Changes

### 1. Pentax K2000 (North American Branding of Pentax K-m)
* **Raw Extension:** `.PEF`
* **Raw EXIF Header:** `Make: "PENTAX"`, `Model: "PENTAX K2000"`
* **Upstream Status:** In `rawler/data/cameras/Pentax/k-m.toml`, the primary model is defined as `PENTAX K-m`. The North American release was branded `PENTAX K2000`, causing `rawler` to throw `Unknown camera, model 'PENTAX K2000', make: 'PENTAX', mode: ''`.
* **Hardware:** Identical sensor (Sony ICX493AQA 10.2 MP CCD), identical active area and CFA pattern as the Pentax K-m.
* **Proposed Upstream PR:**
  In `rawler/data/cameras/Pentax/k-m.toml`, add `model_aliases`:
  ```toml
  model_aliases = [
    ["PENTAX K2000", "K2000"],
  ]
  ```

---

### 2. Konica Minolta ALPHA SWEET DIGITAL (Japanese Branding of Dynax 5D / Maxxum 5D)
* **Raw Extension:** `.MRW`
* **Raw EXIF Header:** `Make: "KONICA MINOLTA"`, `Model: "ALPHA SWEET DIGITAL"`
* **Upstream Status:** In `rawler/data/cameras/Minolta/dynax5d.toml`, existing model aliases cover European (`DYNAX 5D`) and North American (`MAXXUM 5D`) names, but omit the Japanese domestic market name `ALPHA SWEET DIGITAL`.
* **Hardware:** Identical 6.1 MP CCD sensor and MRW container as Dynax 5D / Maxxum 5D.
* **Proposed Upstream PR:**
  In `rawler/data/cameras/Minolta/dynax5d.toml`, append to `model_aliases`:
  ```toml
  model_aliases = [
    ["MAXXUM 5D", "MAXXUM 5D"],
    ["ALPHA-5 DIGITAL", "ALPHA-5 DIGITAL"],
    ["ALPHA SWEET DIGITAL", "ALPHA SWEET DIGITAL"],
  ]
  ```

---

### 3. Samsung Techwin GX-1L (Rebadged Pentax *ist DL2)
* **Raw Extension:** `.PEF`
* **Raw EXIF Header:** `Make: "SAMSUNG TECHWIN"`, `Model: "GX-1L              "`
* **Upstream Status:** In `rawler/src/decoders/mod.rs`, the decoder dispatcher only tests for `make == "SAMSUNG"`. It throws `Couldn't find a decoder for make "SAMSUNG TECHWIN"`.
* **Hardware:** The Samsung GX-1L is a joint-venture rebadged Pentax *ist DL2 with identical 6.1 MP CCD sensor and Pentax PEF structure.
* **Proposed Upstream PR:**
  1. In `rawler/src/decoders/mod.rs`, expand the manufacturer routing to handle `SAMSUNG TECHWIN`:
     ```rust
     "SAMSUNG" | "SAMSUNG TECHWIN" => return use_decoder!(srw::SrwDecoder, rawfile, tiff, self),
     ```
  2. If the file is PEF (TIFF-based with Pentax makernotes), allow the `pef::PefDecoder` to handle it:
     ```rust
     "PENTAX" | "PENTAX Corporation" | "SAMSUNG TECHWIN" => return use_decoder!(pef::PefDecoder, rawfile, tiff, self),
     ```
  3. In `rawler/data/cameras/Pentax/ist_dl2.toml`, add model aliases:
     ```toml
     model_aliases = [
       ["GX-1L", "GX-1L"],
     ]
     ```

---

### 4. Hasselblad Lunar (Luxury Rebadged Sony NEX-7)
* **Raw Extension:** `.ARW`
* **Raw EXIF Header:** `Make: "HASSELBLAD"`, `Model: "Lunar"`
* **Upstream Status:** In `rawler/src/decoders/mod.rs`, the manufacturer dispatcher only checks `make == "Hasselblad"` (case-sensitive) and routes to 3FR/FFF decoders. The Lunar was built by Sony for Hasselblad and uses standard Sony ARW 2.3 Bayer sensor data.
* **Hardware:** Identical 24.3 MP APS-C CMOS sensor as the Sony NEX-7.
* **Proposed Upstream PR:**
  1. In `rawler/src/decoders/mod.rs`, detect ARW container for Hasselblad Lunar:
     ```rust
     "HASSELBLAD" | "Hasselblad" => {
       if tiff.has_entry(TiffCommonTag::SonyRawFileType) || tiff.has_entry(TiffCommonTag::SonyToneCurve) {
         return use_decoder!(arw::ArwDecoder, rawfile, tiff, self);
       }
       return use_decoder!(fff::FffDecoder, rawfile, tiff, self);
     }
     ```
  2. In `rawler/data/cameras/Sony/nex7.toml`, add model alias:
     ```toml
     model_aliases = [
       ["Lunar", "Lunar"],
     ]
     ```

---

### 5. Sony Cinema Line ILME-FX2 (Variant of FX3 / FX30)
* **Raw Extension:** `.ARW`
* **Raw EXIF Header:** `Make: "SONY"`, `Model: "ILME-FX2"`
* **Upstream Status:** Missing from `rawler/data/cameras/Sony/`.
* **Hardware:** Standard Sony Cinema Line sensor architecture sharing CFA, color matrix, and Huffman tables with FX3 / FX30.
* **Proposed Upstream PR:**
  In `rawler/data/cameras/Sony/fx3.toml`, add `ILME-FX2` to `model_aliases`:
  ```toml
  model_aliases = [
    ["ILME-FX3A", "ILME-FX3A"],
    ["ILME-FX2", "ILME-FX2"],
  ]
  ```

---

### 6. Panasonic Lumix DMC-GM1S (Japanese Domestic Market Refresh of DMC-GM1)
* **Raw Extension:** `.RW2`
* **Raw EXIF Header:** `Make: "Panasonic"`, `Model: "DMC-GM1S"`
* **Upstream Status:** Missing from `rawler/data/cameras/Panasonic/gm1.toml`.
* **Hardware:** Identical 16.0 MP Micro Four Thirds Live MOS sensor and RW2 container as DMC-GM1.
* **Proposed Upstream PR:**
  In `rawler/data/cameras/Panasonic/gm1.toml`, add `model_aliases`:
  ```toml
  model_aliases = [
    ["DMC-GM1S", "DMC-GM1S"],
  ]
  ```

---

### 7. Panasonic Lumix DC-FZ45 (European Branding of DMC-FZ40)
* **Raw Extension:** `.RW2`
* **Raw EXIF Header:** `Make: "Panasonic"`, `Model: "DC-FZ45"`
* **Upstream Status:** Missing from `rawler/data/cameras/Panasonic/fz40.toml`.
* **Hardware:** Identical 14.1 MP 1/2.33" CCD sensor and RW2 format as DMC-FZ40.
* **Proposed Upstream PR:**
  In `rawler/data/cameras/Panasonic/fz40.toml`, add `model_aliases`:
  ```toml
  model_aliases = [
    ["DC-FZ45", "DC-FZ45"],
  ]
  ```

---

### 8. Pentax Corporation Make String (Pentax K200D, K20D, K-m, K-7)
* **Raw Extension:** `.PEF`
* **Raw EXIF Header:** `Make: "PENTAX Corporation"` (instead of `"PENTAX"`)
* **Upstream Status:** In `rawler/src/decoders/mod.rs`, the match statement matches `make == "PENTAX"`. Firmware versions on K200D and K20D write `"PENTAX Corporation"`, throwing `Couldn't find a decoder for make "PENTAX Corporation"`.
* **Proposed Upstream PR:**
  In `rawler/src/decoders/mod.rs`:
  ```rust
  "PENTAX" | "PENTAX Corporation" => return use_decoder!(pef::PefDecoder, rawfile, tiff, self),
  ```

---

### 9. Leaf Aptus Creo/Leaf Tag (Aptus 22, 65, 75)
* **Raw Extension:** `.MOS`
* **Raw EXIF Header:** `<tiff:Make>Creo/Leaf</tiff:Make>` (instead of `"Leaf"`)
* **Upstream Status:** In `rawler/src/decoders/mod.rs`, match checks `make == "Leaf"`. Files from Creo-era Leaf backs fail to match.
* **Proposed Upstream PR:**
  In `rawler/src/decoders/mod.rs`:
  ```rust
  "Leaf" | "Creo/Leaf" | "Creo" => return use_decoder!(mos::MosDecoder, rawfile, tiff, self),
  ```

---

### 10. Nikon 1 S1 (Omitted Model in Nikon 1 CX Family)
* **Raw Extension:** `.NEF`
* **Raw EXIF Header:** `Make: "NIKON CORPORATION"`, `Model: "NIKON 1 S1"`, `mode: "12bit"`
* **Upstream Status:** In `rawler/data/cameras/nikon/`, definitions exist for `1j1`, `1j2`, `1j3`, `1j5`, `1s2`, `1v1`, `1v2`, `1v3`, but `1s1.toml` was omitted.
* **Hardware:** 10.1 MP CX-format (13.2 x 8.8 mm) CMOS sensor sharing CFA pattern, active area, and Huffman tables with the Nikon 1 J1 / J2.
* **Proposed Upstream PR:**
  Add `rawler/data/cameras/nikon/1s1.toml` (or alias in `1j2.toml`):
  ```toml
  make = "NIKON CORPORATION"
  model = "NIKON 1 S1"
  clean_make = "Nikon"
  clean_model = "1 S1"
  color_pattern = "RGGB"
  active_area = [0, 0, 0, 0]
  crop_area = [6, 6, 6, 6]

  [cameras.color_matrix]
  A = [0.692, -0.1829, 0.0018, -0.2514, 0.9725, 0.3272, 0.0146, 0.1316, 0.5566]
  D65 = [0.6588, -0.1305, -0.0693, -0.3277, 1.0987, 0.2634, -0.0355, 0.2016, 0.5106]

  [[cameras.modes]]
  mode = "12bit"
  blackpoint = 0
  whitepoint = 3300
  ```

---

### 11. Nikon 1 J4 (Omitted Model in Nikon 1 CX Family)
* **Raw Extension:** `.NEF`
* **Raw EXIF Header:** `Make: "NIKON CORPORATION"`, `Model: "NIKON 1 J4"`, `mode: "12bit"`
* **Upstream Status:** `1j4.toml` is omitted in `rawler/data/cameras/nikon/`.
* **Hardware:** 18.4 MP CX-format CMOS sensor sharing architecture and matrix with Nikon 1 V3.
* **Proposed Upstream PR:**
  In `rawler/data/cameras/nikon/1v3.toml`, add `model_aliases`:
  ```toml
  model_aliases = [
    ["NIKON 1 J4", "1 J4"],
  ]
  ```

---

### 12. Nikon D1H (Vintage Professional CCD DSLR)
* **Raw Extension:** `.NEF`
* **Raw EXIF Header:** `Make: "NIKON CORPORATION"`, `Model: "NIKON D1H"`, `mode: "12bit"`
* **Upstream Status:** In `rawler/data/cameras/nikon/`, `d1.toml` and `d1x.toml` exist, but `d1h.toml` is missing.
* **Hardware:** Identical 2.7 MP CCD sensor and color matrix as the original Nikon D1.
* **Proposed Upstream PR:**
  In `rawler/data/cameras/nikon/d1.toml`, add `model_aliases`:
  ```toml
  model_aliases = [
    ["NIKON D1H", "D1H"],
  ]
  ```

---

### 13. Nikon COOLPIX P7700
* **Raw Extension:** `.NRW`
* **Raw EXIF Header:** `Make: "NIKON"`, `Model: "COOLPIX P7700"`, `mode: "12bit"`
* **Upstream Status:** `coolpix_p7000.toml`, `coolpix_p7100.toml`, and `coolpix_p7800.toml` exist, but `coolpix_p7700.toml` is missing.
* **Hardware:** Identical 12.2 MP 1/1.7" BSI CMOS sensor and image processor as the Coolpix P7800 (the P7800 was simply the P7700 with an added electronic viewfinder).
* **Proposed Upstream PR:**
  In `rawler/data/cameras/nikon/coolpix_p7800.toml`, add `model_aliases`:
  ```toml
  model_aliases = [
    ["COOLPIX P7700", "Coolpix P7700"],
  ]
  ```

---

### 14. Minolta DiMAGE 5
* **Raw Extension:** `.MRW`
* **Raw EXIF Header:** `Make: "Minolta Co., Ltd."`, `Model: "DiMAGE 5"`
* **Upstream Status:** `7.toml`, `7i.toml`, `7hi.toml` exist in `rawler/data/cameras/minolta/`, but `5.toml` was omitted.
* **Hardware:** Bridge camera sharing the DiMAGE 7 image processing pipeline and color matrices.
* **Proposed Upstream PR:**
  In `rawler/data/cameras/minolta/7.toml`, add `model_aliases`:
  ```toml
  model_aliases = [
    ["DiMAGE 5", "DiMAGE 5"],
  ]
  ```

---

### 15. Samsung Galaxy NX (EK-GN120)
* **Raw Extension:** `.SRW`
* **Raw EXIF Header:** `Make: "SAMSUNG"`, `Model: "EK-GN120"`
* **Upstream Status:** Missing from `rawler/data/cameras/samsung/`.
* **Hardware:** Samsung's Android-powered mirrorless camera using the exact 20.3 MP APS-C CMOS sensor and SRW format as the Samsung NX300 / NX2000.
* **Proposed Upstream PR:**
  In `rawler/data/cameras/samsung/nx300.toml`, add `model_aliases`:
  ```toml
  model_aliases = [
    ["EK-GN120", "Galaxy NX"],
  ]
  ```

---

## 💡 How Fujify Handles These Currently

Until upstream pull requests are merged and tagged in `rawler` / `dnglab`:
1. In native C++ / Rust FFI ([`native/dnglab_ffi/src/lib.rs`](../native/dnglab_ffi/src/lib.rs)), `try_create_temp_with_normalized_camera` intercepts unknown camera errors, modifies the TIFF/EXIF tag bytes in-memory with equal-length substitutions, and safely converts the raw image.
2. In C# managed layer ([`Fujify/Services/DngLabNative.cs`](../Fujify/Services/DngLabNative.cs)), `TryCreateTempWithNormalizedCamera` provides a secondary safety fallback.

