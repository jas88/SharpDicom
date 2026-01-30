const std = @import("std");

/// SharpDicom native codecs build script
/// Cross-compiles to 6 target platforms: win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64
pub fn build(b: *std.Build) void {
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

        // Add C source files
        lib.addCSourceFile(.{
            .file = b.path("src/sharpdicom_codecs.c"),
            .flags = &.{
                "-std=c11",
                "-fstack-protector-strong", // Security hardening
                "-D_FORTIFY_SOURCE=2",
                "-Wall",
                "-Wextra",
                "-Werror",
            },
        });

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

    native_lib.addCSourceFile(.{
        .file = b.path("src/sharpdicom_codecs.c"),
        .flags = &.{
            "-std=c11",
            "-fstack-protector-strong",
            "-Wall",
            "-Wextra",
            "-Werror",
        },
    });

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
