using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace KamiToolKit;

/// <summary>
///     Simplified controller for using AddonNamePlate for basic overlays.
/// </summary>
public unsafe class NameplateAddonController : AddonController<AddonNamePlate> {
    public NameplateAddonController(IDalamudPluginInterface pluginInterface) : base(pluginInterface) {
        OnPostEnable += RefreshAddon;
        OnPostDisable += RefreshAddon;
    }

    public override void Dispose() {
        base.Dispose();

        OnPostEnable -= RefreshAddon;
        OnPostDisable -= RefreshAddon;
    }

    private void RefreshAddon(AddonNamePlate* addon) {
        if (addon is not null) {
            if (addon->UldManager.LoadedState is AtkLoadState.Loaded) {
                addon->UldManager.UpdateDrawNodeList();
            }

            addon->UpdateCollisionNodeList(false);

            addon->DoFullUpdate = 1;

            // NamePlateNumberArray.Instance() 是三層鏈：AtkStage.Instance()（isPointer:true，槽內容
            // 合法為 null）→ GetNumberArrayData(NamePlate) → IntArray。CS 版本已對前兩跳判空，
            // 但**取回值本身仍可為 null**（登入前／陣列尚未配置），裸解參考等於寫進位址 0 →
            // AccessViolationException（try/catch 與任何例外隔離包裝一律攔不到）。
            // 這是 addon 啟用／停用回呼路徑（輪詢型），取不到就跳過這次 full update，不寫 log。
            var namePlateNumberArray = NamePlateNumberArray.Instance();
            if (namePlateNumberArray is not null) {
                namePlateNumberArray->DoFullUpdate = true;
            }
        }
    }
}
