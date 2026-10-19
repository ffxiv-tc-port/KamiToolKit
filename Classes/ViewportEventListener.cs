using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;

namespace KamiToolKit.Classes;

public unsafe class ViewportEventListener(AtkEventListener.Delegates.ReceiveEvent eventHandler) : CustomEventListener(eventHandler) {
    public void AddEvent(AtkEventType eventType, AtkResNode* node) {
        DalamudInterface.Instance.Framework.RunOnFrameworkThread(() => {
            // ViewportEventManager 回傳的是 AtkStage 內嵌欄位的真實位址，
            // AtkStage 取不到時為 null。取不到就不註冊。
            var eventManager = Experimental.Instance.ViewportEventManager;
            if (eventManager is null) {
                Log.Error($"ERROR: AtkStage is unavailable. Skipping ViewportEvent registration: {eventType}");
                return;
            }

            Log.Verbose($"Registering ViewportEvent: {eventType}");
            eventManager->RegisterEvent(eventType, 0, node, (AtkEventTarget*)node, EventListener, false);
        });
    }

    public void RemoveEvent(AtkEventType eventType) {
        DalamudInterface.Instance.Framework.RunOnFrameworkThread(() => {
            // 同 AddEvent：取不到 AtkStage 時是 null 指標，不能拿去呼叫 [MemberFunction]。
            var eventManager = Experimental.Instance.ViewportEventManager;
            if (eventManager is null) {
                Log.Error($"ERROR: AtkStage is unavailable. Skipping ViewportEvent unregistration: {eventType}");
                return;
            }

            Log.Verbose($"Unregistering ViewportEvent: {eventType}");
            eventManager->UnregisterEvent(eventType, 0, EventListener, false);
        });
    }

    public override void Dispose() {
        var eventList = new List<Pointer<AtkEvent>>();

        // 🔴 AtkStage 取不到時 ViewportEventManager 是 null，不判空就是裸解參考 null。
        //    取不到時跳過清理，但 base.Dispose() 一定要照樣呼叫（它負責釋放 EventListener 本體）。
        var eventManager = Experimental.Instance.ViewportEventManager;
        if (eventManager is not null) {
            var currentEvent = eventManager->Event;
            while (currentEvent is not null) {
                eventList.Add(currentEvent);
                currentEvent = currentEvent->NextEvent;
            }

            foreach (var atkEvent in eventList) {
                RemoveEvent(atkEvent.Value->State.EventType);
            }
        }

        base.Dispose();
    }
}
