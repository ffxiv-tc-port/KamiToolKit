using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace KamiToolKit.Classes;

public static unsafe class ColorHelper {
    /// <summary>
    ///     AtkUIColorHolder 取不到時的回退色（不透明白色）。
    ///     這些顏色是在節點建構期取的，回退方向刻意選「看得見」而不是「透明」，
    ///     免得失敗形式變成整段文字靜默消失。
    /// </summary>
    private static readonly Vector4 FallbackColor = new(1.0f, 1.0f, 1.0f, 1.0f);

    public static Vector4 GetColor(uint colorId) {
        // AtkStage.Instance() 是 [StaticAddress(..., isPointer: true)]：只有「特徵碼沒解析到」
        // 才會擲例外；解析到但遊戲尚未建好 AtkStage 時，回傳的是存在該位址的 null 指標。
        // AtkUIColorHolder 又是純指標欄位，而 GetColor 是 [MemberFunction]，
        // 拿 null 當 this 呼叫下去會在原生端解參考 → try/catch 攔不到的 AccessViolationException。
        var stage = AtkStage.Instance();
        if (stage is null) return FallbackColor;

        var colorHolder = stage->AtkUIColorHolder;
        if (colorHolder is null) return FallbackColor;

        return ConvertToVector4(colorHolder->GetColor(true, colorId));
    }

    private static Vector4 ConvertToVector4(uint color) {
        var a = (byte)(color >> 24);
        var b = (byte)(color >> 16);
        var g = (byte)(color >> 8);
        var r = (byte)color;

        return new Vector4(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f);
    }
}
