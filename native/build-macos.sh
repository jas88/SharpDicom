#!/usr/bin/env bash
# build-macos.sh - Build SharpDicom native codecs on macOS
#
# Builds libsharpdicom_codecs.dylib for both x86_64 and arm64 using each
# library's native build system (CMake or configure) instead of manually
# compiling individual source files.
#
# Produces separate .dylib files per architecture:
#   native/runtimes/osx-arm64/native/libsharpdicom_codecs.dylib
#   native/runtimes/osx-x64/native/libsharpdicom_codecs.dylib
#
# Usage: ./native/build-macos.sh
#
# Requirements: Xcode command line tools, CMake, nasm (for libjpeg-turbo)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
VENDOR_DIR="$SCRIPT_DIR/vendor"
SRC_DIR="$SCRIPT_DIR/src"
BUILD_BASE="${BUILD_DIR:-$SCRIPT_DIR/build-macos}"
OUTPUT_DIR="${OUTPUT_DIR:-$SCRIPT_DIR/runtimes}"

NCPU=$(sysctl -n hw.ncpu)

# ============================================================
# Library versions (must match CI)
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

# ============================================================
# Checksum verification (macOS uses shasum)
# ============================================================
sha256check() {
    echo "$1  $2" | shasum -a 256 -c -
}

# ============================================================
# Download vendor sources
# ============================================================
download_vendors() {
    echo "=== Downloading vendor sources ==="
    mkdir -p "$VENDOR_DIR"
    cd "$VENDOR_DIR"

    # libjpeg-turbo
    if [ ! -d libjpeg-turbo/src ]; then
        echo "Downloading libjpeg-turbo ${LIBJPEG_VERSION}..."
        curl -sL "https://github.com/libjpeg-turbo/libjpeg-turbo/archive/refs/tags/${LIBJPEG_VERSION}.tar.gz" -o libjpeg-turbo.tar.gz
        sha256check "$LIBJPEG_SHA256" libjpeg-turbo.tar.gz
        tar xzf libjpeg-turbo.tar.gz && rm libjpeg-turbo.tar.gz
        mkdir -p libjpeg-turbo
        mv "libjpeg-turbo-${LIBJPEG_VERSION}" libjpeg-turbo/src
    fi

    # CharLS
    if [ ! -d charls/src ]; then
        echo "Downloading CharLS ${CHARLS_VERSION}..."
        curl -sL "https://github.com/team-charls/charls/archive/refs/tags/${CHARLS_VERSION}.tar.gz" -o charls.tar.gz
        sha256check "$CHARLS_SHA256" charls.tar.gz
        tar xzf charls.tar.gz && rm charls.tar.gz
        mkdir -p charls
        mv "charls-${CHARLS_VERSION}" charls/src
    fi

    # OpenJPEG
    if [ ! -d openjpeg/src ]; then
        echo "Downloading OpenJPEG ${OPENJPEG_VERSION}..."
        curl -sL "https://github.com/uclouvain/openjpeg/archive/refs/tags/v${OPENJPEG_VERSION}.tar.gz" -o openjpeg.tar.gz
        sha256check "$OPENJPEG_SHA256" openjpeg.tar.gz
        tar xzf openjpeg.tar.gz && rm openjpeg.tar.gz
        mkdir -p openjpeg
        mv "openjpeg-${OPENJPEG_VERSION}" openjpeg/src
    fi

    # FFmpeg (full source tree)
    if [ ! -f ffmpeg/configure ]; then
        echo "Downloading FFmpeg ${FFMPEG_VERSION}..."
        curl -sL "https://github.com/FFmpeg/FFmpeg/archive/refs/tags/n${FFMPEG_VERSION}.tar.gz" -o ffmpeg.tar.gz
        sha256check "$FFMPEG_SHA256" ffmpeg.tar.gz
        tar xzf ffmpeg.tar.gz && rm ffmpeg.tar.gz
        mkdir -p ffmpeg
        cp -r "FFmpeg-n${FFMPEG_VERSION}"/* ffmpeg/
        rm -rf "FFmpeg-n${FFMPEG_VERSION}"
    fi

    # x264
    if [ ! -f x264/configure ]; then
        echo "Downloading x264..."
        curl -sL "https://code.videolan.org/videolan/x264/-/archive/${X264_COMMIT}/x264-${X264_COMMIT}.tar.gz" -o x264.tar.gz
        sha256check "$X264_SHA256" x264.tar.gz
        tar xzf x264.tar.gz && rm x264.tar.gz
        mkdir -p x264
        cp -r "x264-${X264_COMMIT}"/* x264/
        rm -rf "x264-${X264_COMMIT}"
    fi

    # x265
    if [ ! -f x265/source/CMakeLists.txt ]; then
        echo "Downloading x265 ${X265_VERSION}..."
        curl -sL "https://bitbucket.org/multicoreware/x265_git/downloads/x265_${X265_VERSION}.tar.gz" -o x265.tar.gz
        sha256check "$X265_SHA256" x265.tar.gz
        tar xzf x265.tar.gz && rm x265.tar.gz
        mkdir -p x265
        cp -r "x265_${X265_VERSION}"/* x265/
        rm -rf "x265_${X265_VERSION}"

        # Patch x265 CMakeLists.txt for CMake 4.x compatibility
        # CMP0025/CMP0054 OLD behaviour is removed in CMake 4.x
        echo "Patching x265 for CMake 4.x compatibility..."
        sed -i '' 's/cmake_policy(SET CMP0025 OLD)/cmake_policy(SET CMP0025 NEW)/' \
            x265/source/CMakeLists.txt
        sed -i '' 's/cmake_policy(SET CMP0054 OLD)/cmake_policy(SET CMP0054 NEW)/' \
            x265/source/CMakeLists.txt
        # CMP0025 NEW reports Apple Clang as "AppleClang" not "Clang"
        sed -i '' 's/STREQUAL "Clang"/STREQUAL "Clang" OR ${CMAKE_CXX_COMPILER_ID} STREQUAL "AppleClang"/' \
            x265/source/CMakeLists.txt
        # cmake_minimum_required must come before project() in CMake 4.x
        sed -i '' 's/^project (x265)/cmake_minimum_required(VERSION 3.5)\nproject(x265)/' \
            x265/source/CMakeLists.txt
        sed -i '' '/^cmake_minimum_required (VERSION 2.8.8)/d' \
            x265/source/CMakeLists.txt
    fi

    # stb_image (header-only, no build needed)
    if [ ! -f stb/stb_image.h ]; then
        echo "Downloading stb_image..."
        mkdir -p stb
        curl -sL "https://raw.githubusercontent.com/nothings/stb/${STB_COMMIT}/stb_image.h" -o stb/stb_image.h
        sha256check "$STB_IMAGE_SHA256" stb/stb_image.h
    fi

    echo "=== All vendor sources ready ==="
}

# ============================================================
# Build all libraries for a single architecture
# ============================================================
build_arch() {
    local ARCH="$1"  # x86_64 or arm64
    local RID
    local HOST_TRIPLE
    case "$ARCH" in
        x86_64)
            RID="osx-x64"
            HOST_TRIPLE="x86_64-apple-darwin"
            ;;
        arm64)
            RID="osx-arm64"
            HOST_TRIPLE="aarch64-apple-darwin"
            ;;
        *)
            echo "Unknown arch: $ARCH" >&2
            exit 1
            ;;
    esac

    echo ""
    echo "============================================================"
    echo "Building for $ARCH ($RID)"
    echo "============================================================"

    local BUILD_DIR="$BUILD_BASE/$ARCH"
    local PREFIX="$BUILD_DIR/prefix"

    # Clean build dirs for this arch to ensure consistent results.
    # Vendor sources are preserved; only build artifacts are removed.
    rm -rf "$BUILD_DIR"
    mkdir -p "$BUILD_DIR" "$PREFIX"

    # ----------------------------------------------------------
    # 1. libjpeg-turbo (CMake)
    #    3.0+ builds multi-precision (8/12/16-bit) by default
    # ----------------------------------------------------------
    echo ""
    echo "--- libjpeg-turbo ($ARCH) ---"
    cmake -S "$VENDOR_DIR/libjpeg-turbo/src" -B "$BUILD_DIR/libjpeg-turbo" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_INSTALL_PREFIX="$PREFIX" \
        -DCMAKE_OSX_ARCHITECTURES="$ARCH" \
        -DENABLE_SHARED=OFF -DENABLE_STATIC=ON \
        -DWITH_JAVA=OFF -DWITH_TURBOJPEG=OFF
    cmake --build "$BUILD_DIR/libjpeg-turbo" -j"$NCPU"
    cmake --install "$BUILD_DIR/libjpeg-turbo"

    # ----------------------------------------------------------
    # 2. OpenJPEG (CMake)
    # ----------------------------------------------------------
    echo ""
    echo "--- OpenJPEG ($ARCH) ---"
    cmake -S "$VENDOR_DIR/openjpeg/src" -B "$BUILD_DIR/openjpeg" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_INSTALL_PREFIX="$PREFIX" \
        -DCMAKE_OSX_ARCHITECTURES="$ARCH" \
        -DBUILD_SHARED_LIBS=OFF \
        -DBUILD_CODEC=OFF -DBUILD_TESTING=OFF
    cmake --build "$BUILD_DIR/openjpeg" -j"$NCPU"
    cmake --install "$BUILD_DIR/openjpeg"

    # ----------------------------------------------------------
    # 3. CharLS (CMake)
    # ----------------------------------------------------------
    echo ""
    echo "--- CharLS ($ARCH) ---"
    cmake -S "$VENDOR_DIR/charls/src" -B "$BUILD_DIR/charls" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_INSTALL_PREFIX="$PREFIX" \
        -DCMAKE_OSX_ARCHITECTURES="$ARCH" \
        -DBUILD_SHARED_LIBS=OFF \
        -DCHARLS_BUILD_TESTS=OFF -DCHARLS_BUILD_SAMPLES=OFF \
        -DCHARLS_BUILD_FUZZ_TEST=OFF
    cmake --build "$BUILD_DIR/charls" -j"$NCPU"
    cmake --install "$BUILD_DIR/charls"

    # ----------------------------------------------------------
    # 4. x264 (configure)
    #    x264's configure doesn't support true out-of-tree builds,
    #    so we build in the source dir and distclean afterwards.
    # ----------------------------------------------------------
    echo ""
    echo "--- x264 ($ARCH) ---"
    (
        cd "$VENDOR_DIR/x264"
        # Clean any previous build for a different arch
        make distclean 2>/dev/null || true
        ./configure --prefix="$PREFIX" \
            --enable-static --disable-shared --disable-cli \
            --enable-pic \
            --host="$HOST_TRIPLE" \
            --extra-cflags="-arch $ARCH" \
            --extra-ldflags="-arch $ARCH"
        make -j"$NCPU"
        make install
        make distclean
    )

    # ----------------------------------------------------------
    # 5. x265 (CMake)
    # ----------------------------------------------------------
    echo ""
    echo "--- x265 ($ARCH) ---"
    cmake -S "$VENDOR_DIR/x265/source" -B "$BUILD_DIR/x265" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_INSTALL_PREFIX="$PREFIX" \
        -DCMAKE_OSX_ARCHITECTURES="$ARCH" \
        -DENABLE_SHARED=OFF -DENABLE_CLI=OFF \
        -DENABLE_ASSEMBLY=OFF
    cmake --build "$BUILD_DIR/x265" -j"$NCPU"
    cmake --install "$BUILD_DIR/x265"

    # ----------------------------------------------------------
    # 6. FFmpeg (configure)
    #    FFmpeg supports out-of-tree builds natively.
    #    Linked against x264 and x265 already installed to $PREFIX.
    #    Ensure no stale config.h in source dir (blocks out-of-tree builds).
    # ----------------------------------------------------------
    echo ""
    echo "--- FFmpeg ($ARCH) ---"
    rm -f "$VENDOR_DIR/ffmpeg/config.h" "$VENDOR_DIR/ffmpeg/config_components.h"
    mkdir -p "$BUILD_DIR/ffmpeg"
    (
        cd "$BUILD_DIR/ffmpeg"
        export PKG_CONFIG_PATH="$PREFIX/lib/pkgconfig:${PKG_CONFIG_PATH:-}"
        "$VENDOR_DIR/ffmpeg/configure" \
            --prefix="$PREFIX" \
            --arch="$ARCH" \
            --enable-cross-compile --target-os=darwin \
            --cc="cc -arch $ARCH" --cxx="c++ -arch $ARCH" \
            --pkg-config-flags="--static" \
            --enable-static --disable-shared \
            --enable-gpl --enable-libx264 --enable-libx265 \
            --disable-programs --disable-doc \
            --disable-htmlpages --disable-manpages --disable-podpages --disable-txtpages \
            --disable-network --disable-avdevice --disable-postproc \
            --disable-avfilter \
            --disable-everything \
            --disable-videotoolbox --disable-audiotoolbox \
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
            --extra-ldflags="-L$PREFIX/lib"
        make -j"$NCPU"
        make install
    )

    # ----------------------------------------------------------
    # 7. Compile wrapper sources (native/src/*.c)
    #    These provide the P/Invoke API that .NET calls into.
    # ----------------------------------------------------------
    echo ""
    echo "--- Wrapper sources ($ARCH) ---"
    local WRAPPER_DIR="$BUILD_DIR/wrappers"
    mkdir -p "$WRAPPER_DIR"

    # OpenJPEG installs headers in a versioned subdirectory
    local OPJ_INCDIR
    OPJ_INCDIR=$(find "$PREFIX/include" -name 'openjpeg-*' -type d | head -1)

    local WRAPPER_CFLAGS="-arch $ARCH -O2 -fPIC -std=c11 \
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
        # Some wrappers need relaxed warnings for vendor header compatibility
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
        cc $WRAPPER_CFLAGS $extra_flags \
            -c "$f" -o "$WRAPPER_DIR/$basename.o"
    done

    # ----------------------------------------------------------
    # 8. Link everything into a single shared library
    # ----------------------------------------------------------
    echo ""
    echo "--- Linking libsharpdicom_codecs.dylib ($ARCH) ---"
    local OUT_DIR="$OUTPUT_DIR/$RID/native"
    mkdir -p "$OUT_DIR"

    c++ -arch "$ARCH" -dynamiclib -shared \
        -o "$OUT_DIR/libsharpdicom_codecs.dylib" \
        "$WRAPPER_DIR"/*.o \
        -L"$PREFIX/lib" \
        -lavcodec -lavutil -lavformat -lswscale -lswresample \
        -ljpeg -lopenjp2 -lcharls -lx264 -lx265 \
        -lpthread -lm -lz -lc++ -liconv \
        -install_name @rpath/libsharpdicom_codecs.dylib \
        -Wl,-dead_strip

    # Strip debug symbols for smaller output
    strip -x "$OUT_DIR/libsharpdicom_codecs.dylib"

    local SIZE
    SIZE=$(du -h "$OUT_DIR/libsharpdicom_codecs.dylib" | cut -f1)
    echo "  Output: $OUT_DIR/libsharpdicom_codecs.dylib ($SIZE)"
}

# ============================================================
# Main
# ============================================================
main() {
    echo "SharpDicom native codec build for macOS"
    echo "========================================"

    # Verify required tools are available
    for tool in cmake cc c++ nasm make curl; do
        if ! command -v "$tool" &>/dev/null; then
            echo "ERROR: Required tool '$tool' not found. Install Xcode CLT and CMake." >&2
            exit 1
        fi
    done

    download_vendors

    build_arch arm64
    build_arch x86_64

    echo ""
    echo "============================================================"
    echo "Build complete!"
    echo "  runtimes/osx-arm64/native/libsharpdicom_codecs.dylib"
    echo "  runtimes/osx-x64/native/libsharpdicom_codecs.dylib"
    echo "============================================================"
}

main "$@"
