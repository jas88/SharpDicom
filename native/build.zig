const std = @import("std");

/// SharpDicom native codecs build script
/// Cross-compiles to 6 target platforms: win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64
///
/// Vendor libraries:
/// - libjpeg-turbo: vendor/libjpeg-turbo/src (downloaded in CI)
/// - OpenJPEG: vendor/openjpeg/src (downloaded in CI)
pub fn build(b: *std.Build) void {
    // Check for vendor library sources
    const libjpeg_path = "vendor/libjpeg-turbo/src";
    const openjpeg_path = "vendor/openjpeg/src";
    const have_libjpeg = detectVendorLibrary(libjpeg_path);
    const have_openjpeg = detectVendorLibrary(openjpeg_path);

    if (have_libjpeg) {
        std.debug.print("Found libjpeg-turbo at {s}\n", .{libjpeg_path});
    }
    if (have_openjpeg) {
        std.debug.print("Found OpenJPEG at {s}\n", .{openjpeg_path});
    }
    // Target configurations for all supported platforms
    const targets = [_]std.Target.Query{
        // Windows x64
        .{
            .cpu_arch = .x86_64,
            .os_tag = .windows,
            .abi = .msvc,
        },
        // Windows ARM64
        .{
            .cpu_arch = .aarch64,
            .os_tag = .windows,
            .abi = .msvc,
        },
        // Linux x64 (musl for zero dependencies)
        .{
            .cpu_arch = .x86_64,
            .os_tag = .linux,
            .abi = .musl,
        },
        // Linux ARM64 (musl for zero dependencies)
        .{
            .cpu_arch = .aarch64,
            .os_tag = .linux,
            .abi = .musl,
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

    const optimize = b.standardOptimizeOption(.{
        .preferred_optimize_mode = .ReleaseFast,
    });

    // Build shared library for each target
    for (targets) |target_query| {
        const target = b.resolveTargetQuery(target_query);
        const lib = b.addSharedLibrary(.{
            .name = "sharpdicom_codecs",
            .target = target,
            .optimize = optimize,
            .pic = true, // Position-independent code for ASLR
        });

        // Build flags common to all source files
        const common_flags = &[_][]const u8{
            "-std=c11",
            "-fstack-protector-strong", // Security hardening
            "-D_FORTIFY_SOURCE=2",
            "-Wall",
            "-Wextra",
            "-Werror",
        };

        // Core flags with JPEG feature enabled
        const jpeg_flags = common_flags ++ &[_][]const u8{
            "-DSHARPDICOM_WITH_JPEG",
        };

        // Add C source files (core)
        lib.addCSourceFile(.{
            .file = b.path("src/sharpdicom_codecs.c"),
            .flags = jpeg_flags,
        });

        // JPEG wrapper (libjpeg-turbo)
        if (have_libjpeg) {
            lib.addCSourceFile(.{
                .file = b.path("src/jpeg_wrapper.c"),
                .flags = jpeg_flags,
            });
            // Add libjpeg-turbo include path
            lib.addIncludePath(b.path(libjpeg_path));
            // Link against turbojpeg library
            lib.linkSystemLibrary("turbojpeg");
        } else {
            // Build stub version (JPEG functions will error at runtime)
            lib.addCSourceFile(.{
                .file = b.path("src/jpeg_wrapper.c"),
                .flags = common_flags,
            });
        }

        // J2K wrapper (OpenJPEG)
        if (have_openjpeg) {
            lib.addCSourceFile(.{
                .file = b.path("src/j2k_wrapper.c"),
                .flags = common_flags ++ &[_][]const u8{
                    "-DSHARPDICOM_HAS_OPENJPEG",
                    "-DSHARPDICOM_WITH_J2K",
                },
            });

            // Add OpenJPEG include path
            lib.addIncludePath(b.path("vendor/openjpeg-src/src/lib/openjp2"));

            // Add OpenJPEG source files needed for compilation
            addOpenJpegSources(lib, b, common_flags);
        } else {
            // Build stub version
            lib.addCSourceFile(.{
                .file = b.path("src/j2k_wrapper.c"),
                .flags = common_flags,
            });
        }

        // Include paths
        lib.addIncludePath(b.path("src"));

        // Install the library to zig-out with platform-specific naming
        const rid = getRuntimeId(target_query);
        const install_step = b.addInstallArtifact(lib, .{
            .dest_dir = .{ .custom = rid },
        });
        b.getInstallStep().dependOn(&install_step.step);
    }

    // Native test executable (for local platform only)
    const native_target = b.standardTargetOptions(.{});
    const test_exe = b.addExecutable(.{
        .name = "test_version",
        .target = native_target,
        .optimize = optimize,
    });

    test_exe.addCSourceFile(.{
        .file = b.path("test/test_version.c"),
        .flags = &.{
            "-std=c11",
            "-Wall",
            "-Wextra",
        },
    });

    test_exe.addIncludePath(b.path("src"));

    // Link against the native platform's library
    test_exe.addCSourceFile(.{
        .file = b.path("src/sharpdicom_codecs.c"),
        .flags = &.{
            "-std=c11",
            "-Wall",
            "-Wextra",
        },
    });

    // Add j2k_wrapper stub for tests (without OpenJPEG for simplicity)
    test_exe.addCSourceFile(.{
        .file = b.path("src/j2k_wrapper.c"),
        .flags = &.{
            "-std=c11",
            "-Wall",
            "-Wextra",
        },
    });

    const test_install = b.addInstallArtifact(test_exe, .{});

    // Test step
    const test_step = b.step("test", "Run native tests");
    const run_test = b.addRunArtifact(test_exe);
    test_step.dependOn(&test_install.step);
    test_step.dependOn(&run_test.step);

    // Single-platform build step (for development)
    const single_step = b.step("native", "Build for native platform only");
    const native_lib = b.addSharedLibrary(.{
        .name = "sharpdicom_codecs",
        .target = native_target,
        .optimize = optimize,
        .pic = true,
    });

    const native_flags = &[_][]const u8{
        "-std=c11",
        "-fstack-protector-strong",
        "-Wall",
        "-Wextra",
        "-Werror",
    };

    native_lib.addCSourceFile(.{
        .file = b.path("src/sharpdicom_codecs.c"),
        .flags = native_flags,
    });

    // J2K wrapper for native build
    if (have_openjpeg) {
        native_lib.addCSourceFile(.{
            .file = b.path("src/j2k_wrapper.c"),
            .flags = native_flags ++ &[_][]const u8{
                "-DSHARPDICOM_HAS_OPENJPEG",
                "-DSHARPDICOM_WITH_J2K",
            },
        });
        native_lib.addIncludePath(b.path("vendor/openjpeg-src/src/lib/openjp2"));
        addOpenJpegSources(native_lib, b, native_flags);
    } else {
        native_lib.addCSourceFile(.{
            .file = b.path("src/j2k_wrapper.c"),
            .flags = native_flags,
        });
    }

    native_lib.addIncludePath(b.path("src"));

    const native_install = b.addInstallArtifact(native_lib, .{});
    single_step.dependOn(&native_install.step);
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
fn addOpenJpegSources(lib: *std.Build.Step.Compile, b: *std.Build, common_flags: []const []const u8) void {
    const opj_base = "vendor/openjpeg-src/src/lib/openjp2";

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

    // OpenJPEG-specific flags (suppress warnings from third-party code)
    const opj_flags = common_flags ++ &[_][]const u8{
        "-Wno-unused-parameter",
        "-Wno-sign-compare",
        "-Wno-implicit-fallthrough",
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
