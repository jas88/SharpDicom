# CMakeLists.txt for nvJPEG2000 Wrapper
#
# This builds the CUDA-based nvJPEG2000 wrapper as a separate shared library.
# The main sharpdicom_codecs library loads this dynamically at runtime.
#
# Requirements:
# - CUDA Toolkit 11.0 or later
# - nvJPEG2000 library (bundled with CUDA 11.0+ or separate download)
# - CMake 3.18+

cmake_minimum_required(VERSION 3.18)
project(nvjpeg2k_wrapper LANGUAGES C CUDA)

# Require CUDA 11.0+
find_package(CUDAToolkit 11.0 REQUIRED)

# Check for nvJPEG2000
find_library(NVJPEG2K_LIBRARY
    NAMES nvjpeg2k
    HINTS
        ${CUDAToolkit_LIBRARY_DIR}
        ${CUDA_TOOLKIT_ROOT_DIR}/lib64
        ${CUDA_TOOLKIT_ROOT_DIR}/lib
        /usr/local/cuda/lib64
    REQUIRED
)

find_path(NVJPEG2K_INCLUDE_DIR
    NAMES nvjpeg2k.h
    HINTS
        ${CUDAToolkit_INCLUDE_DIRS}
        ${CUDA_TOOLKIT_ROOT_DIR}/include
        /usr/local/cuda/include
    REQUIRED
)

message(STATUS "nvJPEG2000 library: ${NVJPEG2K_LIBRARY}")
message(STATUS "nvJPEG2000 include: ${NVJPEG2K_INCLUDE_DIR}")

# Output library name
set(TARGET_NAME nvjpeg2k_wrapper)

# Create shared library
add_library(${TARGET_NAME} SHARED
    nvjpeg2k_wrapper.c
)

# Set CUDA architectures (compute capability 5.0+)
# 50 = Maxwell (GTX 750 Ti, GTX 9xx)
# 60 = Pascal (GTX 10xx, Tesla P100)
# 70 = Volta (Tesla V100)
# 75 = Turing (RTX 20xx, Tesla T4)
# 80 = Ampere (RTX 30xx, A100)
# 86 = Ampere (RTX 30xx laptop variants)
# 89 = Ada Lovelace (RTX 40xx)
# 90 = Hopper (H100)
set(CMAKE_CUDA_ARCHITECTURES 50 60 70 75 80 86 89 90)

# Define HAVE_NVJPEG2K to enable the implementation
target_compile_definitions(${TARGET_NAME} PRIVATE
    HAVE_NVJPEG2K
    NVJPEG2K_WRAPPER_EXPORTS
)

# Include directories
target_include_directories(${TARGET_NAME} PRIVATE
    ${NVJPEG2K_INCLUDE_DIR}
    ${CMAKE_CURRENT_SOURCE_DIR}
)

# Link libraries
target_link_libraries(${TARGET_NAME} PRIVATE
    ${NVJPEG2K_LIBRARY}
    CUDA::cudart
)

# Compiler flags
target_compile_options(${TARGET_NAME} PRIVATE
    $<$<COMPILE_LANGUAGE:C>:-Wall -Wextra -fvisibility=hidden>
    $<$<COMPILE_LANGUAGE:CUDA>:-Xcompiler=-Wall,-Wextra,-fvisibility=hidden>
)

# Security hardening
if(UNIX AND NOT APPLE)
    target_compile_options(${TARGET_NAME} PRIVATE
        $<$<COMPILE_LANGUAGE:C>:-fstack-protector-strong -D_FORTIFY_SOURCE=2>
    )
    target_link_options(${TARGET_NAME} PRIVATE
        -Wl,-z,relro,-z,now
    )
endif()

# Position-independent code
set_target_properties(${TARGET_NAME} PROPERTIES
    POSITION_INDEPENDENT_CODE ON
    C_VISIBILITY_PRESET hidden
    CUDA_VISIBILITY_PRESET hidden
)

# SONAME for versioning
set_target_properties(${TARGET_NAME} PROPERTIES
    VERSION 1.0.0
    SOVERSION 1
)

# Output directory
set_target_properties(${TARGET_NAME} PROPERTIES
    LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin
    ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib
)

# Install rules
install(TARGETS ${TARGET_NAME}
    LIBRARY DESTINATION lib
    RUNTIME DESTINATION bin
    ARCHIVE DESTINATION lib
)

install(FILES nvjpeg2k_wrapper.h
    DESTINATION include
)

# Test executable
option(BUILD_TESTS "Build test executable" ON)
if(BUILD_TESTS)
    add_executable(test_nvjpeg2k_wrapper test_nvjpeg2k_wrapper.c)
    target_include_directories(test_nvjpeg2k_wrapper PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}
    )
    target_link_libraries(test_nvjpeg2k_wrapper PRIVATE
        ${TARGET_NAME}
    )
endif()

# Summary
message(STATUS "")
message(STATUS "nvJPEG2000 Wrapper Configuration:")
message(STATUS "  CUDA version:      ${CUDAToolkit_VERSION}")
message(STATUS "  CUDA architectures: ${CMAKE_CUDA_ARCHITECTURES}")
message(STATUS "  Library output:    ${CMAKE_BINARY_DIR}/lib")
message(STATUS "")
