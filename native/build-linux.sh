#!/usr/bin/env bash
# build-linux.sh - Cross-compile SharpDicom native codecs on Linux
#
# Builds libsharpdicom_codecs for up to 4 targets:
#   linux-x64   (musl static, zero runtime dependencies)
#   linux-arm64 (musl static, zero runtime dependencies)
#   win-x64     (MinGW, depends on system UCRT/kernel32)
#   win-arm64   (MinGW, depends on system UCRT/kernel32)
#
# Uses Bootlin musl cross-compilers for Linux targets and
# LLVM-MinGW for Windows targets (same approach as libarchive.net).
#
# Usage:
#   ./native/build-linux.sh                  # build all 4 targets
#   ./native/build-linux.sh linux-x64        # build one target
#   ./native/build-linux.sh linux-x64 win-x64  # build specific targets
#
# Requirements: cmake, make, nasm, curl, xz (for decompressing toolchains)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
VENDOR_DIR="$SCRIPT_DIR/vendor"
SRC_DIR="$SCRIPT_DIR/src"
BUILD_BASE="${BUILD_DIR:-$SCRIPT_DIR/build-linux}"
OUTPUT_DIR="${OUTPUT_DIR:-$SCRIPT_DIR/runtimes}"
TOOLCHAIN_DIR="${TOOLCHAIN_DIR:-$HOME/toolchains}"
DOWNLOAD_CACHE="${DOWNLOAD_CACHE:-$HOME/downloads}"

NCPU=$(nproc 2>/dev/null || echo 4)

# ============================================================
# Library versions (must match CI and build-macos.sh)
# ============================================================
LIBJPEG_VERSION="3.0.4"
LIBJPEG_SHA256="0270f9496ad6d69e743f1e7b9e3e9398f5b4d606b6a47744df4b73df50f62e38"
CHARLS_VERSION="2.4.2"
CHARLS_SHA256="d1c2c35664976f1e43fec7764d72755e6a50a80f38eca70fcc7553cad4fe19d9"
OPENJPEG_VERSION="2.5.3"
OPENJPEG_SHA256="368fe0468228e767433c9ebdea82ad9d801a3ad1e4234421f352c8b06e7aa707"
FFMPEG_VERSION="7.1"
FFMPEG_SHA256="7ddad2d992bd250a6c56053c26029f7e728bebf0f37f80cf3f8a0e6ec706431a"
X264_COMMIT="31e19f92f00c7003fa115047ce50978bc98c3a0d"
X264_SHA256="d053c9d86988d6bc78237ca5205865c5ddf99c98ef4cd9927eec8f6d388f6dd9"
X265_VERSION="3.6"
X265_SHA256="663531f341c5389f460d730e62e10a4fcca3428ca2ca109693867bc5fe2e2807"
STB_COMMIT="f75e8d1cad7d90d72ef7a4661f1b994ef78b4e31"
STB_IMAGE_SHA256="594c2fe35d49488b4382dbfaec8f98366defca819d916ac95becf3e75f4200b3"

# Toolchain versions
BOOTLIN_RELEASE="stable-2025.08-1"
BOOTLIN_BASE_URL="https://toolchains.bootlin.com/downloads/releases/toolchains"
LLVM_MINGW_VERSION="20240619"
LLVM_MINGW_URL="https://github.com/mstorsjo/llvm-mingw/releases/download/${LLVM_MINGW_VERSION}/llvm-mingw-${LLVM_MINGW_VERSION}-ucrt-ubuntu-20.04-x86_64.tar.xz"

# ============================================================
# Checksum verification
# ============================================================
sha256check() {
    echo "$1  $2" | sha256sum -c -
}

# ============================================================
# Download vendor sources (shared with build-macos.sh logic)
# ============================================================
download_vendors() {
    echo "=== Downloading vendor sources ==="
    mkdir -p "$VENDOR_DIR"
    cd "$VENDOR_DIR"

    if [ ! -d libjpeg-turbo/src ]; then
        echo "Downloading libjpeg-turbo ${LIBJPEG_VERSION}..."
        curl -sL "https://github.com/libjpeg-turbo/libjpeg-turbo/archive/refs/tags/${LIBJPEG_VERSION}.tar.gz" -o libjpeg-turbo.tar.gz
        sha256check "$LIBJPEG_SHA256" libjpeg-turbo.tar.gz
        tar xzf libjpeg-turbo.tar.gz && rm libjpeg-turbo.tar.gz
        mkdir -p libjpeg-turbo
        mv "libjpeg-turbo-${LIBJPEG_VERSION}" libjpeg-turbo/src
    fi

    if [ ! -d charls/src ]; then
        echo "Downloading CharLS ${CHARLS_VERSION}..."
        curl -sL "https://github.com/team-charls/charls/archive/refs/tags/${CHARLS_VERSION}.tar.gz" -o charls.tar.gz
        sha256check "$CHARLS_SHA256" charls.tar.gz
        tar xzf charls.tar.gz && rm charls.tar.gz
        mkdir -p charls
        mv "charls-${CHARLS_VERSION}" charls/src
    fi

    if [ ! -d openjpeg/src ]; then
        echo "Downloading OpenJPEG ${OPENJPEG_VERSION}..."
        curl -sL "https://github.com/uclouvain/openjpeg/archive/refs/tags/v${OPENJPEG_VERSION}.tar.gz" -o openjpeg.tar.gz
        sha256check "$OPENJPEG_SHA256" openjpeg.tar.gz
        tar xzf openjpeg.tar.gz && rm openjpeg.tar.gz
        mkdir -p openjpeg
        mv "openjpeg-${OPENJPEG_VERSION}" openjpeg/src
    fi

    if [ ! -f ffmpeg/configure ]; then
        echo "Downloading FFmpeg ${FFMPEG_VERSION}..."
        curl -sL "https://github.com/FFmpeg/FFmpeg/archive/refs/tags/n${FFMPEG_VERSION}.tar.gz" -o ffmpeg.tar.gz
        sha256check "$FFMPEG_SHA256" ffmpeg.tar.gz
        tar xzf ffmpeg.tar.gz && rm ffmpeg.tar.gz
        mkdir -p ffmpeg
        cp -r "FFmpeg-n${FFMPEG_VERSION}"/* ffmpeg/
        rm -rf "FFmpeg-n${FFMPEG_VERSION}"
    fi

    if [ ! -f x264/configure ]; then
        echo "Downloading x264..."
        curl -sL "https://code.videolan.org/videolan/x264/-/archive/${X264_COMMIT}/x264-${X264_COMMIT}.tar.gz" -o x264.tar.gz
        sha256check "$X264_SHA256" x264.tar.gz
        tar xzf x264.tar.gz && rm x264.tar.gz
        mkdir -p x264
        cp -r "x264-${X264_COMMIT}"/* x264/
        rm -rf "x264-${X264_COMMIT}"
    fi

    if [ ! -f x265/source/CMakeLists.txt ]; then
        echo "Downloading x265 ${X265_VERSION}..."
        curl -sL "https://bitbucket.org/multicoreware/x265_git/downloads/x265_${X265_VERSION}.tar.gz" -o x265.tar.gz
        sha256check "$X265_SHA256" x265.tar.gz
        tar xzf x265.tar.gz && rm x265.tar.gz
        mkdir -p x265
        cp -r "x265_${X265_VERSION}"/* x265/
        rm -rf "x265_${X265_VERSION}"

        # Patch x265 for CMake 4.x compatibility
        echo "Patching x265 for CMake compatibility..."
        sed -i 's/cmake_policy(SET CMP0025 OLD)/cmake_policy(SET CMP0025 NEW)/' \
            x265/source/CMakeLists.txt
        sed -i 's/cmake_policy(SET CMP0054 OLD)/cmake_policy(SET CMP0054 NEW)/' \
            x265/source/CMakeLists.txt
        sed -i 's/STREQUAL "Clang"/STREQUAL "Clang" OR ${CMAKE_CXX_COMPILER_ID} STREQUAL "AppleClang"/' \
            x265/source/CMakeLists.txt
        # cmake_minimum_required must come before project()
        sed -i 's/^project (x265)/cmake_minimum_required(VERSION 3.5)\nproject(x265)/' \
            x265/source/CMakeLists.txt
        sed -i '/^cmake_minimum_required (VERSION 2.8.8)/d' \
            x265/source/CMakeLists.txt
    fi

    if [ ! -f stb/stb_image.h ]; then
        echo "Downloading stb_image..."
        mkdir -p stb
        curl -sL "https://raw.githubusercontent.com/nothings/stb/${STB_COMMIT}/stb_image.h" -o stb/stb_image.h
        sha256check "$STB_IMAGE_SHA256" stb/stb_image.h
    fi

    echo "=== All vendor sources ready ==="
}

# ============================================================
# Download cross-compilation toolchains
# ============================================================
download_toolchains() {
    local targets=("$@")
    mkdir -p "$TOOLCHAIN_DIR" "$DOWNLOAD_CACHE"

    for target in "${targets[@]}"; do
        case "$target" in
            linux-x64)
                if [ ! -d "$TOOLCHAIN_DIR/x86-64--musl" ]; then
                    echo "Downloading Bootlin x86_64 musl toolchain..."
                    local url="${BOOTLIN_BASE_URL}/x86-64/tarballs/x86-64--musl--${BOOTLIN_RELEASE}.tar.xz"
                    local archive="$DOWNLOAD_CACHE/bootlin-x64-musl.tar.xz"
                    [ -f "$archive" ] || curl -sL "$url" -o "$archive"
                    tar xJf "$archive" -C "$TOOLCHAIN_DIR"
                    mv "$TOOLCHAIN_DIR/x86-64--musl--${BOOTLIN_RELEASE}" "$TOOLCHAIN_DIR/x86-64--musl"
                fi
                ;;
            linux-arm64)
                if [ ! -d "$TOOLCHAIN_DIR/aarch64--musl" ]; then
                    echo "Downloading Bootlin aarch64 musl toolchain..."
                    local url="${BOOTLIN_BASE_URL}/aarch64/tarballs/aarch64--musl--${BOOTLIN_RELEASE}.tar.xz"
                    local archive="$DOWNLOAD_CACHE/bootlin-arm64-musl.tar.xz"
                    [ -f "$archive" ] || curl -sL "$url" -o "$archive"
                    tar xJf "$archive" -C "$TOOLCHAIN_DIR"
                    mv "$TOOLCHAIN_DIR/aarch64--musl--${BOOTLIN_RELEASE}" "$TOOLCHAIN_DIR/aarch64--musl"
                fi
                ;;
            win-x64|win-arm64)
                if [ ! -d "$TOOLCHAIN_DIR/llvm-mingw" ]; then
                    echo "Downloading LLVM-MinGW toolchain..."
                    local archive="$DOWNLOAD_CACHE/llvm-mingw.tar.xz"
                    [ -f "$archive" ] || curl -sL "$LLVM_MINGW_URL" -o "$archive"
                    tar xJf "$archive" -C "$TOOLCHAIN_DIR"
                    mv "$TOOLCHAIN_DIR/llvm-mingw-${LLVM_MINGW_VERSION}-ucrt-ubuntu-20.04-x86_64" \
                        "$TOOLCHAIN_DIR/llvm-mingw"
                fi
                ;;
        esac
    done
}

# ============================================================
# Set up toolchain variables for a target
# ============================================================
setup_toolchain() {
    local target="$1"

    case "$target" in
        linux-x64)
            TOOLCHAIN_PREFIX="$TOOLCHAIN_DIR/x86-64--musl"
            COMPILER_PREFIX="x86_64-buildroot-linux-musl"
            TOOLCHAIN_SYSROOT="$TOOLCHAIN_PREFIX/$COMPILER_PREFIX/sysroot"
            export CC="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-gcc"
            export CXX="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-g++"
            export AR="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-ar"
            export RANLIB="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-ranlib"
            export NM="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-nm"
            export STRIP="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-strip"
            export OBJCOPY="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-objcopy"
            CMAKE_SYSTEM_NAME="Linux"
            CMAKE_SYSTEM_PROCESSOR="x86_64"
            CONFIGURE_HOST="--host=${COMPILER_PREFIX}"
            FFMPEG_ARCH="x86_64"
            FFMPEG_CROSS_PREFIX="${TOOLCHAIN_PREFIX}/bin/${COMPILER_PREFIX}-"
            IS_LINUX=1
            IS_WINDOWS=0
            RID="linux-x64"
            LIB_EXT="so"
            LIB_PREFIX="lib"
            ;;
        linux-arm64)
            TOOLCHAIN_PREFIX="$TOOLCHAIN_DIR/aarch64--musl"
            COMPILER_PREFIX="aarch64-buildroot-linux-musl"
            TOOLCHAIN_SYSROOT="$TOOLCHAIN_PREFIX/$COMPILER_PREFIX/sysroot"
            export CC="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-gcc"
            export CXX="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-g++"
            export AR="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-ar"
            export RANLIB="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-ranlib"
            export NM="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-nm"
            export STRIP="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-strip"
            export OBJCOPY="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-objcopy"
            CMAKE_SYSTEM_NAME="Linux"
            CMAKE_SYSTEM_PROCESSOR="aarch64"
            CONFIGURE_HOST="--host=${COMPILER_PREFIX}"
            FFMPEG_ARCH="aarch64"
            FFMPEG_CROSS_PREFIX="${TOOLCHAIN_PREFIX}/bin/${COMPILER_PREFIX}-"
            IS_LINUX=1
            IS_WINDOWS=0
            RID="linux-arm64"
            LIB_EXT="so"
            LIB_PREFIX="lib"
            ;;
        win-x64)
            TOOLCHAIN_PREFIX="$TOOLCHAIN_DIR/llvm-mingw"
            COMPILER_PREFIX="x86_64-w64-mingw32"
            export CC="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-gcc"
            export CXX="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-g++"
            export AR="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-ar"
            export RANLIB="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-ranlib"
            export NM="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-nm"
            export STRIP="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-strip"
            export OBJCOPY="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-objcopy"
            CMAKE_SYSTEM_NAME="Windows"
            CMAKE_SYSTEM_PROCESSOR="AMD64"
            CONFIGURE_HOST="--host=${COMPILER_PREFIX}"
            FFMPEG_ARCH="x86_64"
            FFMPEG_CROSS_PREFIX="${TOOLCHAIN_PREFIX}/bin/${COMPILER_PREFIX}-"
            IS_LINUX=0
            IS_WINDOWS=1
            RID="win-x64"
            LIB_EXT="dll"
            LIB_PREFIX=""
            ;;
        win-arm64)
            TOOLCHAIN_PREFIX="$TOOLCHAIN_DIR/llvm-mingw"
            COMPILER_PREFIX="aarch64-w64-mingw32"
            export CC="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-gcc"
            export CXX="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-g++"
            export AR="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-ar"
            export RANLIB="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-ranlib"
            export NM="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-nm"
            export STRIP="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-strip"
            export OBJCOPY="$TOOLCHAIN_PREFIX/bin/${COMPILER_PREFIX}-objcopy"
            CMAKE_SYSTEM_NAME="Windows"
            CMAKE_SYSTEM_PROCESSOR="ARM64"
            CONFIGURE_HOST="--host=${COMPILER_PREFIX}"
            FFMPEG_ARCH="aarch64"
            FFMPEG_CROSS_PREFIX="${TOOLCHAIN_PREFIX}/bin/${COMPILER_PREFIX}-"
            IS_LINUX=0
            IS_WINDOWS=1
            RID="win-arm64"
            LIB_EXT="dll"
            LIB_PREFIX=""
            ;;
        *)
            echo "ERROR: Unknown target '$target'" >&2
            echo "Valid targets: linux-x64 linux-arm64 win-x64 win-arm64" >&2
            exit 1
            ;;
    esac

    export PATH="$TOOLCHAIN_PREFIX/bin:$PATH"
}

# ============================================================
# Generate CMake toolchain file for cross-compilation
# ============================================================
generate_cmake_toolchain() {
    local toolchain_file="$1"
    cat > "$toolchain_file" <<CMEOF
set(CMAKE_SYSTEM_NAME ${CMAKE_SYSTEM_NAME})
set(CMAKE_SYSTEM_PROCESSOR ${CMAKE_SYSTEM_PROCESSOR})
set(CMAKE_C_COMPILER ${CC})
set(CMAKE_CXX_COMPILER ${CXX})
set(CMAKE_AR ${AR})
set(CMAKE_RANLIB ${RANLIB})
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
CMEOF
}

# ============================================================
# Build all libraries for a single target
# ============================================================
build_target() {
    local target="$1"

    echo ""
    echo "============================================================"
    echo "Building for $target"
    echo "============================================================"

    setup_toolchain "$target"

    echo "  CC:  $CC"
    echo "  CXX: $CXX"
    $CC --version | head -1

    local BUILD_DIR="$BUILD_BASE/$target"
    local PREFIX="$BUILD_DIR/prefix"

    rm -rf "$BUILD_DIR"
    mkdir -p "$BUILD_DIR" "$PREFIX"

    # Generate CMake toolchain file
    local TOOLCHAIN_FILE="$BUILD_DIR/toolchain.cmake"
    generate_cmake_toolchain "$TOOLCHAIN_FILE"

    local COMMON_CMAKE_ARGS=(
        -DCMAKE_BUILD_TYPE=Release
        -DCMAKE_INSTALL_PREFIX="$PREFIX"
        -DCMAKE_TOOLCHAIN_FILE="$TOOLCHAIN_FILE"
    )

    # ----------------------------------------------------------
    # 1. libjpeg-turbo (CMake)
    # ----------------------------------------------------------
    echo ""
    echo "--- libjpeg-turbo ($target) ---"
    cmake -S "$VENDOR_DIR/libjpeg-turbo/src" -B "$BUILD_DIR/libjpeg-turbo" \
        "${COMMON_CMAKE_ARGS[@]}" \
        -DENABLE_SHARED=OFF -DENABLE_STATIC=ON \
        -DWITH_JAVA=OFF -DWITH_TURBOJPEG=OFF \
        -DWITH_SIMD=OFF
    cmake --build "$BUILD_DIR/libjpeg-turbo" -j"$NCPU"
    cmake --install "$BUILD_DIR/libjpeg-turbo"

    # ----------------------------------------------------------
    # 2. OpenJPEG (CMake)
    # ----------------------------------------------------------
    echo ""
    echo "--- OpenJPEG ($target) ---"
    cmake -S "$VENDOR_DIR/openjpeg/src" -B "$BUILD_DIR/openjpeg" \
        "${COMMON_CMAKE_ARGS[@]}" \
        -DBUILD_SHARED_LIBS=OFF \
        -DBUILD_CODEC=OFF -DBUILD_TESTING=OFF
    cmake --build "$BUILD_DIR/openjpeg" -j"$NCPU"
    cmake --install "$BUILD_DIR/openjpeg"

    # ----------------------------------------------------------
    # 3. CharLS (CMake, C++17)
    # ----------------------------------------------------------
    echo ""
    echo "--- CharLS ($target) ---"
    cmake -S "$VENDOR_DIR/charls/src" -B "$BUILD_DIR/charls" \
        "${COMMON_CMAKE_ARGS[@]}" \
        -DBUILD_SHARED_LIBS=OFF \
        -DCHARLS_BUILD_TESTS=OFF -DCHARLS_BUILD_SAMPLES=OFF \
        -DCHARLS_BUILD_FUZZ_TEST=OFF
    cmake --build "$BUILD_DIR/charls" -j"$NCPU"
    cmake --install "$BUILD_DIR/charls"

    # ----------------------------------------------------------
    # 4. x264 (configure)
    # ----------------------------------------------------------
    echo ""
    echo "--- x264 ($target) ---"
    (
        cd "$VENDOR_DIR/x264"
        make distclean 2>/dev/null || true

        local x264_extra_flags=""
        if [ "$IS_WINDOWS" = "1" ]; then
            x264_extra_flags="--disable-win32thread"
        fi

        ./configure --prefix="$PREFIX" \
            --enable-static --disable-shared --disable-cli \
            --enable-pic \
            --cross-prefix="${FFMPEG_CROSS_PREFIX}" \
            $CONFIGURE_HOST \
            $x264_extra_flags
        make -j"$NCPU"
        make install
        make distclean
    )

    # ----------------------------------------------------------
    # 5. x265 (CMake, C++14)
    # ----------------------------------------------------------
    echo ""
    echo "--- x265 ($target) ---"
    cmake -S "$VENDOR_DIR/x265/source" -B "$BUILD_DIR/x265" \
        "${COMMON_CMAKE_ARGS[@]}" \
        -DENABLE_SHARED=OFF -DENABLE_CLI=OFF \
        -DENABLE_ASSEMBLY=OFF
    cmake --build "$BUILD_DIR/x265" -j"$NCPU"
    cmake --install "$BUILD_DIR/x265"

    # ----------------------------------------------------------
    # 6. FFmpeg (configure)
    # ----------------------------------------------------------
    echo ""
    echo "--- FFmpeg ($target) ---"
    rm -f "$VENDOR_DIR/ffmpeg/config.h" "$VENDOR_DIR/ffmpeg/config_components.h"
    mkdir -p "$BUILD_DIR/ffmpeg"
    (
        cd "$BUILD_DIR/ffmpeg"
        export PKG_CONFIG_PATH="$PREFIX/lib/pkgconfig:${PKG_CONFIG_PATH:-}"

        local ffmpeg_target_os="linux"
        local ffmpeg_extra_flags=""
        if [ "$IS_WINDOWS" = "1" ]; then
            ffmpeg_target_os="mingw32"
        fi

        "$VENDOR_DIR/ffmpeg/configure" \
            --prefix="$PREFIX" \
            --arch="$FFMPEG_ARCH" \
            --enable-cross-compile --target-os="$ffmpeg_target_os" \
            --cross-prefix="${FFMPEG_CROSS_PREFIX}" \
            --pkg-config-flags="--static" \
            --enable-static --disable-shared \
            --enable-gpl --enable-libx264 --enable-libx265 \
            --enable-pic \
            --disable-programs --disable-doc \
            --disable-htmlpages --disable-manpages --disable-podpages --disable-txtpages \
            --disable-network --disable-avdevice --disable-postproc \
            --disable-avfilter \
            --disable-everything \
            --enable-decoder=mpeg1video --enable-decoder=mpeg2video \
            --enable-decoder=h264 --enable-decoder=hevc \
            --enable-decoder=mpeg4 \
            --enable-decoder=aac --enable-decoder=pcm_s16le --enable-decoder=pcm_s16be \
            --enable-encoder=mpeg2video --enable-encoder=libx264 --enable-encoder=libx265 \
            --enable-encoder=aac --enable-encoder=pcm_s16le \
            --enable-parser=h264 --enable-parser=hevc \
            --enable-parser=mpeg4video --enable-parser=mpegvideo --enable-parser=aac \
            --enable-muxer=mpegts --enable-muxer=h264 --enable-muxer=hevc \
            --enable-muxer=wav --enable-muxer=adts --enable-muxer=rawvideo \
            --enable-demuxer=mpegts --enable-demuxer=h264 --enable-demuxer=hevc \
            --enable-demuxer=wav --enable-demuxer=aac \
            --enable-protocol=file \
            --enable-swscale --enable-swresample \
            --extra-cflags="-I$PREFIX/include" \
            --extra-ldflags="-L$PREFIX/lib" \
            $ffmpeg_extra_flags
        make -j"$NCPU"
        make install
    )

    # ----------------------------------------------------------
    # 7. Compile wrapper sources (native/src/*.c)
    # ----------------------------------------------------------
    echo ""
    echo "--- Wrapper sources ($target) ---"
    local WRAPPER_DIR="$BUILD_DIR/wrappers"
    mkdir -p "$WRAPPER_DIR"

    # OpenJPEG installs headers in a versioned subdirectory
    local OPJ_INCDIR
    OPJ_INCDIR=$(find "$PREFIX/include" -name 'openjpeg-*' -type d | head -1)

    local WRAPPER_CFLAGS="-O2 -fPIC -std=c11 \
        -ffunction-sections -fdata-sections \
        -I$PREFIX/include \
        ${OPJ_INCDIR:+-I$OPJ_INCDIR} \
        -I$SRC_DIR \
        -I$VENDOR_DIR/stb \
        -DSHARPDICOM_WITH_JPEG -DSHARPDICOM_WITH_JPEG12 \
        -DSHARPDICOM_WITH_J2K -DSHARPDICOM_WITH_JLS \
        -DSHARPDICOM_WITH_STB_IMAGE \
        -DSHARPDICOM_WITH_FFMPEG_ENC -DSHARPDICOM_WITH_MPEG \
        -DSHARPDICOM_HAS_OPENJPEG -DSHARPDICOM_HAS_CHARLS \
        -DSHARPDICOM_HAS_FFMPEG \
        -Wall -Wextra"

    for f in "$SRC_DIR"/*.c; do
        local basename
        basename="$(basename "${f%.c}")"
        echo "    CC $basename.c"
        local extra_flags=""
        case "$basename" in
            stb_image_wrapper)
                extra_flags="-Wno-unused-function -Wno-sign-compare"
                ;;
            video_encoder)
                extra_flags="-Wno-deprecated-declarations"
                ;;
        esac
        # shellcheck disable=SC2086
        $CC $WRAPPER_CFLAGS $extra_flags \
            -c "$f" -o "$WRAPPER_DIR/$basename.o"
    done

    # ----------------------------------------------------------
    # 8. Link everything into a single shared library
    # ----------------------------------------------------------
    echo ""
    echo "--- Linking ${LIB_PREFIX}sharpdicom_codecs.${LIB_EXT} ($target) ---"
    local OUT_DIR="$OUTPUT_DIR/$RID/native"
    mkdir -p "$OUT_DIR"
    local OUTPUT_LIB="$OUT_DIR/${LIB_PREFIX}sharpdicom_codecs.${LIB_EXT}"

    if [ "$IS_LINUX" = "1" ]; then
        # Linux: statically link musl libc for zero runtime dependencies.
        # musl bundles libc, libm, libpthread, libdl all into libc.a.
        # Use --start-group/--end-group for multi-pass symbol resolution
        # between all static libraries, libgcc (compiler intrinsics), and musl libc.
        local LIBGCC_PATH
        LIBGCC_PATH=$($CC -print-libgcc-file-name)
        # Also need libstdc++ for C++ code (CharLS, x265)
        local LIBSTDCXX_PATH
        LIBSTDCXX_PATH=$($CXX -print-file-name=libstdc++.a)

        $CC -shared -o "$OUTPUT_LIB" \
            -Wl,--gc-sections \
            -Wl,--whole-archive "$WRAPPER_DIR"/*.o -Wl,--no-whole-archive \
            -Wl,--start-group \
            "$PREFIX/lib/libavcodec.a" \
            "$PREFIX/lib/libavformat.a" \
            "$PREFIX/lib/libavutil.a" \
            "$PREFIX/lib/libswscale.a" \
            "$PREFIX/lib/libswresample.a" \
            "$PREFIX/lib/libjpeg.a" \
            "$PREFIX/lib/libopenjp2.a" \
            "$PREFIX/lib/libcharls.a" \
            "$PREFIX/lib/libx264.a" \
            "$PREFIX/lib/libx265.a" \
            "$LIBSTDCXX_PATH" \
            "$LIBGCC_PATH" \
            "${TOOLCHAIN_SYSROOT}/lib/libc.a" \
            -Wl,--end-group \
            -nostdlib

    elif [ "$IS_WINDOWS" = "1" ]; then
        # Windows: link against system libraries
        $CXX -shared -o "$OUTPUT_LIB" \
            -Wl,--gc-sections \
            "$WRAPPER_DIR"/*.o \
            -Wl,--start-group \
            "$PREFIX/lib/libavcodec.a" \
            "$PREFIX/lib/libavformat.a" \
            "$PREFIX/lib/libavutil.a" \
            "$PREFIX/lib/libswscale.a" \
            "$PREFIX/lib/libswresample.a" \
            "$PREFIX/lib/libjpeg.a" \
            "$PREFIX/lib/libopenjp2.a" \
            "$PREFIX/lib/libcharls.a" \
            "$PREFIX/lib/libx264.a" \
            "$PREFIX/lib/libx265.a" \
            -Wl,--end-group \
            -static-libgcc -static-libstdc++ \
            -lws2_32 -lbcrypt -lkernel32
    fi

    $STRIP -x "$OUTPUT_LIB"

    local SIZE
    SIZE=$(du -h "$OUTPUT_LIB" | cut -f1)
    echo "  Output: $OUTPUT_LIB ($SIZE)"

    # ----------------------------------------------------------
    # 9. Verify dependencies
    # ----------------------------------------------------------
    echo ""
    echo "--- Verifying dependencies ($target) ---"
    if [ "$IS_LINUX" = "1" ]; then
        # Linux: should report "statically linked" or "not a dynamic executable"
        local ldd_output
        ldd_output=$(ldd "$OUTPUT_LIB" 2>&1 || true)
        if echo "$ldd_output" | grep -qvE "not a dynamic executable|statically linked|ldd:"; then
            echo "WARNING: Unexpected dynamic dependencies:"
            echo "$ldd_output"
        else
            echo "  OK: No dynamic dependencies (fully static)"
        fi
    elif [ "$IS_WINDOWS" = "1" ]; then
        # Windows: show DLL imports
        echo "  DLL dependencies:"
        ${COMPILER_PREFIX}-objdump -p "$OUTPUT_LIB" 2>/dev/null | grep "DLL Name:" | sed 's/^/    /' || echo "    (none)"
    fi
}

# ============================================================
# Main
# ============================================================
main() {
    echo "SharpDicom native codec cross-compilation build"
    echo "================================================"

    # Verify required tools
    for tool in cmake make curl; do
        if ! command -v "$tool" &>/dev/null; then
            echo "ERROR: Required tool '$tool' not found." >&2
            exit 1
        fi
    done

    # Parse targets from args, default to all
    local targets=()
    if [ $# -gt 0 ]; then
        targets=("$@")
    else
        targets=(linux-x64 linux-arm64 win-x64 win-arm64)
    fi

    echo "Targets: ${targets[*]}"

    download_vendors
    download_toolchains "${targets[@]}"

    for target in "${targets[@]}"; do
        build_target "$target"
    done

    echo ""
    echo "============================================================"
    echo "Build complete!"
    for target in "${targets[@]}"; do
        case "$target" in
            linux-*)  echo "  runtimes/$target/native/libsharpdicom_codecs.so" ;;
            win-*)    echo "  runtimes/$target/native/sharpdicom_codecs.dll" ;;
        esac
    done
    echo "============================================================"
}

main "$@"
