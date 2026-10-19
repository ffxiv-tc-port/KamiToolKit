using System;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using KamiToolKit.Classes.TimelineBuilding;
using KamiToolKit.Extensions;
using KamiToolKit.Nodes;

namespace KamiToolKit.Addon;

public abstract unsafe partial class NativeAddon {

    private GCHandle? disposeHandle;

    internal AtkUnitBase* InternalAddon;

    public ResNode RootNode = null!;

    public WindowNode WindowNode {
        get;
        set {
            if (value is null) throw new Exception("Cannot set a window node to null");

            if (InternalAddon->WindowNode is not null) {
                InternalAddon->WindowNode = null;
                field.DetachNode();
            }

            field = value;
            value.AttachNode(RootNode, NodePosition.AsFirstChild);
            InternalAddon->WindowNode = value.InternalComponentNode;
        }
    } = null!;

    private void AllocateAddon() {
        if (InternalAddon is not null) {
            Log.Warning("Tried to allocate addon that was already allocated.");
            return;
        }

        var raptureAtkUnitManager = RaptureAtkUnitManager.Instance();
        if (raptureAtkUnitManager is null) {
            Log.Error("ERROR: RaptureAtkUnitManager is unavailable. Aborting allocation.");
            return;
        }

        var currentAddonCount = raptureAtkUnitManager->AllLoadedUnitsList.Count;
        if (currentAddonCount >= 200) {
            Log.Warning("WARNING: Current Addon Count is approaching hard limits. Please ensure custom Addons are not being used as overlays.");
        }

        if (currentAddonCount >= 225) {
            Log.Error("ERROR: Current Addon Count is too high. Aborting allocation.");
            return;
        }

        Log.Verbose($"[{InternalName}] Beginning Native Addon Allocation");

        InternalAddon = NativeMemoryHelper.Create<AtkUnitBase>();

        RegisterVirtualTable();

        InternalAddon->Flags1A2 |= 0b0100_0000; // don't save/load AddonConfig

        RootNode = new ResNode {
            NodeId = 1, NodeFlags = NodeFlags.AnchorTop | NodeFlags.AnchorLeft | NodeFlags.Visible | NodeFlags.Enabled | NodeFlags.Fill | NodeFlags.Focusable | NodeFlags.EmitsEvents,
        };

        WindowNode = new WindowNode();

        InternalAddon->NameString = GetInternalNameSafe();

        InternalAddon->OpenSoundEffectId = (short)OpenWindowSoundEffectId;

        Log.Verbose($"[{InternalName}] Allocation Complete");
    }

    private void InitializeAddon() {
        Log.Verbose($"[{InternalName}] Initializing Addon");

        var widgetInfo = NativeMemoryHelper.UiAlloc<AtkUldWidgetInfo>(1, 16);
        widgetInfo->Id = 1;
        widgetInfo->NodeCount = 0;
        widgetInfo->NodeList = null;
        widgetInfo->WidgetAlignment = new AtkWidgetAlignment {
            AlignmentType = AlignmentType.Center, X = 50.0f, Y = 50.0f,
        };

        InternalAddon->UldManager.Objects = (AtkUldObjectInfo*)widgetInfo;
        InternalAddon->UldManager.ObjectCount = 1;
        InternalAddon->UldManager.ResourceFlags |= AtkUldManagerResourceFlag.ArraysAllocated;

        InternalAddon->RootNode = RootNode.InternalResNode;
        InternalAddon->UldManager.AddNodeToObjectList(RootNode.InternalResNode);

        LoadTimeline();

        InternalAddon->UldManager.UpdateDrawNodeList();
        InternalAddon->UldManager.LoadedState = AtkLoadState.Loaded;

        // UldManager finished loading the uld
        InternalAddon->Flags198 |= 2 << 0x1C;

        // LoadUldByName called
        InternalAddon->Flags1A2 |= 4;

        InternalAddon->UpdateCollisionNodeList(false);

        // Now that we have constructed this instance, track it for auto-dispose
        CreatedAddons.Add(this);

        Log.Verbose($"[{InternalName}] Initialization Complete");
    }

    /// <summary>
    ///     Initializes and Opens this instance of Addon
    /// </summary>
    /// <param name="depthLayer">Which UI layer to attach the Addon to</param>
    public void Open(int depthLayer = 4)
        => DalamudInterface.Instance.Framework.RunOnFrameworkThread(() => {
            Log.Verbose($"[{InternalName}] Open Called");

            if (InternalAddon is null) {
                // 先擋掉才配置：AllocateAddon() 自己雖然也會檢查 RaptureAtkUnitManager，
                // 但若在它成功之後才擋下初始化，會留下「已配置但沒開啟」的半套狀態，
                // 下一次 Open() 會走到 else 分支印「Already open」而永遠開不起來。
                if (RaptureAtkUnitManager.Instance() is null) {
                    Log.Error("ERROR: RaptureAtkUnitManager is unavailable. Aborting open.");
                    return;
                }

                AllocateAddon();

                if (InternalAddon is not null) {
                    // 使用點重查一次，不沿用上面那個指標。
                    // AllocateAddon() 的守衛讓這條路目前走不到 null，
                    // 但那是「前提」不是防護，前提失效時不會有人提醒，所以這裡自己判空。
                    var raptureAtkUnitManager = RaptureAtkUnitManager.Instance();
                    if (raptureAtkUnitManager is null) {
                        Log.Error("ERROR: RaptureAtkUnitManager became unavailable. Aborting addon initialization.");
                        return;
                    }

                    raptureAtkUnitManager->InitializeAddon(InternalAddon, InternalName);

                    // 這一行之後 InternalAddon 還是同一個值，是「前提」不是保證：
                    // InitializeAddon() 會經由我們自己覆寫的 vf1 重入回本類別，
                    // 而遊戲的 addon 釋放路徑會呼叫我們的 vf0（見 NativeAddon.Functions.cs 的 Destructor），
                    // 那裡會把 InternalAddon 設回 null。前提不成立時下一行就是對位址 0 的呼叫。
                    if (InternalAddon is null) {
                        Log.Error("ERROR: Addon was torn down during initialization. Aborting open.");
                        return;
                    }

                    InternalAddon->Open((uint)depthLayer);
                    disposeHandle = GCHandle.Alloc(this);
                }
            }
            else {
                Log.Verbose($"[{InternalName}] Already open, skipping call.");
            }
        });

    /// <summary>
    ///     Closes this instance of Addon
    /// </summary>
    /// <remarks>
    ///     這裡看起來像「跨幀持有原生指標」，但不是：委派捕獲的是 this，
    ///     每次執行都重新讀取 InternalAddon 欄位；而該欄位由我們自己覆寫的 vf0
    ///     （NativeAddon.Functions.cs 的 Destructor）在遊戲釋放這塊記憶體的同一個呼叫框架內設回 null
    ///     ⇒ 欄位非 null 就代表這塊記憶體尚未被釋放（釋放路徑一定會以刪除旗標呼叫 vf0）。
    ///     🔴 這個結論的前提是我們的自訂虛擬表真的裝上去了（RegisterVirtualTable）；
    ///     若哪天改成不覆寫 vf0，這裡就必須改回「每次重新查詢 addon」。
    /// </remarks>
    public void Close()
        => DalamudInterface.Instance.Framework.RunOnFrameworkThread(() => {
            Log.Verbose($"[{InternalName}] Close");

            if (InternalAddon is null) return;
            InternalAddon->Close(false);
        });

    public void Toggle() {
        if (IsOpen) {
            Close();
        }
        else {
            Open();
        }
    }

    private void LoadTimeline() {
        RootNode.AddTimeline(new TimelineBuilder()
            .BeginFrameSet(1, 89)
            .AddLabel(1, 101, AtkTimelineJumpBehavior.PlayOnce, 0)
            .AddLabel(10, 102, AtkTimelineJumpBehavior.PlayOnce, 0)
            .AddLabel(20, 103, AtkTimelineJumpBehavior.PlayOnce, 0)
            .AddLabel(30, 104, AtkTimelineJumpBehavior.PlayOnce, 0)
            .AddLabel(40, 105, AtkTimelineJumpBehavior.PlayOnce, 0)
            .AddLabel(50, 106, AtkTimelineJumpBehavior.PlayOnce, 0)
            .AddLabel(60, 107, AtkTimelineJumpBehavior.PlayOnce, 0)
            .AddLabel(70, 108, AtkTimelineJumpBehavior.PlayOnce, 0)
            .AddLabel(80, 109, AtkTimelineJumpBehavior.PlayOnce, 0)
            .EndFrameSet()
            .Build());
    }

    private string GetInternalNameSafe() {
        var noSpaces = InternalName.Replace(" ", string.Empty);

        if (noSpaces.Length > 31) {
            noSpaces = noSpaces[..31];
        }

        noSpaces += char.MinValue;
        return noSpaces;
    }
}
