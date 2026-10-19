using System.IO;
using Dalamud.Interface.Textures.TextureWraps;
using KamiToolKit.Classes;
using KamiToolKit.NodeParts;

namespace KamiToolKit.Nodes;

/// <summary>
///     A simple image node that allows you to load an IDalamudTextureWrap texture into a native image node.
///     This node creates a single <see cref="Part" />
/// </summary>
/// <remarks>This node is not intended to be used with multiple <see cref="Part" />'s.</remarks>
public class ImGuiImageNode : SimpleImageNode {

    public IDalamudTextureWrap? LoadedTexture;

    public override string TexturePath {
        get => base.TexturePath;
        set {
            if (Path.IsPathRooted(value)) {
                LoadTextureFromFile(value);
            }
            else if (DalamudInterface.Instance.DataManager.FileExists(value)) {
                PartsList[0].LoadTexture(value);
            }
        }
    }

    public void LoadTexture(IDalamudTextureWrap texture)
        => PartsList[0].LoadTexture(texture);

    public void LoadTextureFromFile(string fileSystemPath) {
        DalamudInterface.Instance.Framework.RunOnTick(async () => {
            Alpha = 0.0f;

            var texture = await DalamudInterface.Instance.TextureProvider.GetFromFile(fileSystemPath).RentAsync();

            // await 的續行不會留在框架執行緒上：框架執行緒沒有 SynchronizationContext，
            // 而 RunOnTick 的延續是用 TaskContinuationOptions.HideScheduler 排的，
            // 所以這一行之後是執行緒池。底下每一句都會寫進原生節點，必須先轉派回去。
            // IsFrameworkUnloading 為真時 RunOnFrameworkThread 會就地執行（等於沒轉派），先擋掉。
            if (DalamudInterface.Instance.Framework.IsFrameworkUnloading) {
                texture.Dispose();
                return;
            }

            await DalamudInterface.Instance.Framework.RunOnFrameworkThread(() => ApplyTexture(texture));
        });
    }

    /// <summary>
    ///     把載入好的材質寫進原生節點。
    /// </summary>
    /// <remarks>只能在框架執行緒上呼叫。</remarks>
    private unsafe void ApplyTexture(IDalamudTextureWrap texture) {
        // 兩道閘門，缺一不可：
        // 1. IsFrameworkUnloading —— 呼叫端判過之後到轉派之間仍有空隙，
        //    而 RunOnFrameworkThread 在卸載期是「就地執行」。這裡再判一次才保證不在執行緒池上往下走。
        // 2. InternalResNode 為 null —— 載入期間節點可能已經被 Dispose。
        if (DalamudInterface.Instance.Framework.IsFrameworkUnloading || InternalResNode is null) {
            texture.Dispose();
            return;
        }

        LoadedTexture = texture;

        LoadTexture(texture);
        TextureSize = texture.Size;
        Alpha = 1.0f;
        MarkDirty();
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            LoadedTexture?.Dispose();

            base.Dispose(disposing);
        }
    }
}
