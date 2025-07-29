using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhotonSystem
{
    /// <summary>
    /// RTManager is responsible for managing RenderTextures and Cubemaps.
    /// It caches created textures to avoid duplicate allocations.
    /// </summary>
    [System.Serializable]
    public class RTManager : PhotonSingleton<RTManager>
    {
        // Cache for RenderTextures
        private Dictionary<string, RenderTexture> _rtCache = new Dictionary<string, RenderTexture>();
        // Cache for Cubemaps
        private Dictionary<string, Cubemap> _cbCache = new Dictionary<string, Cubemap>();
        private Dictionary<string, GraphicsBuffer> _gbCache = new Dictionary<string, GraphicsBuffer>();
        #region RenderTexture
        /// <summary>
        /// Gets or creates a RenderTexture that can be adjusted by width and height.
        /// The key does NOT include width/height. If the cached RT has different size,
        /// it will be released and replaced by a new one.
        /// </summary>
        /// <param name="name">The unique name (not including width/height) for the key.</param>
        /// <param name="width">Requested width.</param>
        /// <param name="height">Requested height.</param>
        /// <param name="format">RenderTexture format.</param>
        /// <param name="wrapMode">Texture wrap mode.</param>
        /// <param name="filterMode">Texture filter mode.</param>
        /// <param name="useMipMap">Whether to use mip maps.</param>
        /// <param name="autoGenerateMips">Whether to auto-generate mip maps (only if useMipMap is true).</param>
        /// <param name="enableRandomWrite">Whether random write is enabled.</param>
        /// <param name="mipCount">Mip map count (if > 0, use the appropriate constructor).</param>
        /// <param name="readWrite">Read/write mode for the RT.</param>
        /// <returns>A RenderTexture that matches the latest requested size and other parameters.</returns>
        public RenderTexture GetAdjustableRT(
            string name,
            int width,
            int height,
            RenderTextureFormat format = RenderTextureFormat.Default,
            TextureWrapMode wrapMode = TextureWrapMode.Repeat,
            FilterMode filterMode = FilterMode.Trilinear,
            bool useMipMap = false,
            bool autoGenerateMips = false,
            bool enableRandomWrite = true,
            int mipCount = 0,
            RenderTextureReadWrite readWrite = RenderTextureReadWrite.Linear,
            int depth = 0,
            TextureDimension dimension = TextureDimension.Tex2D
        )
        {
            // Key does NOT include width/height
            string key = $"{name}_{format}_{wrapMode}_{filterMode}_{useMipMap}_{autoGenerateMips}_{enableRandomWrite}_{mipCount}_{readWrite}_{dimension}";

            RenderTexture rt;
            if (_rtCache.TryGetValue(key, out rt))
            {
                // If the cached RT exists but has different size, release it and create a new one
                if (rt.width != width || rt.height != height)
                {
                    rt.DiscardContents();
                    rt.Release();
                    DestroyImmediate(rt);
                    _rtCache.Remove(key); // remove old RT from cache
                    rt = CreateRenderTexture(key, width, height, format, wrapMode, filterMode,
                        useMipMap, autoGenerateMips, enableRandomWrite, mipCount, readWrite, depth, dimension);
                    _rtCache[key] = rt;
                }
            }
            else
            {
                // If no cached RT for this key, create and cache a new one
                rt = CreateRenderTexture(key, width, height, format, wrapMode, filterMode,
                    useMipMap, autoGenerateMips, enableRandomWrite, mipCount, readWrite, depth, dimension);
                _rtCache[key] = rt;
            }
            return rt;
        }

        /// <summary>
        /// Helper function to create a RenderTexture based on the provided parameters.
        /// </summary>
        private RenderTexture CreateRenderTexture(
            string rtName,
            int width,
            int height,
            RenderTextureFormat format,
            TextureWrapMode wrapMode,
            FilterMode filterMode,
            bool useMipMap,
            bool autoGenerateMips,
            bool enableRandomWrite,
            int mipCount,
            RenderTextureReadWrite readWrite,
            int depth = 0,
            TextureDimension dimension = TextureDimension.Tex2D
        )
        {
            RenderTexture newRT;
            if (mipCount > 0)
            {
                // Use the constructor that takes mipCount
                newRT = new RenderTexture(width, height, depth, format, mipCount)
                {
                    name = rtName,
                    wrapMode = wrapMode,
                    filterMode = filterMode,
                    useMipMap = useMipMap,
                    autoGenerateMips = autoGenerateMips,
                    enableRandomWrite = enableRandomWrite,
                    dimension = dimension
                };
            }
            else
            {
                // Use the constructor that takes readWrite
                newRT = new RenderTexture(width, height, depth, format, readWrite)
                {
                    name = rtName,
                    wrapMode = wrapMode,
                    filterMode = filterMode,
                    useMipMap = useMipMap,
                    autoGenerateMips = autoGenerateMips,
                    enableRandomWrite = enableRandomWrite,
                    dimension = dimension
                };
            }

            newRT.Create();
            return newRT;
        }

        /// <summary>
        /// Release the adjustable RT for a given key (since key doesn't contain width/height).
        /// </summary>
        public void ReleaseAdjustableRT(
            string name,
            RenderTextureFormat format = RenderTextureFormat.Default,
            TextureWrapMode wrapMode = TextureWrapMode.Repeat,
            FilterMode filterMode = FilterMode.Trilinear,
            bool useMipMap = false,
            bool autoGenerateMips = false,
            bool enableRandomWrite = true,
            int mipCount = 0,
            RenderTextureReadWrite readWrite = RenderTextureReadWrite.Linear
        )
        {
            // Key does NOT include width/height
            string key = $"{name}_{format}_{wrapMode}_{filterMode}_{useMipMap}_{autoGenerateMips}_{enableRandomWrite}_{mipCount}_{readWrite}";

            if (_rtCache.TryGetValue(key, out RenderTexture rt))
            {
                rt.DiscardContents();
                rt.Release();
                DestroyImmediate(rt);
                _rtCache.Remove(key);
            }
        }
        /// <summary>
        /// Retrieves or creates a RenderTexture with the specified parameters.
        /// <para>
        /// If <paramref name="height"/> is less than 1, the texture will be square,
        /// using <paramref name="widthHeight"/> for both width and height.
        /// </para>
        /// </summary>
        /// <param name="name">Name or base identifier for the RenderTexture.</param>
        /// <param name="widthHeight">Width of the texture; if height < 1, this value is used for both width and height.</param>
        /// <param name="height">Height of the texture; optional, defaults to -1 (meaning it uses <paramref name="widthHeight"/> for both dimensions).</param>
        /// <param name="format">RenderTexture format. Defaults to <see cref="RenderTextureFormat.Default"/>.</param>
        /// <param name="wrapMode">Texture wrap mode. Defaults to <see cref="TextureWrapMode.Repeat"/>.</param>
        /// <param name="filterMode">Texture filter mode. Defaults to <see cref="FilterMode.Trilinear"/>.</param>
        /// <param name="useMipMap">Whether MipMap is used. Defaults to false.</param>
        /// <param name="autoGenerateMips">Whether MipMaps are automatically generated (only valid if <paramref name="useMipMap"/> is true). Defaults to false.</param>
        /// <param name="enableRandomWrite">Whether random write is enabled. Defaults to true.</param>
        /// <param name="mipCount">MipMap count. If greater than 0, it uses the constructor that includes mipCount. Defaults to 0.</param>
        /// <param name="readWrite">Read/write mode. Defaults to <see cref="RenderTextureReadWrite.Linear"/>.</param>
        /// <returns>A RenderTexture that matches the specified parameters.</returns>
        /// <example>
        /// <code>
        /// // Example usage:
        /// var rt1 = GetRT("MyRT", 1024); 
        /// // Creates a 1024x1024 texture with default settings (Trilinear, Repeat, etc.)
        ///
        /// var rt2 = GetRT("MyRT", 512, 256, RenderTextureFormat.ARGBHalf); 
        /// // Creates a 512x256 texture with ARGBHalf format
        /// </code>
        /// </example>
        public RenderTexture GetRT(
            string name,
            int widthHeight,
            int height = -1,
            RenderTextureFormat format = RenderTextureFormat.Default,
            TextureWrapMode wrapMode = TextureWrapMode.Repeat,
            FilterMode filterMode = FilterMode.Trilinear,
            bool useMipMap = false,
            bool autoGenerateMips = false,
            bool enableRandomWrite = true,
            int mipCount = 0,
            RenderTextureReadWrite readWrite = RenderTextureReadWrite.Linear
        )
        {
            // If height is not specified or is less than 1, treat it as a square texture
            if (height < 1)
            {
                height = widthHeight;
            }

            // Generate a unique key to distinguish different RenderTextures
            string realName = $"{name}_{widthHeight}x{height}_{format}_{wrapMode}_{filterMode}_{useMipMap}_{autoGenerateMips}_{enableRandomWrite}_{mipCount}_{readWrite}";

            // Return the texture from cache if it already exists
            if (_rtCache.ContainsKey(realName))
            {
                return _rtCache[realName];
            }

            RenderTexture newRT;
            // Choose different constructors based on whether mipCount > 0
            if (mipCount > 0)
            {
                newRT = new RenderTexture(widthHeight, height, 0, format, mipCount)
                {
                    wrapMode = wrapMode,
                    filterMode = filterMode,
                    useMipMap = useMipMap,       // UseMipMap only makes sense if it's true
                    autoGenerateMips = autoGenerateMips,
                    enableRandomWrite = enableRandomWrite,
                    name = realName
                };
            }
            else
            {
                newRT = new RenderTexture(widthHeight, height, 0, format, readWrite)
                {
                    wrapMode = wrapMode,
                    filterMode = filterMode,
                    useMipMap = useMipMap,
                    autoGenerateMips = autoGenerateMips,
                    enableRandomWrite = enableRandomWrite,
                    name = realName
                };
            }

            newRT.Create();
            _rtCache[realName] = newRT;

            return newRT;
        }

        /// <summary>
        /// Releases a RenderTexture based on the same parameters used to create it.
        /// </summary>
        /// <param name="name">Name or base identifier used when the RenderTexture was created.</param>
        /// <param name="widthHeight">Width used when the RenderTexture was created.</param>
        /// <param name="height">Height used when the RenderTexture was created. If less than 1, it implies a square texture.</param>
        /// <param name="format">RenderTexture format.</param>
        /// <param name="wrapMode">Texture wrap mode.</param>
        /// <param name="filterMode">Texture filter mode.</param>
        /// <param name="useMipMap">Indicates if MipMap was used.</param>
        /// <param name="autoGenerateMips">Indicates if MipMaps were automatically generated.</param>
        /// <param name="enableRandomWrite">Indicates if random write was enabled.</param>
        /// <param name="mipCount">MipMap count used during creation.</param>
        /// <param name="readWrite">Read/write mode used during creation.</param>
        public void ReleaseRT(
            string name,
            int widthHeight,
            int height,
            RenderTextureFormat format = RenderTextureFormat.Default,
            TextureWrapMode wrapMode = TextureWrapMode.Repeat,
            FilterMode filterMode = FilterMode.Trilinear,
            bool useMipMap = false,
            bool autoGenerateMips = false,
            bool enableRandomWrite = true,
            int mipCount = 0,
            RenderTextureReadWrite readWrite = RenderTextureReadWrite.Linear
        )
        {
            // If height is not specified or is less than 1, treat it as a square texture
            if (height < 1)
            {
                height = widthHeight;
            }

            string realName = $"{name}_{widthHeight}x{height}_{format}_{wrapMode}_{filterMode}_{useMipMap}_{autoGenerateMips}_{enableRandomWrite}_{mipCount}_{readWrite}";

            if (_rtCache.ContainsKey(realName))
            { 
                _rtCache[realName].DiscardContents();
                _rtCache[realName].Release();
                DestroyImmediate(_rtCache[realName]);
                _rtCache.Remove(realName);
            }
        }

        /// <summary>
        /// Releases a specific RenderTexture instance directly.
        /// </summary>
        /// <param name="renderTexture">RenderTexture instance to be released.</param>
        public void ReleaseRT(RenderTexture renderTexture)
        {
            string keyToRemove = null;
            foreach (var kv in _rtCache)
            {
                if (kv.Value == renderTexture)
                {
                    kv.Value.DiscardContents();
                    kv.Value.Release();
                    DestroyImmediate(kv.Value);
                    keyToRemove = kv.Key;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(keyToRemove))
            {
                _rtCache.Remove(keyToRemove);
            }
        }
        #endregion
        #region Cubemap Methods

        /// <summary>
        /// Retrieves or creates a Cubemap with the specified parameters.
        /// </summary>
        /// <param name="name">Name or base identifier for the Cubemap.</param>
        /// <param name="size">Size of the Cubemap (width/height of each face).</param>
        /// <param name="format">TextureFormat for the Cubemap. Defaults to <see cref="TextureFormat.RGBA32"/>.</param>
        /// <param name="wrapMode">Texture wrap mode. Defaults to <see cref="TextureWrapMode.Repeat"/>.</param>
        /// <param name="filterMode">Texture filter mode. Defaults to <see cref="FilterMode.Trilinear"/>.</param>
        /// <returns>A Cubemap that matches the specified parameters.</returns>
        public Cubemap GetCubemap(
            string name,
            int size,
            TextureFormat format = TextureFormat.RGBA32,
            TextureWrapMode wrapMode = TextureWrapMode.Repeat,
            FilterMode filterMode = FilterMode.Trilinear
        )
        {
            string realName = $"{name}_{size}_{format}_{wrapMode}_{filterMode}";
            if (_cbCache.ContainsKey(realName))
            {
                return _cbCache[realName];
            }

            Cubemap newCB = new Cubemap(size, format, false)
            {
                wrapMode = wrapMode,
                filterMode = filterMode,
                name = realName
            };

            _cbCache[realName] = newCB;
            return newCB;
        }

        /// <summary>
        /// Releases a Cubemap identified by the same parameters used when it was created.
        /// </summary>
        /// <param name="name">Name or base identifier for the Cubemap.</param>
        /// <param name="size">Size of the Cubemap.</param>
        /// <param name="format">TextureFormat used during creation.</param>
        /// <param name="wrapMode">Texture wrap mode used during creation.</param>
        /// <param name="filterMode">Texture filter mode used during creation.</param>
        public void ReleaseCubemap(
            string name,
            int size,
            TextureFormat format,
            TextureWrapMode wrapMode = TextureWrapMode.Repeat,
            FilterMode filterMode = FilterMode.Trilinear
        )
        {
            string realName = $"{name}_{size}_{format}_{wrapMode}_{filterMode}";
            if (_cbCache.ContainsKey(realName))
            {
                var cubemap = _cbCache[realName];
                _cbCache.Remove(realName);
                if (cubemap != null)
                {
                    Object.DestroyImmediate(cubemap);
                }
            }
        }

        /// <summary>
        /// Releases a specific Cubemap instance directly.
        /// </summary>
        /// <param name="cubemap">Cubemap instance to be released.</param>
        public void ReleaseCubemap(Cubemap cubemap)
        {
            string keyToRemove = null;
            foreach (var kv in _cbCache)
            {
                if (kv.Value == cubemap)
                {
                    keyToRemove = kv.Key;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(keyToRemove))
            {
                _cbCache.Remove(keyToRemove);
                Object.DestroyImmediate(cubemap);
            }
        }

        #endregion
        #region GraphicsBuffer Methods

        public GraphicsBuffer GetAdjustableGB(
            string name,
            int count,
            int stride,
            GraphicsBuffer.Target target =
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Raw |
                GraphicsBuffer.Target.Append)
        {
            string key = $"{name}_{target}_{stride}"; 

            if (_gbCache.TryGetValue(key, out var gb))
            {
                if (gb.count != count)
                {
                    gb.Release();
                    gb.Dispose();
                    _gbCache.Remove(key);
                    gb = CreateGraphicsBuffer(key, count, stride, target);
                    _gbCache[key] = gb;
                }
            }
            else
            {
                gb = CreateGraphicsBuffer(key, count, stride, target);
                _gbCache[key] = gb;
            }

            return gb;
        }

        private static GraphicsBuffer CreateGraphicsBuffer(
            string gbName,
            int count,
            int stride,
            GraphicsBuffer.Target target)
        {
            var gb = new GraphicsBuffer(target, count, stride)
            {
#if UNITY_EDITOR
                name = gbName
#endif
            };
            return gb;
        }

        public GraphicsBuffer GetGB(
            string name,
            int count,
            int stride,
            GraphicsBuffer.Target target =
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Raw)
        {
            string realName = $"{name}_{count}_{stride}_{target}";

            if (_gbCache.TryGetValue(realName, out var gb))
                return gb;

            gb = CreateGraphicsBuffer(realName, count, stride, target);
            _gbCache[realName] = gb;
            return gb;
        }

        public void ReleaseGB(
            string name,
            int count,
            int stride,
            GraphicsBuffer.Target target =
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Raw)
        {
            string realName = $"{name}_{count}_{stride}_{target}";

            if (_gbCache.TryGetValue(realName, out var gb))
            {
                gb.Dispose();
                _gbCache.Remove(realName);
            }
        }


        public void ReleaseGB(GraphicsBuffer buffer)
        {
            string keyToRemove = null;
            foreach (var kv in _gbCache)
            {
                if (kv.Value == buffer)
                {
                    keyToRemove = kv.Key;
                    break;
                }
            }

            if (keyToRemove != null)
            {
                _gbCache[keyToRemove].Release();
                _gbCache[keyToRemove].Dispose();
                _gbCache.Remove(keyToRemove);
            }
        }

        #endregion
        #region Buffer
        /// <summary>
        /// 按 <paramref name="name"/>+<paramref name="type"/> 作为 key，
        /// 返回一个<strong>可变大小</strong>的 ComputeBuffer：
        /// <para>1. 若已存在但 <c>count</c> 不同，则释放旧 Buffer 并重新创建；</para>
        /// <para>2. 若不存在，则直接创建并缓存；</para>
        /// </summary>
        public ComputeBuffer GetAdjustableCB(
            string name,
            int count,
            int stride,
            ComputeBufferType type = ComputeBufferType.Structured)
        {
            // key 不包含 count，以便后续检查 count 差异时能替换
            string key = $"{name}_{type}_{stride}";

            if (_computeBufferCache.TryGetValue(key, out var cb))
            {
                if (cb.count != count)
                {
                    cb.Release();
                    _computeBufferCache.Remove(key);

                    cb = CreateComputeBuffer(count, stride, type);
                    _computeBufferCache[key] = cb;
                }
            }
            else
            {
                cb = CreateComputeBuffer(count, stride, type);
                _computeBufferCache[key] = cb;
            }

            return cb;
        }

        /// <summary>
        /// 按完全限定 key（包含 <c>count</c>）创建 / 获取不可变大小的 ComputeBuffer。  
        /// 用于你<strong>确定</strong>缓冲大小不会变化的情况（如静态 LUT）。
        /// </summary>
        public ComputeBuffer GetCB(
            string name,
            int count,
            int stride,
            ComputeBufferType type = ComputeBufferType.Structured)
        {
            string realKey = $"{name}_{count}_{stride}_{type}";

            if (_computeBufferCache.TryGetValue(realKey, out var cb))
                return cb;

            cb = CreateComputeBuffer(count, stride, type);
            _computeBufferCache[realKey] = cb;
            return cb;
        }

        /// <summary>释放按 <paramref name="name"/>+<paramref name="type"/>（可变大小）缓存的 ComputeBuffer。</summary>
        public void ReleaseAdjustableCB(
            string name,
            ComputeBufferType type = ComputeBufferType.Structured,
            int stride = 0)                 // stride 仅用来区分同名不同 stride 的情况
        {
            string key = $"{name}_{type}_{stride}";
            if (_computeBufferCache.TryGetValue(key, out var cb))
            {
                cb.Release();
                _computeBufferCache.Remove(key);
            }
        }

        /// <summary>释放按完整 key（含 <c>count</c>）缓存的 ComputeBuffer。</summary>
        public void ReleaseCB(
            string name,
            int count,
            int stride,
            ComputeBufferType type = ComputeBufferType.Structured)
        {
            string realKey = $"{name}_{count}_{stride}_{type}";
            if (_computeBufferCache.TryGetValue(realKey, out var cb))
            {
                cb.Release();
                _computeBufferCache.Remove(realKey);
            }
        }

        /// <summary>直接通过实例释放 ComputeBuffer（自动寻找并移除 cache 项）。</summary>
        public void ReleaseCB(ComputeBuffer buffer)
        {
            string keyToRemove = null;
            foreach (var kv in _computeBufferCache)
            {
                if (kv.Value == buffer)
                {
                    keyToRemove = kv.Key;
                    break;
                }
            }

            if (keyToRemove != null)
            {
                _computeBufferCache[keyToRemove].Release();
                _computeBufferCache.Remove(keyToRemove);
            }
        }

        /// <summary>内部统一创建 ComputeBuffer。</summary>
        private static ComputeBuffer CreateComputeBuffer(
            int count,
            int stride,
            ComputeBufferType type)
        {
            var cb = new ComputeBuffer(count, stride, type);
            return cb;
        }
        #endregion
        // ────────────────────────────────────────────────────────────
        // 字段：ComputeBuffer 缓存字典（放在类字段区域）
        // ────────────────────────────────────────────────────────────
        private readonly Dictionary<string, ComputeBuffer> _computeBufferCache =
            new Dictionary<string, ComputeBuffer>();



        #region Reset/Destroy

        /// <summary>
        /// Resets the manager by releasing all cached RenderTextures and Cubemaps.
        /// </summary>
        public void ResetDic()
        {
            // ─ RenderTextures
            foreach (var kv in _rtCache)
            {
                kv.Value.DiscardContents();
                kv.Value.Release();
                DestroyImmediate(kv.Value);
            }
            _rtCache.Clear();

            // ─ Cubemaps
            foreach (var kv in _cbCache)
                DestroyImmediate(kv.Value);
            _cbCache.Clear();

            // ─ GraphicsBuffers
            foreach (var kv in _gbCache)
            {
                kv.Value.Release();
                kv.Value.Dispose();
            }

            _gbCache.Clear();
        }

        /// <summary>
        /// Resets the system (override from PhotonSingleton).
        /// </summary>
        public override void ResetSystem()
        {
            ResetDic();
        }

        /// <summary>
        /// Destroys the system (override from PhotonSingleton).
        /// </summary>
        public override void DestroySystem()
        {
            ResetDic();
        }

        #endregion
    }
}
