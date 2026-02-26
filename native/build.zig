const std = @import("std");

/// SharpDicom native codecs build script
/// Cross-compiles to 6 target platforms: win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64
///
/// Vendor libraries:
/// - libjpeg-turbo: vendor/libjpeg-turbo/src (downloaded in CI)
/// - OpenJPEG: vendor/openjpeg/src (downloaded in CI)
/// - CharLS: vendor/charls/src (downloaded in CI)
/// - FFmpeg: vendor/ffmpeg/ (downloaded in CI) - decoding and encoding
/// - x264: vendor/x264/ (downloaded in CI) - H.264 software encoder
/// - x265: vendor/x265/ (downloaded in CI) - HEVC software encoder
/// - stb_image: vendor/stb/ (downloaded in CI) - image sequence loading
pub fn build(b: *std.Build) void {
    // All vendor libraries must be present - CI downloads them before build.
    // For local development, run scripts/download-vendors.sh first.
    //
    // Verify required vendor sources exist. Build fails if any are missing.
    const have_libjpeg = detectVendorLibrary("vendor/libjpeg-turbo/src");
    const have_openjpeg = detectVendorLibrary("vendor/openjpeg/src");
    const have_charls = detectVendorLibrary("vendor/charls/src");
    const have_ffmpeg = detectVendorLibrary("vendor/ffmpeg");
    const have_tesseract = detectVendorLibrary("vendor/tesseract/src");
    const have_stb_image = detectVendorLibrary("vendor/stb"); // stb_image is header-only

    // Also check for encoder libraries when FFmpeg encoding is enabled
    const have_x264 = detectVendorLibrary("vendor/x264");
    const have_x265 = detectVendorLibrary("vendor/x265/source");
    const have_leptonica = detectVendorLibrary("vendor/leptonica/src");

    // All vendor libraries are required - fail the build if any are missing.
    // Run scripts/download-vendors.sh or let CI download them before building.
    var missing_vendors = false;
    if (!have_libjpeg) {
        std.log.err("libjpeg-turbo sources required at vendor/libjpeg-turbo/src", .{});
        missing_vendors = true;
    }
    if (!have_openjpeg) {
        std.log.err("OpenJPEG sources required at vendor/openjpeg/src", .{});
        missing_vendors = true;
    }
    if (!have_charls) {
        std.log.err("CharLS sources required at vendor/charls/src", .{});
        missing_vendors = true;
    }
    if (!have_ffmpeg) {
        std.log.err("FFmpeg sources required at vendor/ffmpeg", .{});
        missing_vendors = true;
    }
    if (!have_x264) {
        std.log.err("x264 sources required at vendor/x264", .{});
        missing_vendors = true;
    }
    if (!have_x265) {
        std.log.err("x265 sources required at vendor/x265/source", .{});
        missing_vendors = true;
    }
    if (!have_stb_image) {
        std.log.err("stb sources required at vendor/stb", .{});
        missing_vendors = true;
    }
    if (missing_vendors) {
        std.debug.panic("Missing required vendor sources. Run scripts/download-vendors.sh first.", .{});
    }

    // Target configurations for all supported platforms
    // Using GNU ABI for Windows for better Zig cross-compilation support
    const targets = [_]std.Target.Query{
        // Windows x64 (GNU ABI for cross-compilation)
        .{
            .cpu_arch = .x86_64,
            .os_tag = .windows,
            .abi = .gnu,
        },
        // Windows ARM64 (GNU ABI for cross-compilation)
        .{
            .cpu_arch = .aarch64,
            .os_tag = .windows,
            .abi = .gnu,
        },
        // Linux x64 (glibc for compatibility with .NET runtime)
        .{
            .cpu_arch = .x86_64,
            .os_tag = .linux,
            .abi = .gnu,
        },
        // Linux ARM64 (glibc for compatibility with .NET runtime)
        .{
            .cpu_arch = .aarch64,
            .os_tag = .linux,
            .abi = .gnu,
        },
        // macOS x64
        .{
            .cpu_arch = .x86_64,
            .os_tag = .macos,
        },
        // macOS ARM64
        .{
            .cpu_arch = .aarch64,
            .os_tag = .macos,
        },
    };

    // Force ReleaseFast for cross-compiled libraries - these should always be optimized
    const optimize = std.builtin.OptimizeMode.ReleaseFast;

    // Build shared library for each target
    for (targets) |target_query| {
        const target = b.resolveTargetQuery(target_query);
        const lib = b.addSharedLibrary(.{
            .name = "sharpdicom_codecs",
            .target = target,
            .optimize = optimize,
            .pic = true, // Position-independent code for ASLR
            .strip = true, // Strip debug symbols for smaller binaries (all platforms)
        });

        // Link libc for cross-compilation (provides standard headers like string.h, stdlib.h)
        lib.linkLibC();

        // Build flags common to all source files
        // Note: -O2 required before -D_FORTIFY_SOURCE=2 for glibc compatibility
        const common_flags = &[_][]const u8{
            "-std=c11",
            "-O2", // Required for _FORTIFY_SOURCE with glibc
            "-fstack-protector-strong", // Security hardening
            "-D_FORTIFY_SOURCE=2",
            "-Wall",
            "-Wextra",
            "-Werror",
        };

        // Note: 12-bit libjpeg-turbo uses addLibjpegTurbo12Sources() with full symbol prefix flags

        // All vendor libraries are required, so all features are enabled
        const all_features_flags = common_flags ++ &[_][]const u8{
            "-DSHARPDICOM_WITH_JPEG",
            "-DSHARPDICOM_WITH_JPEG12",
            "-DSHARPDICOM_WITH_J2K",
            "-DSHARPDICOM_WITH_JLS",
            "-DSHARPDICOM_WITH_STB_IMAGE",
            "-DSHARPDICOM_WITH_FFMPEG_ENC",
            "-DSHARPDICOM_WITH_MPEG",
        };

        // Core codec dispatcher
        lib.addCSourceFile(.{
            .file = b.path("src/sharpdicom_codecs.c"),
            .flags = all_features_flags,
        });

        // JPEG wrapper (libjpeg-turbo 8-bit)
        lib.addCSourceFile(.{
            .file = b.path("src/jpeg_wrapper.c"),
            .flags = all_features_flags,
        });
        addLibjpegTurboSources(lib, b);

        // J2K wrapper (OpenJPEG)
        lib.addCSourceFile(.{
            .file = b.path("src/j2k_wrapper.c"),
            .flags = all_features_flags ++ &[_][]const u8{"-DSHARPDICOM_HAS_OPENJPEG"},
        });
        lib.addIncludePath(b.path("vendor/openjpeg/src/src/lib/openjp2"));
        addOpenJpegSources(lib, b, common_flags);

        // GPU wrapper (dynamically loads nvJPEG2000)
        lib.addCSourceFile(.{
            .file = b.path("src/gpu_wrapper.c"),
            .flags = common_flags,
        });

        // JLS wrapper (CharLS)
        lib.addCSourceFile(.{
            .file = b.path("src/jls_wrapper.c"),
            .flags = all_features_flags ++ &[_][]const u8{"-DSHARPDICOM_HAS_CHARLS"},
        });
        lib.addIncludePath(b.path("vendor/charls/src/include"));
        lib.addIncludePath(b.path("vendor/charls/src/src"));
        addCharlsSources(lib, b);

        // Video wrapper (FFmpeg decoding)
        lib.addCSourceFile(.{
            .file = b.path("src/video_wrapper.c"),
            .flags = all_features_flags ++ &[_][]const u8{"-DSHARPDICOM_HAS_FFMPEG"},
        });
        lib.addIncludePath(b.path("vendor/ffmpeg"));
        addFfmpegEncSources(lib, b);

        // Video encoder (FFmpeg encoding with x264/x265)
        lib.addCSourceFile(.{
            .file = b.path("src/video_encoder.c"),
            .flags = all_features_flags,
        });
        addX264Sources(lib, b);
        addX265Sources(lib, b);

        // Tesseract OCR wrapper - always compile as stub for cross-compilation
        // System libraries (tesseract, lept) aren't available during cross-compilation.
        // Tesseract support is only enabled for native builds where system libs are present.
        lib.addCSourceFile(.{
            .file = b.path("src/tesseract_wrapper.c"),
            .flags = common_flags,
        });

        // 12-bit JPEG wrapper (separate libjpeg-turbo build with symbol prefixes)
        lib.addCSourceFile(.{
            .file = b.path("src/jpeg12_wrapper.c"),
            .flags = all_features_flags,
        });
        addLibjpegTurbo12Sources(lib, b);

        // stb_image wrapper (image sequence loading)
        lib.addCSourceFile(.{
            .file = b.path("src/stb_image_wrapper.c"),
            .flags = all_features_flags ++ &[_][]const u8{
                "-Wno-unused-function",
                "-Wno-sign-compare",
            },
        });
        lib.addIncludePath(b.path("vendor/stb"));

        // Include paths
        lib.addIncludePath(b.path("src"));

        // Link -ldl for dynamic library loading on Linux
        if (target_query.os_tag == .linux) {
            lib.linkSystemLibrary("dl");
        }

        // Install the library to zig-out with platform-specific naming
        const rid = getRuntimeId(target_query);
        const install_step = b.addInstallArtifact(lib, .{
            .dest_dir = .{ .override = .{ .custom = rid } },
        });
        b.getInstallStep().dependOn(&install_step.step);
    }

    // Native target (for single-platform build and tests)
    const native_target = b.standardTargetOptions(.{});

    // Single-platform build step (for development)
    const single_step = b.step("native", "Build for native platform only");
    const native_lib = b.addSharedLibrary(.{
        .name = "sharpdicom_codecs",
        .target = native_target,
        .optimize = optimize,
        .pic = true,
        .strip = true, // Strip debug symbols for smaller binaries
    });

    // Link libc for standard library headers
    native_lib.linkLibC();

    const native_flags = &[_][]const u8{
        "-std=c11",
        "-fstack-protector-strong",
        "-Wall",
        "-Wextra",
        "-Werror",
    };

    // All vendor libraries are required, so all features are enabled
    const native_all_features_flags = native_flags ++ &[_][]const u8{
        "-DSHARPDICOM_WITH_JPEG",
        "-DSHARPDICOM_WITH_JPEG12",
        "-DSHARPDICOM_WITH_J2K",
        "-DSHARPDICOM_WITH_JLS",
        "-DSHARPDICOM_WITH_STB_IMAGE",
        "-DSHARPDICOM_WITH_FFMPEG_ENC",
        "-DSHARPDICOM_WITH_MPEG",
    };

    // Core codec dispatcher
    native_lib.addCSourceFile(.{
        .file = b.path("src/sharpdicom_codecs.c"),
        .flags = native_all_features_flags,
    });

    // JPEG wrapper (libjpeg-turbo 8-bit)
    native_lib.addCSourceFile(.{
        .file = b.path("src/jpeg_wrapper.c"),
        .flags = native_all_features_flags,
    });
    addLibjpegTurboSources(native_lib, b);

    // J2K wrapper (OpenJPEG)
    native_lib.addCSourceFile(.{
        .file = b.path("src/j2k_wrapper.c"),
        .flags = native_all_features_flags ++ &[_][]const u8{"-DSHARPDICOM_HAS_OPENJPEG"},
    });
    native_lib.addIncludePath(b.path("vendor/openjpeg/src/src/lib/openjp2"));
    addOpenJpegSources(native_lib, b, native_flags);

    // GPU wrapper (dynamically loads nvJPEG2000)
    native_lib.addCSourceFile(.{
        .file = b.path("src/gpu_wrapper.c"),
        .flags = native_flags,
    });

    // JLS wrapper (CharLS)
    native_lib.addCSourceFile(.{
        .file = b.path("src/jls_wrapper.c"),
        .flags = native_all_features_flags ++ &[_][]const u8{"-DSHARPDICOM_HAS_CHARLS"},
    });
    native_lib.addIncludePath(b.path("vendor/charls/src/include"));
    native_lib.addIncludePath(b.path("vendor/charls/src/src"));
    addCharlsSources(native_lib, b);

    // Video wrapper (FFmpeg decoding)
    native_lib.addCSourceFile(.{
        .file = b.path("src/video_wrapper.c"),
        .flags = native_all_features_flags ++ &[_][]const u8{"-DSHARPDICOM_HAS_FFMPEG"},
    });
    native_lib.addIncludePath(b.path("vendor/ffmpeg"));
    addFfmpegEncSources(native_lib, b);

    // Video encoder (FFmpeg encoding with x264/x265)
    native_lib.addCSourceFile(.{
        .file = b.path("src/video_encoder.c"),
        .flags = native_all_features_flags,
    });
    addX264Sources(native_lib, b);
    addX265Sources(native_lib, b);

    // Tesseract OCR wrapper - source compilation not yet implemented
    // When implemented, this will compile Tesseract 5.x and Leptonica from source
    // For now, always builds without Tesseract support
    _ = have_tesseract;
    _ = have_leptonica;
    native_lib.addCSourceFile(.{
        .file = b.path("src/tesseract_wrapper.c"),
        .flags = native_flags,
    });

    // 12-bit JPEG wrapper (separate libjpeg-turbo build with symbol prefixes)
    native_lib.addCSourceFile(.{
        .file = b.path("src/jpeg12_wrapper.c"),
        .flags = native_all_features_flags,
    });
    addLibjpegTurbo12Sources(native_lib, b);

    // stb_image wrapper (image sequence loading)
    native_lib.addCSourceFile(.{
        .file = b.path("src/stb_image_wrapper.c"),
        .flags = native_all_features_flags ++ &[_][]const u8{
            "-Wno-unused-function",
            "-Wno-sign-compare",
        },
    });
    native_lib.addIncludePath(b.path("vendor/stb"));

    native_lib.addIncludePath(b.path("src"));

    // Link -ldl on Linux for dynamic library loading
    if (native_target.result.os.tag == .linux) {
        native_lib.linkSystemLibrary("dl");
    }

    const native_install = b.addInstallArtifact(native_lib, .{});
    single_step.dependOn(&native_install.step);

    // Native test executable - links against native_lib to verify the full build
    const test_exe = b.addExecutable(.{
        .name = "test_version",
        .target = native_target,
        .optimize = optimize,
    });

    test_exe.linkLibC();
    test_exe.addCSourceFile(.{
        .file = b.path("test/test_version.c"),
        .flags = &.{
            "-std=c11",
            "-Wall",
            "-Wextra",
        },
    });
    test_exe.addIncludePath(b.path("src"));

    // Link against the native_lib artifact (this creates a build dependency)
    test_exe.linkLibrary(native_lib);

    // Link -ldl on Linux for dynamic library loading
    if (native_target.result.os.tag == .linux) {
        test_exe.linkSystemLibrary("dl");
    }

    const test_install = b.addInstallArtifact(test_exe, .{});

    // Test step
    const test_step = b.step("test", "Run native tests");
    const run_test = b.addRunArtifact(test_exe);
    test_step.dependOn(&test_install.step);
    test_step.dependOn(&run_test.step);
}

/// Maps Zig target to .NET Runtime Identifier
fn getRuntimeId(target: std.Target.Query) []const u8 {
    const arch = switch (target.cpu_arch orelse .x86_64) {
        .x86_64 => "x64",
        .aarch64 => "arm64",
        else => "unknown",
    };

    const os = switch (target.os_tag orelse .linux) {
        .windows => "win",
        .linux => "linux",
        .macos => "osx",
        else => "unknown",
    };

    // Return static string based on combination
    if (std.mem.eql(u8, os, "win") and std.mem.eql(u8, arch, "x64")) return "win-x64";
    if (std.mem.eql(u8, os, "win") and std.mem.eql(u8, arch, "arm64")) return "win-arm64";
    if (std.mem.eql(u8, os, "linux") and std.mem.eql(u8, arch, "x64")) return "linux-x64";
    if (std.mem.eql(u8, os, "linux") and std.mem.eql(u8, arch, "arm64")) return "linux-arm64";
    if (std.mem.eql(u8, os, "osx") and std.mem.eql(u8, arch, "x64")) return "osx-x64";
    if (std.mem.eql(u8, os, "osx") and std.mem.eql(u8, arch, "arm64")) return "osx-arm64";

    return "unknown";
}

/// Detect if a vendor library directory exists
fn detectVendorLibrary(path: []const u8) bool {
    var dir = std.fs.cwd().openDir(path, .{}) catch return false;
    dir.close();
    return true;
}

/// Add OpenJPEG source files to compilation
fn addOpenJpegSources(lib: *std.Build.Step.Compile, b: *std.Build, _: []const []const u8) void {
    const opj_base = "vendor/openjpeg/src/src/lib/openjp2";

    // OpenJPEG core source files
    const opj_sources = [_][]const u8{
        "bio.c",
        "cio.c",
        "dwt.c",
        "event.c",
        "function_list.c",
        "ht_dec.c",
        "image.c",
        "invert.c",
        "j2k.c",
        "jp2.c",
        "mct.c",
        "mqc.c",
        "openjpeg.c",
        "opj_clock.c",
        "opj_malloc.c",
        "pi.c",
        "sparse_array.c",
        "t1.c",
        "t2.c",
        "tcd.c",
        "tgt.c",
        "thread.c",
    };

    // OpenJPEG-specific flags - defined as comptime constant to allow concatenation
    // Includes common flags plus OpenJPEG-specific suppressions for third-party code
    // Note: -O2 required before -D_FORTIFY_SOURCE=2 for glibc compatibility
    const opj_flags = &[_][]const u8{
        "-std=c11",
        "-O2", // Required for _FORTIFY_SOURCE with glibc
        "-fstack-protector-strong",
        "-D_FORTIFY_SOURCE=2",
        "-Wall",
        "-Wextra",
        "-Werror",
        "-Wno-unused-parameter",
        "-Wno-sign-compare",
        "-Wno-implicit-fallthrough",
        "-Wno-unused-but-set-variable", // OpenJPEG has some variables set but not used
        "-Wno-unused-function", // OpenJPEG has static functions not used in all configs
        "-DOPJ_STATIC",
        "-DUSE_JPIP=0",
    };

    for (opj_sources) |src| {
        const full_path = std.fmt.allocPrint(b.allocator, "{s}/{s}", .{ opj_base, src }) catch continue;
        lib.addCSourceFile(.{
            .file = b.path(full_path),
            .flags = opj_flags,
        });
    }

    // Add OpenJPEG include paths
    lib.addIncludePath(b.path(opj_base));
}

/// Add CharLS source files to compilation (JPEG-LS codec).
/// Vendor sources are downloaded by CI into vendor/charls/src/.
///
/// CharLS is a C++17 library that provides a C API (charls.h).
/// We compile the C++ sources and link them into our shared library.
///
/// Reference: https://github.com/team-charls/charls
fn addCharlsSources(lib: *std.Build.Step.Compile, b: *std.Build) void {
    const charls_base = "vendor/charls/src/src";

    // CharLS C++17 compilation flags - relaxed warnings for third-party code
    // Note: -O2 required before -D_FORTIFY_SOURCE=2 for glibc compatibility
    const charls_flags = &[_][]const u8{
        "-std=c++17",
        "-O2", // Required for _FORTIFY_SOURCE with glibc
        "-fstack-protector-strong",
        "-D_FORTIFY_SOURCE=2",
        "-Wall",
        "-Wextra",
        "-Wno-unused-parameter",
        "-Wno-sign-compare",
        "-Wno-missing-field-initializers",
        "-DCHARLS_STATIC", // Build as static library to link into our shared lib
    };

    // CharLS 2.4.2 source files (from src/CMakeLists.txt)
    // Note: Files like golomb_lut.cpp, make_scan_codec.cpp only exist in unreleased main branch.
    // Using 2.4.2 stable release file list.
    const charls_sources = [_][]const u8{
        "charls_jpegls_decoder.cpp",
        "charls_jpegls_encoder.cpp",
        "jpeg_stream_reader.cpp",
        "jpeg_stream_writer.cpp",
        "jpegls.cpp",
        "jpegls_error.cpp",
        "validate_spiff_header.cpp",
        "version.cpp",
    };

    for (charls_sources) |src| {
        const full_path = std.fmt.allocPrint(b.allocator, "{s}/{s}", .{ charls_base, src }) catch continue;
        lib.addCSourceFile(.{
            .file = b.path(full_path),
            .flags = charls_flags,
        });
    }

    // CharLS include paths
    lib.addIncludePath(b.path("vendor/charls/src/include"));
    lib.addIncludePath(b.path(charls_base));

    // Link C++ standard library for C++ code
    lib.linkLibCpp();
}

/// Add x264 source files to compilation (H.264 software encoder).
/// Vendor sources are downloaded by CI into vendor/x264/.
/// Requires a generated x264_config.h at vendor/x264/x264_config.h.
///
/// Build follows the pattern from x264's Makefile:
/// - Core encoder in common/ and encoder/ directories
/// - x264_config.h defines X264_BIT_DEPTH=8, X264_CHROMA_FORMAT=0 (all), X264_GPL=1
///
/// Reference: https://code.videolan.org/videolan/x264
fn addX264Sources(lib: *std.Build.Step.Compile, b: *std.Build) void {
    const x264_base = "vendor/x264";

    // x264 compilation flags - relaxed warnings for third-party code
    // Note: -O2 required before -D_FORTIFY_SOURCE=2 for glibc compatibility
    // Note: Include paths via -I flags to avoid polluting other codecs' compilation
    const x264_flags = &[_][]const u8{
        "-std=c11",
        "-O2", // Required for _FORTIFY_SOURCE with glibc
        "-fstack-protector-strong",
        "-D_FORTIFY_SOURCE=2",
        "-D_POSIX_C_SOURCE=200809L", // Ensure POSIX functions (clock_gettime) are declared
        "-D_GNU_SOURCE", // For additional GNU extensions
        "-Wall",
        "-Wextra",
        "-Wno-error", // Downgrade errors to warnings for third-party code
        "-Wno-unused-parameter",
        "-Wno-sign-compare",
        "-Wno-unused-variable",
        "-Wno-implicit-fallthrough",
        "-Wno-missing-field-initializers",
        "-Wno-implicit-function-declaration", // For clock_gettime on some platforms
        "-Ivendor/x264", // x264.h and x264_config.h
        "-Ivendor/x264/common", // Internal x264 common headers
        "-Ivendor/x264/encoder", // Internal x264 encoder headers (for analyse.h etc.)
    };

    // x264 core encoder sources (no CLI, no filters, no asm)
    const x264_sources = [_][]const u8{
        // common/
        "common/base.c",
        "common/bitstream.c",
        "common/cabac.c",
        "common/common.c",
        "common/dct.c",
        "common/deblock.c",
        "common/frame.c",
        "common/mc.c",
        "common/mvpred.c",
        "common/osdep.c",
        "common/pixel.c",
        "common/predict.c",
        "common/quant.c",
        "common/rectangle.c",
        "common/set.c",
        "common/vlc.c",
        "common/threadpool.c",
        "common/cpu.c",
        "common/tables.c",
        // encoder/
        "encoder/analyse.c",
        "encoder/cabac.c",
        "encoder/cavlc.c",
        "encoder/encoder.c",
        "encoder/lookahead.c",
        "encoder/macroblock.c",
        "encoder/me.c",
        "encoder/ratecontrol.c",
        "encoder/set.c",
        "encoder/slicetype.c",
    };

    for (x264_sources) |src| {
        const full_path = std.fmt.allocPrint(b.allocator, "{s}/{s}", .{ x264_base, src }) catch continue;
        lib.addCSourceFile(.{
            .file = b.path(full_path),
            .flags = x264_flags,
        });
    }

    // Note: x264 include paths are set via -I flags in x264_flags above,
    // NOT via lib.addIncludePath, to avoid polluting x265's compilation
    // with x264's similarly-named headers (common.h, threadpool.h).
}

/// Add x265 source files to compilation (HEVC software encoder).
/// Vendor sources are downloaded by CI into vendor/x265/.
/// Requires generated x265_config.h at vendor/x265/source/x265_config.h.
///
/// Build follows the pattern from x265's CMakeLists.txt:
/// - Core encoder in source/common/ and source/encoder/ directories
/// - x265_config.h defines X265_DEPTH=8, EXPORT_C_API=1, X265_NS=x265
///
/// NOTE: x265 is C++, compiled with Zig's C++ compiler mode.
/// Reference: https://bitbucket.org/multicoreware/x265_git
fn addX265Sources(lib: *std.Build.Step.Compile, b: *std.Build) void {
    const x265_base = "vendor/x265/source";

    // x265 compilation flags - C++ mode, relaxed warnings for third-party code
    // Note: -O2 required before -D_FORTIFY_SOURCE=2 for glibc compatibility
    // Note: Include paths via -I flags to avoid polluting other codecs' compilation
    const x265_flags = &[_][]const u8{
        "-std=c++14",
        "-O2", // Required for _FORTIFY_SOURCE with glibc
        "-fstack-protector-strong",
        "-D_FORTIFY_SOURCE=2",
        "-D_POSIX_C_SOURCE=200809L", // Ensure POSIX functions are declared
        "-D_GNU_SOURCE", // For additional GNU extensions
        "-Wall",
        "-Wextra",
        "-Wno-error", // Downgrade errors to warnings for third-party code
        "-Wno-unused-parameter",
        "-Wno-sign-compare",
        "-Wno-unused-variable",
        "-Wno-implicit-fallthrough",
        "-Wno-missing-field-initializers",
        "-Wno-deprecated-declarations",
        "-Wno-unused-function",
        "-Wno-unused-but-set-variable",
        "-DHAVE_CONFIG_H", // Use generated x265_config.h
        "-D__STDC_CONSTANT_MACROS", // Required for INT64_C etc from stdint.h
        "-Ivendor/x265/source", // x265.h, x265_config.h
        "-Ivendor/x265/source/common", // Internal x265 common headers
        "-Ivendor/x265/source/encoder", // Internal x265 encoder headers
    };

    // x265 core encoder sources (no CLI, no asm)
    const x265_sources = [_][]const u8{
        // common/
        "common/bitstream.cpp",
        "common/common.cpp",
        "common/constants.cpp",
        "common/cpu.cpp",
        "common/cudata.cpp",
        "common/dct.cpp",
        "common/deblock.cpp",
        "common/frame.cpp",
        "common/framedata.cpp",
        "common/intrapred.cpp",
        "common/ipfilter.cpp",
        "common/loopfilter.cpp",
        "common/lowpassdct.cpp",
        "common/lowres.cpp",
        "common/md5.cpp",
        "common/param.cpp",
        "common/piclist.cpp",
        "common/picyuv.cpp",
        "common/pixel.cpp",
        "common/predict.cpp",
        "common/primitives.cpp",
        "common/quant.cpp",
        "common/ringmem.cpp",
        "common/scalinglist.cpp",
        "common/shortyuv.cpp",
        "common/slice.cpp",
        "common/threading.cpp",
        "common/threadpool.cpp",
        "common/wavefront.cpp",
        "common/yuv.cpp",
        // encoder/
        "encoder/analysis.cpp",
        "encoder/api.cpp",
        "encoder/bitcost.cpp",
        "encoder/dpb.cpp",
        "encoder/encoder.cpp",
        "encoder/entropy.cpp",
        "encoder/frameencoder.cpp",
        "encoder/framefilter.cpp",
        "encoder/level.cpp",
        "encoder/motion.cpp",
        "encoder/nal.cpp",
        "encoder/ratecontrol.cpp",
        "encoder/reference.cpp",
        "encoder/sao.cpp",
        "encoder/search.cpp",
        "encoder/sei.cpp",
        "encoder/slicetype.cpp",
        "encoder/weightPrediction.cpp",
    };

    for (x265_sources) |src| {
        const full_path = std.fmt.allocPrint(b.allocator, "{s}/{s}", .{ x265_base, src }) catch continue;
        lib.addCSourceFile(.{
            .file = b.path(full_path),
            .flags = x265_flags,
        });
    }

    // Note: x265 include paths are set via -I flags in x265_flags above,
    // NOT via lib.addIncludePath, to avoid include path pollution.

    // Link C++ standard library for x265 (C++ code)
    lib.linkLibCpp();
}

/// Add FFmpeg encoding library source files to compilation.
/// Vendor sources are downloaded by CI into vendor/ffmpeg/.
/// Requires a generated config.h at vendor/ffmpeg/config.h.
///
/// This compiles a minimal subset of FFmpeg focused on encoding:
/// - libavcodec: core encoding framework + specific encoders
/// - libavutil: utility functions (math, memory, pixfmt, etc.)
/// - libswscale: pixel format conversion (RGB -> YUV)
/// - libswresample: audio format conversion
/// - libavformat: muxing (MPEG-TS, raw H.264/HEVC Annex-B output)
///
/// The FFmpeg build bypasses configure/make and compiles C sources directly
/// via Zig, following the allyourcodebase/ffmpeg pattern.
///
/// Minimal encoder configuration (matching RESEARCH.md):
/// - Video encoders: mpeg2video (built-in), libx264, libx265
/// - Audio encoders: aac, pcm_s16le
/// - Video decoders: mpeg2video, h264, hevc (already in have_ffmpeg decode path)
/// - Audio decoders: aac, pcm_s16le
/// - Muxers: mpegts, h264, hevc, rawvideo, adts, wav
///
/// Reference: https://github.com/FFmpeg/FFmpeg
fn addFfmpegEncSources(lib: *std.Build.Step.Compile, b: *std.Build) void {
    const ffmpeg_base = "vendor/ffmpeg";

    // FFmpeg compilation flags - relaxed warnings for third-party code
    // config.h is generated by CI/scripts to enable only the needed codecs
    // Note: -O2 required before -D_FORTIFY_SOURCE=2 for glibc compatibility
    const ffmpeg_flags = &[_][]const u8{
        "-std=c11",
        "-O2", // Required for _FORTIFY_SOURCE with glibc
        "-fstack-protector-strong",
        "-D_FORTIFY_SOURCE=2",
        "-D_POSIX_C_SOURCE=200809L", // Ensure POSIX functions (localtime_r, etc.) are declared
        "-D_GNU_SOURCE", // For additional GNU extensions
        "-Wall",
        "-Wextra",
        "-Wno-error", // Downgrade errors to warnings for third-party code
        "-Wno-unused-parameter",
        "-Wno-sign-compare",
        "-Wno-unused-variable",
        "-Wno-implicit-fallthrough",
        "-Wno-missing-field-initializers",
        "-Wno-pointer-sign",
        "-Wno-switch",
        "-Wno-parentheses",
        "-Wno-deprecated-declarations",
        "-Wno-unused-function",
        "-Wno-unused-but-set-variable",
        "-Wno-implicit-function-declaration", // For strftime, localtime_r on some platforms
        "-DHAVE_CONFIG_H", // Use generated config.h
        "-D__STDC_CONSTANT_MACROS", // Required for INT64_C etc macros
    };

    // FFmpeg 7.1 source files for MPEG-2/H.264/HEVC encode+decode
    // Source lists derived from FFmpeg Makefiles and allyourcodebase/ffmpeg

    // libavutil core sources (from FFmpeg 7.1 Makefile OBJS)
    const avutil_sources = [_][]const u8{
        "libavutil/adler32.c",
        "libavutil/aes.c",
        "libavutil/aes_ctr.c",
        "libavutil/ambient_viewing_environment.c",
        "libavutil/audio_fifo.c",
        "libavutil/avsscanf.c",
        "libavutil/avstring.c",
        "libavutil/base64.c",
        "libavutil/blowfish.c",
        "libavutil/bprint.c",
        "libavutil/buffer.c",
        "libavutil/camellia.c",
        "libavutil/cast5.c",
        "libavutil/channel_layout.c",
        "libavutil/cpu.c",
        "libavutil/crc.c",
        "libavutil/csp.c",
        "libavutil/des.c",
        "libavutil/detection_bbox.c",
        "libavutil/dict.c",
        "libavutil/display.c",
        "libavutil/dovi_meta.c",
        "libavutil/downmix_info.c",
        "libavutil/encryption_info.c",
        "libavutil/error.c",
        "libavutil/eval.c",
        "libavutil/executor.c",
        "libavutil/fifo.c",
        "libavutil/file.c",
        "libavutil/file_open.c",
        "libavutil/film_grain_params.c",
        "libavutil/fixed_dsp.c",
        "libavutil/float_dsp.c",
        "libavutil/frame.c",
        "libavutil/half2float.c",
        "libavutil/hash.c",
        "libavutil/hdr_dynamic_metadata.c",
        "libavutil/hdr_dynamic_vivid_metadata.c",
        "libavutil/hmac.c",
        "libavutil/hwcontext.c",
        "libavutil/hwcontext_stub.c", // Stub for when no HW accel is available
        "libavutil/iamf.c",
        "libavutil/imgutils.c",
        "libavutil/integer.c",
        "libavutil/intmath.c",
        "libavutil/lfg.c",
        "libavutil/lls.c",
        "libavutil/log.c",
        "libavutil/log2_tab.c",
        "libavutil/lzo.c",
        "libavutil/mastering_display_metadata.c",
        "libavutil/mathematics.c",
        "libavutil/md5.c",
        "libavutil/mem.c",
        "libavutil/murmur3.c",
        "libavutil/opt.c",
        "libavutil/parseutils.c",
        "libavutil/pixdesc.c",
        "libavutil/pixelutils.c",
        "libavutil/random_seed.c",
        "libavutil/rational.c",
        "libavutil/rc4.c",
        "libavutil/reverse.c",
        "libavutil/ripemd.c",
        "libavutil/samplefmt.c",
        "libavutil/sha.c",
        "libavutil/sha512.c",
        "libavutil/slicethread.c",
        "libavutil/spherical.c",
        "libavutil/stereo3d.c",
        "libavutil/tea.c",
        "libavutil/threadmessage.c",
        "libavutil/time.c",
        "libavutil/timecode.c",
        "libavutil/timestamp.c",
        "libavutil/tree.c",
        "libavutil/twofish.c",
        "libavutil/tx.c",
        "libavutil/tx_double.c",
        "libavutil/tx_float.c",
        "libavutil/tx_int32.c",
        "libavutil/utils.c",
        "libavutil/uuid.c",
        "libavutil/version.c",
        "libavutil/video_enc_params.c",
        "libavutil/video_hint.c",
        "libavutil/xga_font_data.c",
        "libavutil/xtea.c",
    };

    // libavcodec sources for MPEG-2/H.264/HEVC decode + encode
    const avcodec_sources = [_][]const u8{
        // Core framework (always needed)
        "libavcodec/allcodecs.c",
        "libavcodec/avcodec.c",
        "libavcodec/avdct.c",
        "libavcodec/bitstream.c",
        "libavcodec/bitstream_filters.c",
        "libavcodec/blockdsp.c",
        "libavcodec/bsf.c",
        "libavcodec/cabac.c",
        "libavcodec/codec_desc.c",
        "libavcodec/codec_par.c",
        "libavcodec/decode.c",
        "libavcodec/encode.c",
        "libavcodec/error_resilience.c",
        "libavcodec/faandct.c",
        "libavcodec/faanidct.c",
        "libavcodec/fdctdsp.c",
        "libavcodec/get_buffer.c",
        "libavcodec/golomb.c",
        "libavcodec/h263.c",
        "libavcodec/h263data.c",
        "libavcodec/h263dec.c",
        "libavcodec/h263dsp.c",
        "libavcodec/hpeldsp.c",
        "libavcodec/idctdsp.c",
        "libavcodec/imgconvert.c",
        "libavcodec/jfdctfst.c",
        "libavcodec/jfdctint.c",
        "libavcodec/jrevdct.c",
        "libavcodec/me_cmp.c",
        "libavcodec/options.c",
        "libavcodec/packet.c",
        "libavcodec/parser.c",
        "libavcodec/parsers.c",
        "libavcodec/pixblockdsp.c",
        "libavcodec/profiles.c",
        "libavcodec/raw.c",
        "libavcodec/refstruct.c",
        "libavcodec/rl.c",
        "libavcodec/simple_idct.c",
        "libavcodec/startcode.c",
        "libavcodec/threadprogress.c",
        "libavcodec/utils.c",
        "libavcodec/version.c",
        "libavcodec/videodsp.c",
        "libavcodec/vlc.c",
        // Additional codec dependencies
        "libavcodec/aom_film_grain.c",
        "libavcodec/bswapdsp.c",
        "libavcodec/container_fifo.c",
        // MPEG-1/2 video decoder
        "libavcodec/mpeg12.c",
        "libavcodec/mpeg12data.c",
        "libavcodec/mpeg12dec.c",
        "libavcodec/mpeg_er.c",
        "libavcodec/mpegpicture.c",
        "libavcodec/mpegvideo.c",
        "libavcodec/mpegvideo_motion.c",
        "libavcodec/mpegvideodata.c",
        // MPEG-2 video encoder
        "libavcodec/mpeg12enc.c",
        "libavcodec/mpegvideo_enc.c",
        "libavcodec/motion_est.c",
        "libavcodec/ratecontrol.c",
        // H.264 decoder
        "libavcodec/h264_cabac.c",
        "libavcodec/h264_cavlc.c",
        "libavcodec/h264_direct.c",
        "libavcodec/h264_loopfilter.c",
        "libavcodec/h264_mb.c",
        "libavcodec/h264_parse.c",
        "libavcodec/h264_parser.c",
        "libavcodec/h264_picture.c",
        "libavcodec/h264_ps.c",
        "libavcodec/h264_refs.c",
        "libavcodec/h264_sei.c",
        "libavcodec/h264_slice.c",
        "libavcodec/h264chroma.c",
        "libavcodec/h264data.c",
        "libavcodec/h264dec.c",
        "libavcodec/h264dsp.c",
        "libavcodec/h264idct.c",
        "libavcodec/h264pred.c",
        "libavcodec/h264qpel.c",
        "libavcodec/h2645_parse.c",
        "libavcodec/h2645_sei.c",
        "libavcodec/h2645_vui.c",
        "libavcodec/h2645data.c",
        // HEVC decoder (in hevc/ subdirectory)
        "libavcodec/hevc/cabac.c",
        "libavcodec/hevc/data.c",
        "libavcodec/hevc/dsp.c",
        "libavcodec/hevc/filter.c",
        "libavcodec/hevc/hevcdec.c",
        "libavcodec/hevc/mvs.c",
        "libavcodec/hevc/parse.c",
        "libavcodec/hevc/parser.c",
        "libavcodec/hevc/pred.c",
        "libavcodec/hevc/ps.c",
        "libavcodec/hevc/refs.c",
        "libavcodec/hevc/sei.c",
        // libx264 wrapper (H.264 encoding via x264)
        "libavcodec/libx264.c",
        // libx265 wrapper (HEVC encoding via x265)
        "libavcodec/libx265.c",
        // AAC audio encoder
        "libavcodec/aacenc.c",
        "libavcodec/aaccoder.c",
        "libavcodec/aacenctab.c",
        "libavcodec/aacpsy.c",
        "libavcodec/psymodel.c",
        // PCM encoder
        "libavcodec/pcm.c",
    };

    // libswscale sources (pixel format conversion)
    const swscale_sources = [_][]const u8{
        "libswscale/alphablend.c",
        "libswscale/gamma.c",
        "libswscale/half2float.c",
        "libswscale/hscale.c",
        "libswscale/hscale_fast_bilinear.c",
        "libswscale/input.c",
        "libswscale/options.c",
        "libswscale/output.c",
        "libswscale/rgb2rgb.c",
        "libswscale/slice.c",
        "libswscale/swscale.c",
        "libswscale/swscale_unscaled.c",
        "libswscale/utils.c",
        "libswscale/version.c",
        "libswscale/vscale.c",
        "libswscale/yuv2rgb.c",
    };

    // libswresample sources (audio format conversion)
    const swresample_sources = [_][]const u8{
        "libswresample/audioconvert.c",
        "libswresample/dither.c",
        "libswresample/options.c",
        "libswresample/rematrix.c",
        "libswresample/resample.c",
        "libswresample/resample_dsp.c",
        "libswresample/swresample.c",
        "libswresample/swresample_frame.c",
        "libswresample/version.c",
    };

    const avformat_sources = [_][]const u8{
        // Core muxing framework
        "libavformat/allformats.c",
        "libavformat/avio.c",
        "libavformat/aviobuf.c",
        "libavformat/format.c",
        "libavformat/id3v2.c",
        "libavformat/mux.c",
        "libavformat/mux_utils.c",
        "libavformat/options.c",
        "libavformat/protocols.c",
        "libavformat/url.c",
        "libavformat/utils.c",
        // MPEG-TS muxer (for MPEG-2 video + audio interleave)
        "libavformat/mpegtsenc.c",
        // Raw video/audio muxers
        "libavformat/rawenc.c",
        // Audio container muxers
        "libavformat/adtsenc.c",
        "libavformat/wavenc.c",
    };

    // Compile all FFmpeg source files
    const all_source_groups = [_][]const []const u8{
        &avutil_sources,
        &avcodec_sources,
        &swscale_sources,
        &swresample_sources,
        &avformat_sources,
    };

    for (all_source_groups) |sources| {
        for (sources) |src| {
            const full_path = std.fmt.allocPrint(b.allocator, "{s}/{s}", .{ ffmpeg_base, src }) catch continue;
            lib.addCSourceFile(.{
                .file = b.path(full_path),
                .flags = ffmpeg_flags,
            });
        }
    }

    // FFmpeg include paths
    // Main directory contains the library headers and generated config.h
    lib.addIncludePath(b.path(ffmpeg_base));
    // Individual library directories for internal headers
    lib.addIncludePath(b.path("vendor/ffmpeg/libavutil"));
    lib.addIncludePath(b.path("vendor/ffmpeg/libavcodec"));
    lib.addIncludePath(b.path("vendor/ffmpeg/libavcodec/hevc")); // HEVC decoder headers
    lib.addIncludePath(b.path("vendor/ffmpeg/libavformat"));
    lib.addIncludePath(b.path("vendor/ffmpeg/libswscale"));
    lib.addIncludePath(b.path("vendor/ffmpeg/libswresample"));
    // x264/x265 headers for FFmpeg's libx264.c and libx265.c wrappers
    lib.addIncludePath(b.path("vendor/x264"));
    lib.addIncludePath(b.path("vendor/x265/source"));
}

/// Add libjpeg-turbo source files to compilation (8-bit JPEG codec).
/// Vendor sources are downloaded by CI into vendor/libjpeg-turbo/src/.
/// Config headers jconfig.h and jconfigint.h are in vendor/libjpeg-turbo/.
///
/// Build configuration:
/// - Uses standard libjpeg 6b API (JPEG_LIB_VERSION 62)
/// - TurboJPEG API enabled for high-performance encoding/decoding
/// - SIMD disabled for portable cross-compilation
/// - 8-bit precision (BITS_IN_JSAMPLE 8)
///
/// Reference: https://github.com/libjpeg-turbo/libjpeg-turbo
fn addLibjpegTurboSources(lib: *std.Build.Step.Compile, b: *std.Build) void {
    const jpeg_base = "vendor/libjpeg-turbo/src";

    // libjpeg-turbo compilation flags - 8-bit build, no SIMD
    // Note: -O2 required before -D_FORTIFY_SOURCE=2 for glibc compatibility
    // NO_PUTENV disables PUTENV_S macro that uses setenv() which isn't available in strict C11
    const jpeg_flags = &[_][]const u8{
        "-std=c11",
        "-O2",
        "-fstack-protector-strong",
        "-D_FORTIFY_SOURCE=2",
        "-Wall",
        "-Wextra",
        "-Wno-error", // Downgrade errors to warnings for third-party code
        "-Wno-unused-parameter",
        "-Wno-sign-compare",
        "-Wno-shift-negative-value",
        "-Wno-implicit-fallthrough",
        "-Wno-missing-field-initializers",
        "-DNO_PUTENV", // Disable PUTENV_S macro (uses setenv not available in C11)
        "-DNO_GETENV", // Disable GETENV_S macro for consistency
    };

    // Core libjpeg sources (from libjpeg-turbo CMakeLists.txt)
    // These are the standard libjpeg API files
    const jpeg_sources = [_][]const u8{
        // Compression
        "jcapimin.c",
        "jcapistd.c",
        "jccoefct.c",
        "jccolor.c",
        "jcdctmgr.c",
        "jchuff.c",
        "jcicc.c",
        "jcinit.c",
        "jcmainct.c",
        "jcmarker.c",
        "jcmaster.c",
        "jcomapi.c",
        "jcparam.c",
        "jcphuff.c",
        "jcprepct.c",
        "jcsample.c",
        "jctrans.c",
        // Decompression
        "jdapimin.c",
        "jdapistd.c",
        "jdatadst.c",
        "jdatasrc.c",
        "jdcoefct.c",
        "jdcolor.c",
        "jddctmgr.c",
        "jdhuff.c",
        "jdicc.c",
        "jdinput.c",
        "jdmainct.c",
        "jdmarker.c",
        "jdmaster.c",
        "jdmerge.c",
        "jdphuff.c",
        "jdpostct.c",
        "jdsample.c",
        "jdtrans.c",
        // Common
        "jerror.c",
        "jfdctflt.c",
        "jfdctfst.c",
        "jfdctint.c",
        "jidctflt.c",
        "jidctfst.c",
        "jidctint.c",
        "jidctred.c",
        "jmemmgr.c",
        "jmemnobs.c", // No backing store memory manager
        "jquant1.c",
        "jquant2.c",
        "jutils.c",
        // Arithmetic coding (lossless JPEG support)
        "jaricom.c",
        "jcarith.c",
        "jdarith.c",
    };

    // TurboJPEG API sources
    const turbojpeg_sources = [_][]const u8{
        "turbojpeg.c",
        "transupp.c",
        "jdatadst-tj.c",
        "jdatasrc-tj.c",
    };

    // Compile core libjpeg sources
    for (jpeg_sources) |src| {
        const full_path = std.fmt.allocPrint(b.allocator, "{s}/{s}", .{ jpeg_base, src }) catch continue;
        lib.addCSourceFile(.{
            .file = b.path(full_path),
            .flags = jpeg_flags,
        });
    }

    // Compile TurboJPEG API
    for (turbojpeg_sources) |src| {
        const full_path = std.fmt.allocPrint(b.allocator, "{s}/{s}", .{ jpeg_base, src }) catch continue;
        lib.addCSourceFile(.{
            .file = b.path(full_path),
            .flags = jpeg_flags,
        });
    }

    // Include paths: config headers in vendor/libjpeg-turbo/, source in vendor/libjpeg-turbo/src/
    lib.addIncludePath(b.path("vendor/libjpeg-turbo"));
    lib.addIncludePath(b.path(jpeg_base));
}

/// Add 12-bit libjpeg-turbo source files to compilation.
/// Uses symbol prefixes to avoid collisions with 8-bit build.
///
/// The 12-bit build:
/// - Uses raw libjpeg API (no TurboJPEG - incompatible with WITH_12BIT)
/// - All public symbols prefixed with "jpeg12_" via -D flags
/// - SIMD disabled (not compatible with 12-bit)
/// - 12-bit precision (BITS_IN_JSAMPLE 12)
fn addLibjpegTurbo12Sources(lib: *std.Build.Step.Compile, b: *std.Build) void {
    const jpeg_base = "vendor/libjpeg-turbo/src";

    // 12-bit libjpeg-turbo compilation flags with symbol prefixes
    // NO_PUTENV disables PUTENV_S macro that uses setenv() which isn't available in strict C11
    const jpeg12_flags = &[_][]const u8{
        "-std=c11",
        "-O2",
        "-fstack-protector-strong",
        "-D_FORTIFY_SOURCE=2",
        "-Wall",
        "-Wextra",
        "-Wno-error", // Downgrade errors to warnings for third-party code
        "-Wno-unused-parameter",
        "-Wno-sign-compare",
        "-Wno-shift-negative-value",
        "-Wno-implicit-fallthrough",
        "-Wno-missing-field-initializers",
        "-DNO_PUTENV", // Disable PUTENV_S macro (uses setenv not available in C11)
        "-DNO_GETENV", // Disable GETENV_S macro for consistency
        // Enable 12-bit mode
        "-DWITH_12BIT=1",
        "-DBITS_IN_JSAMPLE=12",
        // Symbol prefixes for ALL public libjpeg functions to avoid collisions with 8-bit build
        // Initialization and destruction
        "-Djpeg_CreateCompress=jpeg12_jpeg_CreateCompress",
        "-Djpeg_CreateDecompress=jpeg12_jpeg_CreateDecompress",
        "-Djpeg_destroy_compress=jpeg12_jpeg_destroy_compress",
        "-Djpeg_destroy_decompress=jpeg12_jpeg_destroy_decompress",
        "-Djpeg_abort_compress=jpeg12_jpeg_abort_compress",
        "-Djpeg_abort_decompress=jpeg12_jpeg_abort_decompress",
        "-Djpeg_abort=jpeg12_jpeg_abort",
        "-Djpeg_destroy=jpeg12_jpeg_destroy",
        // Error handling
        "-Djpeg_std_error=jpeg12_jpeg_std_error",
        // Data source/destination
        "-Djpeg_stdio_dest=jpeg12_jpeg_stdio_dest",
        "-Djpeg_stdio_src=jpeg12_jpeg_stdio_src",
        "-Djpeg_mem_dest=jpeg12_jpeg_mem_dest",
        "-Djpeg_mem_src=jpeg12_jpeg_mem_src",
        // Compression parameter setup
        "-Djpeg_set_defaults=jpeg12_jpeg_set_defaults",
        "-Djpeg_set_colorspace=jpeg12_jpeg_set_colorspace",
        "-Djpeg_default_colorspace=jpeg12_jpeg_default_colorspace",
        "-Djpeg_set_quality=jpeg12_jpeg_set_quality",
        "-Djpeg_set_linear_quality=jpeg12_jpeg_set_linear_quality",
        "-Djpeg_default_qtables=jpeg12_jpeg_default_qtables",
        "-Djpeg_add_quant_table=jpeg12_jpeg_add_quant_table",
        "-Djpeg_quality_scaling=jpeg12_jpeg_quality_scaling",
        "-Djpeg_enable_lossless=jpeg12_jpeg_enable_lossless",
        "-Djpeg_simple_progression=jpeg12_jpeg_simple_progression",
        "-Djpeg_suppress_tables=jpeg12_jpeg_suppress_tables",
        "-Djpeg_alloc_quant_table=jpeg12_jpeg_alloc_quant_table",
        "-Djpeg_alloc_huff_table=jpeg12_jpeg_alloc_huff_table",
        // Compression
        "-Djpeg_start_compress=jpeg12_jpeg_start_compress",
        "-Djpeg_write_scanlines=jpeg12_jpeg_write_scanlines",
        "-Djpeg_finish_compress=jpeg12_jpeg_finish_compress",
        "-Djpeg_calc_jpeg_dimensions=jpeg12_jpeg_calc_jpeg_dimensions",
        "-Djpeg_write_raw_data=jpeg12_jpeg_write_raw_data",
        "-Djpeg_write_marker=jpeg12_jpeg_write_marker",
        "-Djpeg_write_m_header=jpeg12_jpeg_write_m_header",
        "-Djpeg_write_m_byte=jpeg12_jpeg_write_m_byte",
        "-Djpeg_write_tables=jpeg12_jpeg_write_tables",
        "-Djpeg_write_icc_profile=jpeg12_jpeg_write_icc_profile",
        "-Djpeg_write_coefficients=jpeg12_jpeg_write_coefficients",
        // Decompression
        "-Djpeg_read_header=jpeg12_jpeg_read_header",
        "-Djpeg_start_decompress=jpeg12_jpeg_start_decompress",
        "-Djpeg_read_scanlines=jpeg12_jpeg_read_scanlines",
        "-Djpeg_skip_scanlines=jpeg12_jpeg_skip_scanlines",
        "-Djpeg_crop_scanline=jpeg12_jpeg_crop_scanline",
        "-Djpeg_finish_decompress=jpeg12_jpeg_finish_decompress",
        "-Djpeg_read_raw_data=jpeg12_jpeg_read_raw_data",
        "-Djpeg_calc_output_dimensions=jpeg12_jpeg_calc_output_dimensions",
        "-Djpeg_core_output_dimensions=jpeg12_jpeg_core_output_dimensions",
        "-Djpeg_save_markers=jpeg12_jpeg_save_markers",
        "-Djpeg_set_marker_processor=jpeg12_jpeg_set_marker_processor",
        "-Djpeg_read_coefficients=jpeg12_jpeg_read_coefficients",
        "-Djpeg_copy_critical_parameters=jpeg12_jpeg_copy_critical_parameters",
        "-Djpeg_read_icc_profile=jpeg12_jpeg_read_icc_profile",
        // Buffered-image mode
        "-Djpeg_has_multiple_scans=jpeg12_jpeg_has_multiple_scans",
        "-Djpeg_start_output=jpeg12_jpeg_start_output",
        "-Djpeg_finish_output=jpeg12_jpeg_finish_output",
        "-Djpeg_input_complete=jpeg12_jpeg_input_complete",
        "-Djpeg_new_colormap=jpeg12_jpeg_new_colormap",
        "-Djpeg_consume_input=jpeg12_jpeg_consume_input",
        // Restart marker
        "-Djpeg_resync_to_restart=jpeg12_jpeg_resync_to_restart",
        // 12-bit specific entry points (also need prefixing to avoid self-collision)
        "-Djpeg12_write_scanlines=jpeg12_jpeg12_write_scanlines",
        "-Djpeg12_write_raw_data=jpeg12_jpeg12_write_raw_data",
        "-Djpeg12_read_scanlines=jpeg12_jpeg12_read_scanlines",
        "-Djpeg12_skip_scanlines=jpeg12_jpeg12_skip_scanlines",
        "-Djpeg12_crop_scanline=jpeg12_jpeg12_crop_scanline",
        "-Djpeg12_read_raw_data=jpeg12_jpeg12_read_raw_data",
        // 16-bit entry points (also in 12-bit build)
        "-Djpeg16_write_scanlines=jpeg12_jpeg16_write_scanlines",
        "-Djpeg16_read_scanlines=jpeg12_jpeg16_read_scanlines",
    };

    // Core libjpeg sources (same as 8-bit, but compiled with 12-bit flags)
    // Note: No TurboJPEG for 12-bit builds (incompatible)
    const jpeg12_sources = [_][]const u8{
        // Compression
        "jcapimin.c",
        "jcapistd.c",
        "jccoefct.c",
        "jccolor.c",
        "jcdctmgr.c",
        "jchuff.c",
        "jcicc.c",
        "jcinit.c",
        "jcmainct.c",
        "jcmarker.c",
        "jcmaster.c",
        "jcomapi.c",
        "jcparam.c",
        "jcphuff.c",
        "jcprepct.c",
        "jcsample.c",
        "jctrans.c",
        // Decompression
        "jdapimin.c",
        "jdapistd.c",
        "jdatadst.c",
        "jdatasrc.c",
        "jdcoefct.c",
        "jdcolor.c",
        "jddctmgr.c",
        "jdhuff.c",
        "jdicc.c",
        "jdinput.c",
        "jdmainct.c",
        "jdmarker.c",
        "jdmaster.c",
        "jdmerge.c",
        "jdphuff.c",
        "jdpostct.c",
        "jdsample.c",
        "jdtrans.c",
        // Common
        "jerror.c",
        "jfdctflt.c",
        "jfdctfst.c",
        "jfdctint.c",
        "jidctflt.c",
        "jidctfst.c",
        "jidctint.c",
        "jidctred.c",
        "jmemmgr.c",
        "jmemnobs.c",
        "jquant1.c",
        "jquant2.c",
        "jutils.c",
        // Arithmetic coding
        "jaricom.c",
        "jcarith.c",
        "jdarith.c",
    };

    // Compile 12-bit libjpeg sources with symbol prefixes
    for (jpeg12_sources) |src| {
        const full_path = std.fmt.allocPrint(b.allocator, "{s}/{s}", .{ jpeg_base, src }) catch continue;
        lib.addCSourceFile(.{
            .file = b.path(full_path),
            .flags = jpeg12_flags,
        });
    }

    // Include paths
    lib.addIncludePath(b.path("vendor/libjpeg-turbo"));
    lib.addIncludePath(b.path(jpeg_base));
}

/// Add Tesseract OCR source files to compilation.
/// Vendor sources are downloaded by CI into vendor/tesseract/src/ and vendor/leptonica/src/.
///
/// Build configuration:
/// - Tesseract 5.x with C API (capi.h)
/// - Leptonica for image processing (required dependency)
/// - No training tools or language data (handled at runtime)
///
/// Reference: https://github.com/tesseract-ocr/tesseract
fn addTesseractSources(lib: *std.Build.Step.Compile, b: *std.Build) void {
    // Tesseract 5.x source compilation
    // Requires: vendor/tesseract/src/ and vendor/leptonica/src/
    //
    // Source structure:
    // - src/ccmain/ - Main API (capi.cpp, tesseractclass.cpp, etc.)
    // - src/ccstruct/ - Data structures
    // - src/classify/ - Character classification
    // - src/dict/ - Dictionary lookup
    // - src/lstm/ - Neural network recognition
    // - src/textord/ - Text ordering
    // - src/wordrec/ - Word recognition
    //
    // Build requires ~800 C++ files and Leptonica dependency
    _ = lib;
    _ = b;
    @compileError("addTesseractSources not implemented - Tesseract source compilation requires ~800 C++ files");
}

/// Add Leptonica source files to compilation (image processing library).
/// Vendor sources are downloaded by CI into vendor/leptonica/src/.
///
/// Leptonica is required by Tesseract for image preprocessing.
/// Reference: https://github.com/DanBloomberg/leptonica
fn addLeptonicaSources(lib: *std.Build.Step.Compile, b: *std.Build) void {
    // Leptonica source compilation
    // Requires: vendor/leptonica/src/
    //
    // Source structure:
    // - src/ - ~200 C source files for image processing
    //
    // Dependencies: libjpeg, libpng, libtiff, zlib
    _ = lib;
    _ = b;
    @compileError("addLeptonicaSources not implemented - Leptonica source compilation requires ~200 C files and image library dependencies");
}
