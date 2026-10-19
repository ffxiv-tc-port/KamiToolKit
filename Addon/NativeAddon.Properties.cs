using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;

namespace KamiToolKit.Addon;

public abstract unsafe partial class NativeAddon {

    public required string InternalName { get; init; } = "NameNotSet";

    public required string Title { get; set; } = "TitleNotSet";

    public string Subtitle { get; set; } = string.Empty;

    public required NativeController NativeController { get; init; }

    public int OpenWindowSoundEffectId { get; set; } = 23;

    public TitleMenuOptions TitleMenuOptions {
        get;
        set {
            field = value;
            if (InternalAddon is not null) {
                UpdateFlags();
            }
        }
    } = new();

    public WindowOptions WindowOptions {
        get;
        set {
            field = value;
            if (InternalAddon is not null) {
                UpdateFlags();
            }
        }
    } = new();

    public required Vector2 Size { get; set; }

    public Vector2 ContentStartPosition => WindowNode.ContentStartPosition + ContentPadding;

    public Vector2 ContentSize => WindowNode.ContentSize - ContentPadding * 2.0f;

    public Vector2 ContentPadding => new(8.0f, 8.0f);

    private Vector2? InternalPosition { get; set; }

    public Vector2 Position {
        get => GetPosition();
        set => InternalPosition = value;
    }

    // 判空與解參要看到同一個值：InternalAddon 是欄位，兩次讀取之間遊戲的 addon 釋放路徑
    // 可能已經把它設回 null（見 NativeAddon.Functions.cs 的 Destructor）。先抄進區域變數，
    // 判空與解參就不會分歧。⚠️ 這只擋得住「判空之後變 null」，擋不住跨執行緒讀取 ——
    // 這些屬性只在 Framework 執行緒上讀才是安全的。
    public bool IsOpen {
        get {
            var addon = InternalAddon;
            return addon is not null && addon->IsVisible;
        }
    }

    public int AddonId {
        get {
            var addon = InternalAddon;
            return addon is null ? 0 : addon->Id;
        }
    }

    public bool RememberClosePosition { get; set; } = true;

    public static explicit operator AtkUnitBase*(NativeAddon addon) => addon.InternalAddon;

    private void SetInitialState() {
        WindowNode.SetTitle(Title, Subtitle);

        InternalAddon->OpenSoundEffectId = (short)OpenWindowSoundEffectId;

        InternalAddon->SetSize((ushort)Size.X, (ushort)Size.Y);
        WindowNode.Size = Size;

        // AtkStage.Instance() 是 [StaticAddress(..., isPointer: true)]：只有「特徵碼沒解析到」才擲例外；
        // 解析到但遊戲尚未建好 AtkStage 時，回傳的是存在該位址的 null 指標。
        // ScreenSize 是 AtkStage 上的內嵌欄位，不是二次指標，所以只要判 Instance() 這一層；
        // 但對 null 指標讀它是 try/catch 攔不到的 AccessViolationException。
        var stage = AtkStage.Instance();
        if (stage is null) {
            // 取不到就跳過置中計算，維持視窗當下的位置；下面的 UpdatePosition() 仍會套用記住的關閉位置。
            // 失敗形式是「視窗開在左上角」—— 看得見但無害，不會影響視窗能不能開。
            Log.Error("ERROR: AtkStage is unavailable. Skipping initial window centering.");
        }
        else {
            var screenSize = new Vector2(stage->ScreenSize.Width, stage->ScreenSize.Height);
            var defaultPosition = screenSize / 2.0f - Size / 2.0f;

            InternalAddon->SetPosition((short)defaultPosition.X, (short)defaultPosition.Y);
        }

        UpdateFlags();
        UpdatePosition();
    }

    private void UpdateFlags() {
        // Note, some flags are default on, need to invert enable to clear them

        FlagHelper.UpdateFlag(ref InternalAddon->Flags1A3, 0x20, WindowOptions.DisableClamping);
        FlagHelper.UpdateFlag(ref InternalAddon->Flags1A3, 0x40, WindowOptions.EnableClickThrough);
        FlagHelper.UpdateFlag(ref InternalAddon->Flags1A3, 0x1, TitleMenuOptions.Enable);
        FlagHelper.UpdateFlag(ref InternalAddon->Flags1A1, 0x4, !TitleMenuOptions.ShowClose);
        FlagHelper.UpdateFlag(ref InternalAddon->Flags1C8, 0x800, !TitleMenuOptions.ShowScale);
    }

    public Vector2 GetPosition() {
        // 這是 public API，可能在 Open() 之前、或在遊戲把 addon 釋放之後被讀到；
        // 兩種情形 InternalAddon 都是 null，直接解參就是 try/catch 攔不到的 AccessViolationException。
        // 沒有原生視窗可讀時，回報「還沒套用的預定位置」比崩潰有用。
        var addon = InternalAddon;
        if (addon is null) return InternalPosition ?? Vector2.Zero;

        return new Vector2(addon->X, addon->Y);
    }

    private void UpdatePosition() {
        if (InternalPosition is { } position) {
            InternalAddon->SetPosition((short)position.X, (short)position.Y);
            InternalPosition = null;
        }
    }
}

public class TitleMenuOptions {

    /// <summary>
    ///     Enables right-clicking on the window header to open the window context menu
    /// </summary>
    public bool Enable { get; set; } = true;

    /// <summary>
    ///     Enable showing a close button in the context menu
    /// </summary>
    public bool ShowClose { get; set; } = true;

    /// <summary>
    ///     Enable showing the scale selector in the window context menu
    /// </summary>
    public bool ShowScale { get; set; } = true;
}

public class WindowOptions {

    /// <summary>
    ///     Setting to <em>True</em> allows the window to be moved past the edge of the window.
    /// </summary>
    public bool DisableClamping { get; set; } = true;

    public bool EnableClickThrough { get; set; }
}
