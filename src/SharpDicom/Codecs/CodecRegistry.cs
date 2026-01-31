using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#endif
using SharpDicom.Data;
using SharpDicom.Internal;

namespace SharpDicom.Codecs
{
    /// <summary>
    /// Static registry for pixel data codecs, allowing lookup by transfer syntax.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Codecs register themselves with this registry, either explicitly via
    /// <see cref="Register(IPixelDataCodec)"/> or automatically via
    /// <see cref="RegisterFromAssembly(Assembly)"/>.
    /// </para>
    /// <para>
    /// On .NET 8+, the registry uses FrozenDictionary for lock-free reads after
    /// the first lookup. On older runtimes, a regular dictionary is used.
    /// </para>
    /// <para>
    /// Thread-safety: Registration is protected by locks. Lookups are lock-free
    /// after the registry is frozen (which happens automatically on first lookup).
    /// </para>
    /// <para>
    /// Priority levels: 0 = fallback, 50 = pure C# (default), 100 = native, 200 = user override.
    /// Higher priority codecs override lower priority ones.
    /// </para>
    /// </remarks>
    public static class CodecRegistry
    {
        /// <summary>
        /// Default priority for pure C# codec implementations.
        /// </summary>
        public const int PriorityDefault = 50;

        /// <summary>
        /// Priority for native codec implementations.
        /// </summary>
        public const int PriorityNative = 100;

        /// <summary>
        /// Priority for user-specified overrides.
        /// </summary>
        public const int PriorityUserOverride = 200;

        private static readonly object _lock = new();
        private static Dictionary<TransferSyntax, IPixelDataCodec> _mutableRegistry = new();
        private static Dictionary<TransferSyntax, int> _priorities = new();
        private static bool _isFrozen;

#if NET8_0_OR_GREATER
        private static FrozenDictionary<TransferSyntax, IPixelDataCodec>? _frozenRegistry;
#else
        private static IReadOnlyDictionary<TransferSyntax, IPixelDataCodec>? _frozenRegistry;
#endif

        /// <summary>
        /// Registers a codec instance with the registry using default priority.
        /// </summary>
        /// <param name="codec">The codec to register.</param>
        /// <exception cref="ArgumentNullException"><paramref name="codec"/> is null.</exception>
        public static void Register(IPixelDataCodec codec)
        {
            Register(codec, PriorityDefault);
        }

        /// <summary>
        /// Registers a codec instance with the registry with a specified priority.
        /// </summary>
        /// <param name="codec">The codec to register.</param>
        /// <param name="priority">
        /// Priority level (0=fallback, 50=pure C#, 100=native, 200=user override).
        /// Higher priority codecs override lower priority ones.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="codec"/> is null.</exception>
        /// <remarks>
        /// If a codec with equal or higher priority is already registered for the
        /// same transfer syntax, the new codec is ignored.
        /// </remarks>
        public static void Register(IPixelDataCodec codec, int priority)
        {
            ThrowHelpers.ThrowIfNull(codec, nameof(codec));

            lock (_lock)
            {
                var key = codec.TransferSyntax;

                // Check if a higher or equal priority codec is already registered
                if (_priorities.TryGetValue(key, out var existingPriority) && existingPriority >= priority)
                {
                    // Higher or equal priority already registered, skip
                    return;
                }

                _mutableRegistry[key] = codec;
                _priorities[key] = priority;

                // Invalidate frozen cache if it exists
                if (_isFrozen)
                {
                    _frozenRegistry = null;
                    _isFrozen = false;
                }
            }
        }

        /// <summary>
        /// Registers a codec type with the registry by creating a new instance.
        /// </summary>
        /// <typeparam name="TCodec">The codec type, which must have a parameterless constructor.</typeparam>
        public static void Register<TCodec>() where TCodec : IPixelDataCodec, new()
        {
            Register(new TCodec());
        }

        /// <summary>
        /// Scans an assembly for types implementing <see cref="IPixelDataCodec"/>
        /// with parameterless constructors and registers them.
        /// </summary>
        /// <param name="assembly">The assembly to scan.</param>
        /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is null.</exception>
        /// <remarks>
        /// This method uses reflection and is not compatible with trimming or AOT compilation.
        /// For trim-compatible scenarios, use <see cref="Register(IPixelDataCodec)"/> or
        /// <see cref="Register{TCodec}"/> to register codecs explicitly.
        /// </remarks>
#if NET5_0_OR_GREATER
        [RequiresUnreferencedCode("This method uses reflection to scan for codec types. Use Register(IPixelDataCodec) or Register<TCodec>() for trim-compatible registration.")]
#endif
        public static void RegisterFromAssembly(Assembly assembly)
        {
            ThrowHelpers.ThrowIfNull(assembly, nameof(assembly));

            var codecInterface = typeof(IPixelDataCodec);

            foreach (var type in assembly.GetTypes())
            {
                if (!type.IsClass || type.IsAbstract)
                    continue;

                if (!codecInterface.IsAssignableFrom(type))
                    continue;

                // Check for parameterless constructor
                var constructor = type.GetConstructor(Type.EmptyTypes);
                if (constructor == null)
                    continue;

                try
                {
                    var codec = (IPixelDataCodec)Activator.CreateInstance(type)!;
                    Register(codec);
                }
                catch
                {
                    // Skip codecs that fail to instantiate
                }
            }
        }

        /// <summary>
        /// Gets the codec for the specified transfer syntax, or null if none is registered.
        /// </summary>
        /// <param name="syntax">The transfer syntax to look up.</param>
        /// <returns>The registered codec, or null if not found.</returns>
        public static IPixelDataCodec? GetCodec(TransferSyntax syntax)
        {
            EnsureFrozen();

            if (_frozenRegistry!.TryGetValue(syntax, out var codec))
            {
                return codec;
            }

            return null;
        }

        /// <summary>
        /// Checks if a codec is registered that can decode the specified transfer syntax.
        /// </summary>
        /// <param name="syntax">The transfer syntax to check.</param>
        /// <returns>True if a codec with decode capability is registered; otherwise, false.</returns>
        public static bool CanDecode(TransferSyntax syntax)
        {
            var codec = GetCodec(syntax);
            return codec?.Capabilities.CanDecode == true;
        }

        /// <summary>
        /// Checks if a codec is registered that can encode to the specified transfer syntax.
        /// </summary>
        /// <param name="syntax">The transfer syntax to check.</param>
        /// <returns>True if a codec with encode capability is registered; otherwise, false.</returns>
        public static bool CanEncode(TransferSyntax syntax)
        {
            var codec = GetCodec(syntax);
            return codec?.Capabilities.CanEncode == true;
        }

        /// <summary>
        /// Gets all registered transfer syntaxes.
        /// </summary>
        /// <returns>A collection of registered transfer syntaxes.</returns>
        public static IReadOnlyCollection<TransferSyntax> GetRegisteredTransferSyntaxes()
        {
            EnsureFrozen();
            return _frozenRegistry!.Keys.ToArray();
        }

        /// <summary>
        /// Explicitly freezes the registry for optimal read performance.
        /// </summary>
        /// <remarks>
        /// This is called automatically on first lookup, but can be called explicitly
        /// after all codecs have been registered to optimize subsequent lookups.
        /// </remarks>
        public static void Freeze()
        {
            EnsureFrozen();
        }

        /// <summary>
        /// Gets a value indicating whether the registry is currently frozen.
        /// </summary>
        public static bool IsFrozen
        {
            get
            {
                lock (_lock)
                {
                    return _isFrozen;
                }
            }
        }

        /// <summary>
        /// Clears all registrations and resets the registry to its initial state.
        /// </summary>
        /// <remarks>
        /// This method is primarily intended for testing purposes.
        /// </remarks>
        public static void Reset()
        {
            lock (_lock)
            {
                _mutableRegistry = new Dictionary<TransferSyntax, IPixelDataCodec>();
                _priorities = new Dictionary<TransferSyntax, int>();
                _frozenRegistry = null;
                _isFrozen = false;
            }
        }

        /// <summary>
        /// Gets information about the registered codec for a transfer syntax.
        /// </summary>
        /// <param name="syntax">The transfer syntax to look up.</param>
        /// <returns>Information about the registered codec, or null if not found.</returns>
        public static CodecInfo? GetCodecInfo(TransferSyntax syntax)
        {
            lock (_lock)
            {
                // Read both codec and priority under the same lock to avoid race conditions
                if (!_mutableRegistry.TryGetValue(syntax, out var codec))
                {
                    return null;
                }

                _priorities.TryGetValue(syntax, out var priority);
                var assemblyName = codec.GetType().Assembly.GetName().Name;
                return new CodecInfo(codec.Name, priority, assemblyName);
            }
        }

        /// <summary>
        /// Gets the priority of the registered codec for a transfer syntax.
        /// </summary>
        /// <param name="syntax">The transfer syntax to look up.</param>
        /// <returns>The priority level, or null if no codec is registered.</returns>
        public static int? GetPriority(TransferSyntax syntax)
        {
            lock (_lock)
            {
                if (_priorities.TryGetValue(syntax, out var priority))
                {
                    return priority;
                }
                return null;
            }
        }

        private static void EnsureFrozen()
        {
            if (_isFrozen)
            {
                return;
            }

            lock (_lock)
            {
                if (_isFrozen)
                {
                    return;
                }

#if NET8_0_OR_GREATER
                _frozenRegistry = _mutableRegistry.ToFrozenDictionary();
#else
                _frozenRegistry = new Dictionary<TransferSyntax, IPixelDataCodec>(_mutableRegistry);
#endif
                _isFrozen = true;
            }
        }
    }

    /// <summary>
    /// Information about a registered codec.
    /// </summary>
    /// <param name="Name">The display name of the codec.</param>
    /// <param name="Priority">The priority level at which the codec is registered.</param>
    /// <param name="Assembly">The name of the assembly containing the codec.</param>
    public readonly record struct CodecInfo(string Name, int Priority, string? Assembly);
}
