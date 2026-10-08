using System;
using System.Collections.Generic;
using System.Linq;

namespace PDFQFZ.Library
{
    internal sealed class StampPlacement
    {
        public StampPlacement(
            int id,
            string documentPath,
            int page,
            float x,
            float y,
            string stampPath,
            int sizeMm,
            int opacity,
            int rotation,
            int whiteTransparencyTolerance,
            bool useWhiteTransparency,
            bool useOriginalRotationCrop,
            bool randomRotation,
            int batchId,
            bool centerRatio = false,
            float offsetXmm = 0f,
            float offsetYmm = 0f,
            bool textureEnabled = false,
            int textureSeed = 0,
            int textureBrightness = 0,
            int textureBlob = 0,
            int textureGradient = 0,
            int textureWhite = 0,
            int textureSpot = 0,
            int textureRadial = 0,
            int textureCast = 0,
            float textureKb = 0.5f,
            float textureKblob = 0.5f,
            float textureKgrad = 0.5f,
            float textureKwhite = 0.5f,
            float textureKspot = 0.5f,
            float textureKradial = 0.5f,
            float textureKcast = 0.5f,
            string type = "manual",
            string keyword = null)
        {
            Id = id;
            DocumentPath = documentPath ?? string.Empty;
            Page = page;
            X = x;
            Y = y;
            StampPath = stampPath ?? string.Empty;
            SizeMm = sizeMm;
            Opacity = opacity;
            Rotation = rotation;
            WhiteTransparencyTolerance = whiteTransparencyTolerance;
            UseWhiteTransparency = useWhiteTransparency;
            UseOriginalRotationCrop = useOriginalRotationCrop;
            RandomRotation = randomRotation;
            BatchId = batchId;
            CenterRatio = centerRatio;
            OffsetXmm = offsetXmm;
            OffsetYmm = offsetYmm;
            TextureEnabled = textureEnabled;
            TextureSeed = textureSeed;
            TextureBrightness = textureBrightness;
            TextureBlob = textureBlob;
            TextureGradient = textureGradient;
            TextureWhite = textureWhite;
            TextureSpot = textureSpot;
            TextureRadial = textureRadial;
            TextureCast = textureCast;
            TextureKb = textureKb;
            TextureKblob = textureKblob;
            TextureKgrad = textureKgrad;
            TextureKwhite = textureKwhite;
            TextureKspot = textureKspot;
            TextureKradial = textureKradial;
            TextureKcast = textureKcast;
            Type = type ?? "manual";
            Keyword = keyword;
        }

        public int Id { get; }
        public string DocumentPath { get; }
        public int Page { get; }
        public float X { get; private set; }
        public float Y { get; private set; }
        public string StampPath { get; }
        public int SizeMm { get; }
        public int Opacity { get; }
        public int Rotation { get; }
        public int WhiteTransparencyTolerance { get; }
        public bool UseWhiteTransparency { get; }
        public bool UseOriginalRotationCrop { get; }
        /// <summary>true=该章角度由随机旋转生成（渲染时按不切边完整显示真实角度，避免大角度被“旋转切边”压缩裁剪）。</summary>
        public bool RandomRotation { get; }
        public int BatchId { get; }
        /// <summary>坐标语义：true=印章中心在页面内的比例（手动/范围页盖章，可超出页面被边界裁剪）；false=印章左上角在"页面减章宽"区间内的比例（按文字盖章，完整在页面内）。</summary>
        public bool CenterRatio { get; }
        /// <summary>随机位移（mm，可正负）：放置时随机生成并固定，预览与输出使用同一值。</summary>
        public float OffsetXmm { get; }
        public float OffsetYmm { get; }
        /// <summary>盖章渲染：true=按四维上限随机生成印泥质感（每次盖章独立种子）。</summary>
        public bool TextureEnabled { get; }
        /// <summary>质感随机种子：放置时生成并固定，预览刷新与输出使用同一纹理。</summary>
        public int TextureSeed { get; set; }
        /// <summary>明暗强度上限 0-100（每章在 0~上限 随机）。</summary>
        public int TextureBrightness { get; set; }
        /// <summary>斑块大小上限 0-100（每章在 0~上限 随机，越大斑块越大）。</summary>
        public int TextureBlob { get; set; }
        /// <summary>渐变上限 0-100（每章在 0~上限 随机，一侧重一侧轻）。</summary>
        public int TextureGradient { get; set; }
        /// <summary>局部露白上限 0-100（每章在 0~上限 随机，印泥薄处接近纸色）。</summary>
        public int TextureWhite { get; set; }
        /// <summary>内部斑点上限 0-100（每章在 0~上限 随机，空白处簇状印泥斑点）。</summary>
        public int TextureSpot { get; set; }
        public int TextureRadial { get; set; }
        public int TextureCast { get; set; }
        /// <summary>印泥浓淡不均强度系数 0~1（盖章时固化；实际强度 = 系数 × 参数上限，调参平滑、章章不同）。</summary>
        public float TextureKb { get; set; }
        /// <summary>斑点大小强度系数 0~1。</summary>
        public float TextureKblob { get; set; }
        /// <summary>渐变强度系数 0~1。</summary>
        public float TextureKgrad { get; set; }
        /// <summary>局部露白强度系数 0~1。</summary>
        public float TextureKwhite { get; set; }
        /// <summary>内部斑点密度系数 0~1。</summary>
        public float TextureKspot { get; set; }
        /// <summary>径向压印强度系数 0~1。</summary>
        public float TextureKradial { get; set; }
        /// <summary>整体色偏强度系数 0~1。</summary>
        public float TextureKcast { get; set; }
        /// <summary>章类型：manual=手动 / range=指定范围页 / batch=批量放置 / text=按文字（V2.4.0.88 数据模型扩展，前端右键弹窗按此分支，不靠 batchId 猜类型）。</summary>
        public string Type { get; }
        /// <summary>按文字章（type=text）的关键词；其他类型为 null（V2.4.0.88）。</summary>
        public string Keyword { get; }
        /// <summary>v2.4.0.50：拖拽移动已盖章——更新页面内比例坐标（0-1，调用方负责钳制）。</summary>
        public void MoveTo(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    internal sealed class StampPlacementCollection
    {
        private readonly List<StampPlacement> placements = new List<StampPlacement>();
        // V2.4.0.81：集合级锁——批量放置（Task.Run）与 UI 线程 Add/Remove/Snapshot 并发安全（批次2-③）
        private readonly object sync = new object();
        private int nextId = 1;
        private int previewBatchCounter = 1;   // V2.4.0.63: preview batch counter (resets with collection rebuild)

        public int Count { get { lock (sync) { return placements.Count; } } }

        public StampPlacement Add(
            string documentPath,
            int page,
            float x,
            float y,
            string stampPath,
            int sizeMm,
            int opacity,
            int rotation,
            int whiteTransparencyTolerance,
            bool useWhiteTransparency,
            bool useOriginalRotationCrop,
            bool randomRotation = false,
            int batchId = 0,
            bool centerRatio = false,
            float offsetXmm = 0f,
            float offsetYmm = 0f,
            bool textureEnabled = false,
            int textureSeed = 0,
            int textureBrightness = 0,
            int textureBlob = 0,
            int textureGradient = 0,
            int textureWhite = 0,
            int textureSpot = 0,
            int textureRadial = 0,
            int textureCast = 0,
            float textureKb = 0.5f,
            float textureKblob = 0.5f,
            float textureKgrad = 0.5f,
            float textureKwhite = 0.5f,
            float textureKspot = 0.5f,
            float textureKradial = 0.5f,
            float textureKcast = 0.5f,
            string type = "manual",
            string keyword = null)
        {
            lock (sync)
            {
            StampPlacement placement = new StampPlacement(
                nextId++,
                documentPath,
                page,
                x,
                y,
                stampPath,
                sizeMm,
                opacity,
                rotation,
                whiteTransparencyTolerance,
                useWhiteTransparency,
                useOriginalRotationCrop,
                randomRotation,
                batchId,
                centerRatio,
                offsetXmm,
                offsetYmm,
                textureEnabled,
                textureSeed,
                textureBrightness,
                textureBlob,
                textureGradient,
                textureWhite,
                textureSpot,
                textureRadial,
                textureCast,
                textureKb,
                textureKblob,
                textureKgrad,
                textureKwhite,
                textureKspot,
                textureKradial,
                textureKcast,
                type,
                keyword);
            placements.Add(placement);
            return placement;
            }
        }

        public IEnumerable<StampPlacement> ForPage(string documentPath, int page)
        {
            // V2.4.0.81：lock 内物化返回——延迟枚举在锁外执行对集合无保护
            lock (sync)
            {
                return placements.Where(item =>
                    string.Equals(item.DocumentPath, documentPath, StringComparison.OrdinalIgnoreCase) &&
                    item.Page == page).ToList();
            }
        }

        /// <summary>v2.4.0.50：拖拽移动——按 id 找到后更新坐标，找不到返回 false。</summary>
        public bool UpdatePosition(int id, float x, float y)
        {
            lock (sync)
            {
                StampPlacement placement = Find(id);
                if (placement == null)
                {
                    return false;
                }

                placement.MoveTo(x, y);
                return true;
            }
        }

        public StampPlacement Find(int id)
        {
            lock (sync) { return placements.FirstOrDefault(item => item.Id == id); }
        }

        public bool Remove(int id)
        {
            lock (sync)
            {
                StampPlacement placement = Find(id);
                return placement != null && placements.Remove(placement);
            }
        }

        public int RemovePage(string documentPath, int page)
        {
            lock (sync)
            {
                return placements.RemoveAll(item =>
                    string.Equals(item.DocumentPath, documentPath, StringComparison.OrdinalIgnoreCase) &&
                    item.Page == page);
            }
        }

        /// <summary>v2.4.0.58：批量预览章约定 batchId=-1（区别于手动 0 / 范围批次 &gt;0）。</summary>
        public const int BatchPreviewId = -1;

        /// <summary>V2.4.0.63：删除该文件的全部批量放置章（batchId&lt;0；批量放置批次号 -1,-2,-3…，负数统一视为批量放置章）。
        /// 只清批量放置产生的章，不误清手动章（0）与范围页章（&gt;0）。</summary>
        public int RemoveBatchPreview(string documentPath)
        {
            lock (sync)
            {
                return placements.RemoveAll(item =>
                    string.Equals(item.DocumentPath, documentPath, StringComparison.OrdinalIgnoreCase) &&
                    item.BatchId < 0);
            }
        }

        public int RemoveBatch(string documentPath, int batchId)
        {
            lock (sync)
            {
                if (batchId <= 0)
                {
                    return 0;
                }

                return placements.RemoveAll(item =>
                    string.Equals(item.DocumentPath, documentPath, StringComparison.OrdinalIgnoreCase) &&
                    item.BatchId == batchId);
            }
        }

        public int RemoveBatchOnPage(string documentPath, int page, int batchId)
        {
            lock (sync)
            {
                if (batchId <= 0)
                {
                    return 0;
                }

                return placements.RemoveAll(item =>
                    string.Equals(item.DocumentPath, documentPath, StringComparison.OrdinalIgnoreCase) &&
                    item.Page == page &&
                    item.BatchId == batchId);
            }
        }

        /// <summary>指定文档中该批次是否仍存在印章。</summary>
        public bool HasBatch(string documentPath, int batchId)
        {
            lock (sync)
            {
                if (batchId <= 0)
                {
                    return false;
                }

                return placements.Any(item =>
                    string.Equals(item.DocumentPath, documentPath, StringComparison.OrdinalIgnoreCase) &&
                    item.BatchId == batchId);
            }
        }

        public int CreateBatchId()
        {
            lock (sync) { return nextId++; }
        }

        /// <summary>V2.4.0.63：批量放置批次号（负值递增 -1,-2,-3…，与范围章正数/手动 0 区分）。
        /// 递增计数器保证每次调用唯一；实例字段随集合重建而从 1 重新开始（重启后自然重置）。</summary>
        public int CreatePreviewBatchId()
        {
            lock (sync) { return -previewBatchCounter++; }
        }

        /// <summary>V2.4.0.63：撤销最近一批批量放置章（LIFO：-1、-2、-3… 中数值最小者为最新批次）。
        /// 删除该批次在所有文件上的章。返回移除数量，remaining 输出剩余批量章数。</summary>
        public int RemoveLatestPreviewBatch(out int remaining)
        {
            lock (sync)
            {
                var previews = placements.Where(p => p.BatchId < 0).ToList();
                if (previews.Count == 0) { remaining = 0; return 0; }
                int latest = previews.Min(p => p.BatchId);
                int removed = placements.RemoveAll(p => p.BatchId == latest);
                remaining = placements.Count(p => p.BatchId < 0);
                return removed;
            }
        }

        /// <summary>V2.4.0.63：删除指定批量放置批次（batchId&lt;0）在所有文件上的章（右键-删除整个批次）。</summary>
        public int RemovePreviewBatch(int batchId)
        {
            lock (sync)
            {
                if (batchId >= 0) return 0;
                return placements.RemoveAll(item => item.BatchId == batchId);
            }
        }

        /// <summary>V2.4.0.63：删除指定文件指定页上指定批次的批量章（右键-仅删除当前页）。</summary>
        public int RemovePreviewBatchOnPage(string documentPath, int page, int batchId)
        {
            lock (sync)
            {
                if (batchId >= 0) return 0;
                return placements.RemoveAll(item =>
                    string.Equals(item.DocumentPath, documentPath, StringComparison.OrdinalIgnoreCase) &&
                    item.Page == page && item.BatchId == batchId);
            }
        }

        public IEnumerable<int> DistinctPages(string documentPath)
        {
            // V2.4.0.81：lock 内物化返回
            lock (sync)
            {
                return placements
                    .Where(item => string.Equals(item.DocumentPath, documentPath, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Page)
                    .Distinct()
                    .OrderBy(page => page).ToList();
            }
        }

        public void Clear()
        {
            lock (sync) { placements.Clear(); }
        }

        /// <summary>V2.4.0.55：快照当前集合（批量处理启动前在 UI 线程调用，避免后台线程并发读 List）。</summary>
        public List<StampPlacement> Snapshot()
        {
            lock (sync) { return placements.ToList(); }
        }

        /// <summary>V2.4.0.55：直接并入一个已存在的章对象（保留全部属性含 BatchId/X/Y/旋转/纹理）——
        /// 批量输出合并预览态章（手动章/范围页章/批量预览章），保证输出与预览一致；渲染层按章自身属性绘制。</summary>
        public void AddExisting(StampPlacement placement)
        {
            if (placement == null) return;
            lock (sync) { placements.Add(placement); }
        }
    }
}
