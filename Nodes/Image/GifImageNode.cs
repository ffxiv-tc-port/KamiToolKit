using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using KamiToolKit.Classes.TimelineBuilding;
using KamiToolKit.NodeParts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace KamiToolKit.Nodes;

public class GifImageNode : ResNode {

    public ImageNode ImageNode;

    public GifImageNode() {
        ImageNode = new ImageNode {
            IsVisible = true,
        };

        ImageNode.AttachNode(this);
    }

    public required string FilePath {
        set {
            Task.Run(() => LoadFrames(value));
        }
    }

    public override float Width {
        get => base.Width;
        set {
            ImageNode.Width = value;
            base.Width = value;
        }
    }

    public override float Height {
        get => base.Height;
        set {
            ImageNode.Height = value;
            base.Height = value;
        }
    }

    public Vector2 GifFrameSize { get; private set; }

    public bool FitNodeToGif { get; set; }

    public Action? OnGifLoaded { get; set; }

    private async void LoadFrames(string filepath) {
        try {
            var image = await LoadAsync(filepath);
            if (image.Length <= 0) return;

            using var memoryStream = new MemoryStream(image);
            using var processedImage = Image.Load<Rgba32>(memoryStream);
            if (processedImage.Frames.Count is 0) return;

            var frameDelay = processedImage.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay / 3.33333333f;
            var frameCount = (int)(processedImage.Frames.Count * frameDelay);
            var frameSize = new Vector2(processedImage.Width, processedImage.Height);

            // 解碼與建立材質仍然留在背景執行緒（時序不變）；
            // 但凡是會配置原生記憶體或解參 Atk 節點的動作全部搬到 ApplyFrames，
            // 由框架執行緒執行 —— 在執行緒池上碰節點是 AccessViolation，try/catch 攔不到。
            var textures = new List<IDalamudTextureWrap>(processedImage.Frames.Count);

            foreach (var frame in processedImage.Frames) {
                var buffer = new byte[8 * frame.Width * frame.Height];

                frame.CopyPixelDataTo(buffer);

                textures.Add(await DalamudInterface.Instance.TextureProvider.CreateFromRawAsync(RawImageSpecification.Rgba32(frame.Width, frame.Height), buffer));
            }

            // IsFrameworkUnloading 為真時，RunOnFrameworkThread 會「就地執行」而不是轉派，
            // 那等於還在執行緒池上碰節點 —— 所以這裡要自己擋掉，不能指望它幫忙轉派。
            if (DalamudInterface.Instance.Framework.IsFrameworkUnloading) {
                DisposeTextures(textures);
                return;
            }

            await DalamudInterface.Instance.Framework.RunOnFrameworkThread(() => ApplyFrames(textures, frameSize, frameCount));
        }
        catch (Exception e) {
            Log.Exception(e);
        }
    }

    /// <summary>
    ///     組裝節點：配置 AtkUldPart／AtkTimeline、寫入節點欄位、播放動畫。
    /// </summary>
    /// <remarks>只能在框架執行緒上呼叫。</remarks>
    private unsafe void ApplyFrames(List<IDalamudTextureWrap> textures, Vector2 frameSize, int frameCount) {
        // 兩道閘門，缺一不可：
        // 1. IsFrameworkUnloading —— 呼叫端雖然已經判過一次，但那次判斷與轉派之間仍有空隙，
        //    而 RunOnFrameworkThread 在卸載期是「就地執行」。這裡再判一次才保證不會在執行緒池上往下走。
        // 2. InternalResNode 為 null —— 背景載入期間節點可能已經被 Dispose。
        if (DalamudInterface.Instance.Framework.IsFrameworkUnloading || InternalResNode is null) {
            DisposeTextures(textures);
            return;
        }

        GifFrameSize = frameSize;

        if (FitNodeToGif) {
            Size = GifFrameSize;
        }

        uint currentPartId = 0;

        foreach (var texture in textures) {
            var texturePart = new Part {
                Size = texture.Size, Id = currentPartId++,
            };

            texturePart.LoadTexture(texture);
            ImageNode.AddPart(texturePart);
        }

        ImageNode.AddTimeline(new TimelineBuilder()
            .BeginFrameSet(1, frameCount)
            .AddFrame(0, partId: 0)
            .AddFrame(frameCount, partId: currentPartId)
            .EndFrameSet()
            .Build());

        AddTimeline(new TimelineBuilder()
            .BeginFrameSet(1, frameCount)
            .AddLabel(1, 200, AtkTimelineJumpBehavior.Start, 0)
            .AddLabel(frameCount, 0, AtkTimelineJumpBehavior.LoopForever, 200)
            .EndFrameSet()
            .Build());

        if (InternalResNode->Timeline is not null) {
            InternalResNode->Timeline->PlayAnimation(AtkTimelineJumpBehavior.LoopForever, (ushort)200);
        }

        OnGifLoaded?.Invoke();
    }

    /// <summary>
    ///     放棄載入時，把已經建立但還沒有交給任何 Part 的材質釋放掉。
    /// </summary>
    private static void DisposeTextures(List<IDalamudTextureWrap> textures) {
        foreach (var texture in textures) {
            texture.Dispose();
        }

        textures.Clear();
    }

    private static async Task<byte[]> LoadAsync(string path) {
        byte[] data = [];

        if (File.Exists(path)) {
            data = await File.ReadAllBytesAsync(path);
        }

        return data;
    }
}
